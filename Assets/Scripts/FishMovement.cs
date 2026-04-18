using System.Collections.Generic;
using UnityEngine;

public class FishMovement : MonoBehaviour
{
    public static readonly List<FishMovement> ActiveFish = new List<FishMovement>();
    public static FishSpatialGrid SpatialGrid { get; private set; }
    private static Vector3[] cachedPositions = System.Array.Empty<Vector3>();
    private static Vector3[] cachedForwards = System.Array.Empty<Vector3>();
    private static int cachedCount;
    private readonly List<int> gridQueryResults = new List<int>();

    public static Vector3[] CachedPositions => cachedPositions;
    public static int CachedCount => cachedCount;

    public static void SetSpatialGrid(FishSpatialGrid grid)
    {
        SpatialGrid = grid;
    }

    public static void UpdateCache()
    {
        int count = ActiveFish.Count;
        if (cachedPositions.Length < count)
        {
            int newSize = Mathf.Max(count, 64);
            cachedPositions = new Vector3[newSize];
            cachedForwards = new Vector3[newSize];
        }

        cachedCount = count;
        for (int i = 0; i < count; i++)
        {
            FishMovement fish = ActiveFish[i];
            if (fish != null)
            {
                Transform t = fish.transform;
                cachedPositions[i] = t.position;
                cachedForwards[i] = t.forward;
            }
        }
    }

    [Header("Movement")]
    [SerializeField] private bool startMovingOnSpawn = false;
    [SerializeField] private float idleSpeed = 1.5f; // slow drift when no predator nearby
    [SerializeField] private float burstSpeed = 2.5f; // burst speed when fleeing predator
    [SerializeField] private float turnSpeed = 6f;

    [Header("Wiggle")]
    [SerializeField] private float wiggleInterval = 0.75f;
    [SerializeField] private float maxWiggleAngle = 15f;
    [SerializeField] private float maxPitchWiggleAngle = 50f;

    [Header("Schooling")]
    [SerializeField] private float schoolingStrength = 0.7f;
    [SerializeField] private float preferredNeighborDistance = 1.1f;
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private float maxSchoolingForce = 1.5f;
    [SerializeField] private float alignmentStrength = 0.75f;

    [Header("Separation")]
    [SerializeField] private float separationRadius = 0.7f;
    [SerializeField] private float separationStrength = 3f;
    [SerializeField] private float maxSeparationForce = 2.5f;

    [Header("Perception")]
    [SerializeField, Range(0f, 180f)] private float followConeAngle = 110f;
    [SerializeField] private Vector3 followConeOriginLocalOffset = new Vector3(0f, 0f, -0.35f);

    [Header("Tank Avoidance")]
    [SerializeField] private float wallAvoidanceDistance = 1.25f;
    [SerializeField] private float wallAvoidanceStrength = 2f;

    [Header("Predator Avoidance")]
    [SerializeField] private float predatorAvoidanceRadius = 3f;
    [SerializeField] private float predatorAvoidanceStrength = 50f;
    [SerializeField] private float maxPredatorAvoidanceForce = 40f;
    [SerializeField, Range(0f, 1f)] private float lateralFleeBlend = 1f; // 0 = flee directly away, 1 = flee fully sideways

    public float IdleSpeed { get => idleSpeed; set => idleSpeed = value; }
    public float TurnSpeedValue { get => turnSpeed; set => turnSpeed = value; }
    public float BurstSpeed { get => burstSpeed; set => burstSpeed = value; }
    public float PredatorAvoidanceRadiusValue { get => predatorAvoidanceRadius; set => predatorAvoidanceRadius = value; }
    public float PredatorAvoidanceStrengthValue { get => predatorAvoidanceStrength; set => predatorAvoidanceStrength = value; }
    public float MaxPredatorAvoidanceForceValue { get => maxPredatorAvoidanceForce; set => maxPredatorAvoidanceForce = value; }
    public float LateralFleeBlendValue { get => lateralFleeBlend; set => lateralFleeBlend = value; }

    [Header("Debug")]
    [Tooltip("Draw an arrow in the Scene view showing each fish's desired movement direction.")]
    public static bool showDebugArrows = true;
    [SerializeField] private float debugArrowLength = 1.5f;

    private bool isMoving;
    private bool movementStateInitialized;
    private Quaternion targetRotation;
    private float wiggleTimer;
    private Quaternion wiggleOffsetRotation = Quaternion.identity;
    private Vector3 tankCenter;
    private Vector3 tankExtents = Vector3.one;
    private bool hasTankBounds;
    private Vector3 lastDesiredForward;

    private void OnEnable()
    {
        if (!ActiveFish.Contains(this))
        {
            ActiveFish.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveFish.Remove(this);
    }

    private void Start()
    {
        if (!movementStateInitialized)
        {
            isMoving = startMovingOnSpawn;
            movementStateInitialized = true;
        }

        targetRotation = transform.rotation;
        wiggleTimer = Random.Range(0f, GetSafeWiggleInterval());
        wiggleOffsetRotation = Quaternion.identity;
    }

    public void UpdateMovement(float deltaTime, bool recalculateForces)
    {
        if (!isMoving)
        {
            return;
        }

        wiggleTimer += deltaTime;
        float interval = GetSafeWiggleInterval();
        if (wiggleTimer >= interval)
        {
            wiggleTimer -= interval;
            QueueRandomWiggle();
        }

        if (recalculateForces)
        {
            Vector3 schoolingForce = CalculateSchoolingForce();
            Vector3 predatorAvoidanceForce = CalculatePredatorAvoidanceForce();
            Vector3 combinedForces = schoolingForce + predatorAvoidanceForce;
            Vector3 wallAvoidanceForce = CalculateWallAvoidanceForce(combinedForces);
            Vector3 desiredForward = transform.forward + combinedForces + wallAvoidanceForce;
            desiredForward = wiggleOffsetRotation * desiredForward;

            if (desiredForward.sqrMagnitude > 0.0001f)
            {
                lastDesiredForward = desiredForward.normalized;
                targetRotation = Quaternion.LookRotation(lastDesiredForward, Vector3.up);
            }
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);

        // scale speed based on closest predator proximity
        float currentSpeed = idleSpeed;
        for (int i = 0; i < PredatorMovement.ActivePredators.Count; i++)
        {
            PredatorMovement predator = PredatorMovement.ActivePredators[i];
            if (predator == null) continue;
            float dist = Vector3.Distance(transform.position, predator.transform.position);
            if (dist < predatorAvoidanceRadius)
            {
                float urgency = 1f - Mathf.Clamp01(dist / predatorAvoidanceRadius);
                float speed = Mathf.Lerp(idleSpeed, burstSpeed, urgency);
                if (speed > currentSpeed) currentSpeed = speed;
            }
        }

        transform.position += transform.forward * (currentSpeed * deltaTime);

        if (hasTankBounds)
        {
            Vector3 pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, tankCenter.x - tankExtents.x, tankCenter.x + tankExtents.x);
            pos.y = Mathf.Clamp(pos.y, tankCenter.y - tankExtents.y, tankCenter.y + tankExtents.y);
            pos.z = Mathf.Clamp(pos.z, tankCenter.z - tankExtents.z, tankCenter.z + tankExtents.z);
            transform.position = pos;
        }
    }

    public void SetMovementActive(bool active)
    {
        isMoving = active;
        movementStateInitialized = true;
        targetRotation = transform.rotation;
        wiggleTimer = Random.Range(0f, GetSafeWiggleInterval());
        wiggleOffsetRotation = Quaternion.identity;
    }

    public void SetTankBounds(Vector3 center, Vector3 extents)
    {
        tankCenter = center;
        tankExtents = new Vector3(
            Mathf.Max(0.01f, extents.x),
            Mathf.Max(0.01f, extents.y),
            Mathf.Max(0.01f, extents.z));
        hasTankBounds = true;
    }

    private void QueueRandomWiggle()
    {
        float yawWiggleAngle = Random.Range(-maxWiggleAngle, maxWiggleAngle);
        float pitchWiggleAngle = Random.Range(-maxPitchWiggleAngle, maxPitchWiggleAngle);
        Quaternion yawRotation = Quaternion.AngleAxis(yawWiggleAngle, Vector3.up);
        Quaternion pitchRotation = Quaternion.AngleAxis(pitchWiggleAngle, transform.right);
        wiggleOffsetRotation = pitchRotation * yawRotation;
    }

    private Vector3 CalculateSchoolingForce()
    {
        bool allowSchooling = schoolingStrength > 0f;
        if (!allowSchooling && separationStrength <= 0f)
        {
            return Vector3.zero;
        }

        float safePreferredDistance = Mathf.Max(0.01f, preferredNeighborDistance);
        float safeInteractionRadius = Mathf.Max(0f, interactionRadius);
        float safeSeparationRadius = Mathf.Max(0.01f, separationRadius);
        float maxRadiusSqr = Mathf.Max(safeInteractionRadius, safeSeparationRadius);
        maxRadiusSqr *= maxRadiusSqr;

        Vector3 hardSeparationForce = Vector3.zero;
        Vector3 weightedNeighborPositionSum = Vector3.zero;
        Vector3 weightedNeighborForwardSum = Vector3.zero;
        float visibleNeighborWeightSum = 0f;
        Vector3 myPosition = transform.position;
        Vector3 myForward = transform.forward;
        Vector3 coneOrigin = GetFollowConeOrigin();
        float minimumDot = GetFollowConeMinimumDot();

        float queryRadius = Mathf.Max(safeInteractionRadius, safeSeparationRadius);
        bool useGrid = SpatialGrid != null;

        if (useGrid)
        {
            SpatialGrid.QueryRadius(myPosition, queryRadius, gridQueryResults);
        }

        int candidateCount = useGrid ? gridQueryResults.Count : cachedCount;
        for (int c = 0; c < candidateCount; c++)
        {
            int i = useGrid ? gridQueryResults[c] : c;
            if (ActiveFish[i] == this) continue;

            Vector3 otherPosition = cachedPositions[i];
            Vector3 displacement = myPosition - otherPosition;
            float sqrDist = displacement.sqrMagnitude;

            if (sqrDist <= 0.00000001f)
            {
                hardSeparationForce += GetFallbackSeparationDirection(ActiveFish[i]);
                continue;
            }

            if (sqrDist > maxRadiusSqr) continue;

            float distance = Mathf.Sqrt(sqrDist);

            if (distance < safeSeparationRadius)
            {
                Vector3 separationDirection = displacement / distance;
                float separationPressure = 1f - Mathf.Clamp01(distance / safeSeparationRadius);
                hardSeparationForce += separationDirection * separationPressure * separationPressure;
            }

            if (safeInteractionRadius > 0f && distance > safeInteractionRadius)
            {
                continue;
            }

            if (!allowSchooling)
            {
                continue;
            }

            // Inlined cone check + weight calculation
            Vector3 toOtherFromConeOrigin = otherPosition - coneOrigin;
            float coneSqrDist = toOtherFromConeOrigin.sqrMagnitude;

            if (coneSqrDist <= 0.0001f)
            {
                float dw = 1f / (1f + 0.01f);
                weightedNeighborPositionSum += otherPosition * dw;
                weightedNeighborForwardSum += cachedForwards[i] * dw;
                visibleNeighborWeightSum += dw;
                continue;
            }

            float coneDistance = Mathf.Sqrt(coneSqrDist);
            Vector3 directionToOther = toOtherFromConeOrigin / coneDistance;
            float forwardDot = Vector3.Dot(myForward, directionToOther);

            if (forwardDot < minimumDot) continue;

            float directionWeight = Mathf.InverseLerp(minimumDot, 1f, forwardDot);
            float distanceWeight = 1f / (1f + coneDistance);
            float neighborWeight = directionWeight * distanceWeight;

            weightedNeighborPositionSum += otherPosition * neighborWeight;
            weightedNeighborForwardSum += cachedForwards[i] * neighborWeight;
            visibleNeighborWeightSum += neighborWeight;
        }

        Vector3 cohesionForce = Vector3.zero;
        if (allowSchooling && visibleNeighborWeightSum > 0f)
        {
            Vector3 localCenter = weightedNeighborPositionSum / visibleNeighborWeightSum;
            cohesionForce = (localCenter - myPosition) / safePreferredDistance;
        }

        Vector3 alignmentForce = Vector3.zero;
        if (allowSchooling && visibleNeighborWeightSum > 0f)
        {
            Vector3 averageForward = weightedNeighborForwardSum / visibleNeighborWeightSum;
            if (averageForward.sqrMagnitude > 0.0001f)
            {
                alignmentForce = averageForward.normalized * Mathf.Max(0f, alignmentStrength);
            }
        }

        Vector3 separationForce = Vector3.ClampMagnitude(hardSeparationForce * Mathf.Max(0f, separationStrength), Mathf.Max(0f, maxSeparationForce));
        Vector3 schoolingForce = (cohesionForce * Mathf.Max(0f, schoolingStrength)) + alignmentForce;
        schoolingForce = Vector3.ClampMagnitude(schoolingForce, Mathf.Max(0f, maxSchoolingForce));
        return separationForce + schoolingForce;
    }


    public bool IsPredatorNearby()
    {
        for (int i = 0; i < PredatorMovement.ActivePredators.Count; i++)
        {
            PredatorMovement predator = PredatorMovement.ActivePredators[i];
            if (predator == null) continue;

            float dist = Vector3.Distance(transform.position, predator.transform.position);
            if (dist < predatorAvoidanceRadius) return true;
        }

        return false;
    }


    private Vector3 CalculatePredatorAvoidanceForce()
    {
        Vector3 avoidanceForce = Vector3.zero;

        for (int i = 0; i < PredatorMovement.ActivePredators.Count; i++)
        {
            PredatorMovement predator = PredatorMovement.ActivePredators[i];
            if (predator == null) continue;

            Vector3 toPredator = predator.transform.position - transform.position;
            float distance = toPredator.magnitude;

            if (distance < predatorAvoidanceRadius && distance > 0.0001f)
            {
                float pressure = 1f - Mathf.Clamp01(distance / predatorAvoidanceRadius);

                Vector3 awayDir = -toPredator.normalized;

                // lateral direction based on shark's charge heading
                // sign determined by which side of the shark the fish is on
                Vector3 sharkForward = predator.transform.forward;
                Vector3 sharkRight = Vector3.Cross(sharkForward, Vector3.up);
                if (sharkRight.sqrMagnitude < 0.0001f)
                {
                    sharkRight = Vector3.Cross(sharkForward, Vector3.right);
                }
                sharkRight = sharkRight.normalized;

                // fish on the shark's right side flees right, left side flees left
                float sign = Vector3.Dot(-toPredator, sharkRight) >= 0f ? 1f : -1f;
                Vector3 lateralDir = sharkRight * sign;

                Vector3 fleeDir = Vector3.Lerp(awayDir, lateralDir, lateralFleeBlend).normalized;
                avoidanceForce += fleeDir * pressure;
            }
        }

        return Vector3.ClampMagnitude(avoidanceForce * predatorAvoidanceStrength, maxPredatorAvoidanceForce);
    }

    private Vector3 GetFallbackSeparationDirection(FishMovement other)
    {
        float seed = (GetInstanceID() ^ other.GetInstanceID()) * 0.0174533f;
        Vector3 fallback = new Vector3(
            Mathf.Sin(seed),
            Mathf.Sin((seed * 1.7f) + 1.3f),
            Mathf.Sin((seed * 2.3f) + 2.1f));

        if (fallback.sqrMagnitude <= 0.0001f)
        {
            fallback = transform.right;
        }

        return fallback.normalized;
    }

    private Vector3 CalculateWallAvoidanceForce(Vector3 schoolingForce)
    {
        if (!hasTankBounds || wallAvoidanceStrength <= 0f)
        {
            return Vector3.zero;
        }

        float safeAvoidanceDistance = Mathf.Max(0.01f, wallAvoidanceDistance);
        Vector3 localPosition = transform.position - tankCenter;
        Vector3 inwardWallNormal = Vector3.zero;
        float maxWallPressure = 0f;

        AccumulateWallPressure(Vector3.right, tankExtents.x + localPosition.x, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);
        AccumulateWallPressure(Vector3.left, tankExtents.x - localPosition.x, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);
        AccumulateWallPressure(Vector3.up, tankExtents.y + localPosition.y, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);
        AccumulateWallPressure(Vector3.down, tankExtents.y - localPosition.y, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);
        AccumulateWallPressure(Vector3.forward, tankExtents.z + localPosition.z, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);
        AccumulateWallPressure(Vector3.back, tankExtents.z - localPosition.z, safeAvoidanceDistance, ref inwardWallNormal, ref maxWallPressure);

        if (inwardWallNormal.sqrMagnitude <= 0.0001f || maxWallPressure <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 wallNormal = inwardWallNormal.normalized;
        Vector3 guidanceDirection = transform.forward + schoolingForce;
        Vector3 tangentDirection = Vector3.ProjectOnPlane(guidanceDirection, wallNormal);

        if (tangentDirection.sqrMagnitude <= 0.0001f)
        {
            tangentDirection = GetFallbackWallTangent(wallNormal);
        }

        Vector3 wallDesiredForward = (tangentDirection.normalized + (wallNormal * 0.35f)).normalized;
        float wallBlend = Mathf.Clamp01(maxWallPressure * Mathf.Max(0f, wallAvoidanceStrength));
        return (wallDesiredForward - transform.forward) * wallBlend;
    }

    private static void AccumulateWallPressure(
        Vector3 directionAwayFromWall,
        float distanceToWall,
        float avoidanceDistance,
        ref Vector3 inwardWallNormal,
        ref float maxWallPressure)
    {
        if (distanceToWall >= avoidanceDistance)
        {
            return;
        }

        float normalizedDistance = 1f - Mathf.Clamp01(distanceToWall / avoidanceDistance);
        float pressure = normalizedDistance * normalizedDistance * (3f - (2f * normalizedDistance));
        inwardWallNormal += directionAwayFromWall * pressure;
        maxWallPressure = Mathf.Max(maxWallPressure, pressure);
    }

    private Vector3 GetFallbackWallTangent(Vector3 wallNormal)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(transform.right, wallNormal);
        if (tangent.sqrMagnitude > 0.0001f)
        {
            return tangent;
        }

        tangent = Vector3.ProjectOnPlane(transform.up, wallNormal);
        if (tangent.sqrMagnitude > 0.0001f)
        {
            return tangent;
        }

        tangent = Vector3.Cross(wallNormal, Vector3.up);
        if (tangent.sqrMagnitude > 0.0001f)
        {
            return tangent;
        }

        return Vector3.Cross(wallNormal, Vector3.right);
    }

    private Vector3 GetFollowConeOrigin()
    {
        return transform.TransformPoint(followConeOriginLocalOffset);
    }

    private float GetFollowConeMinimumDot()
    {
        float halfAngleRadians = Mathf.Clamp(followConeAngle, 0f, 180f) * 0.5f * Mathf.Deg2Rad;
        return Mathf.Cos(halfAngleRadians);
    }

    private float GetSafeWiggleInterval()
    {
        return Mathf.Max(0.05f, wiggleInterval);
    }

    private void OnDrawGizmos()
    {
        if (!showDebugArrows || !isMoving) return;

        Vector3 origin = transform.position;
        Vector3 tip = origin + lastDesiredForward * debugArrowLength;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, tip);

        // Arrowhead
        float headLength = debugArrowLength * 0.2f;
        float headWidth = headLength * 0.4f;
        Vector3 right = Vector3.Cross(lastDesiredForward, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(lastDesiredForward, Vector3.right);
        right = right.normalized;
        Vector3 arrowBase = tip - lastDesiredForward * headLength;
        Gizmos.DrawLine(tip, arrowBase + right * headWidth);
        Gizmos.DrawLine(tip, arrowBase - right * headWidth);
    }
}