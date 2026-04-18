using UnityEngine;
using System.Collections.Generic;

public class PredatorMovement : MonoBehaviour
{
    public static readonly List<PredatorMovement> ActivePredators = new List<PredatorMovement>();

    [Header("Movement")]
    [SerializeField] private bool startMovingOnSpawn = false;
    [SerializeField] private float moveSpeed = 3f;//2.5f;
    [SerializeField] private float turnSpeed = 0.4f;

    public float MoveSpeedValue { get => moveSpeed; set => moveSpeed = value; }
    public float TurnSpeedValue { get => turnSpeed; set => turnSpeed = value; }

    [Header("Wiggle")]
    [SerializeField] private float wiggleInterval = 1.35f;
    [SerializeField] private float maxWiggleAngle = 35f;
    [SerializeField] private float maxPitchWiggleAngle = 14f;

    [Header("Hunting")]
    [SerializeField] private float visionRadius = 8f;
    [SerializeField] private float chaseStrength = 2f;

    [Header("Burst Turn")]
    [SerializeField] private float burstTurnInterval = 5f;
    [SerializeField] private float burstTurnDuration = 0.5f;
    [SerializeField] private float burstTurnSpeed = 4f;

    [Header("Tank Avoidance")]
    [SerializeField] private float wallAvoidanceDistance = 1.25f;
    [SerializeField] private float wallAvoidanceStrength = 2f;

    [Header("Eating")]
    [SerializeField] private float eatRadius = 0.8f; // how close the shark needs to be to bite
    [SerializeField] private int maxEatPerPass = 3; // max fish eaten in a single pass

    private bool isMoving;
    private bool movementStateInitialized;
    private Quaternion targetRotation;
    private float wiggleTimer;
    private Quaternion wiggleOffsetRotation = Quaternion.identity;
    private float burstTurnTimer;
    private float burstTurnRemainingTime;

    // Tank boundaries
    private Vector3 tankCenter;
    private Vector3 tankExtents = Vector3.one;
    private bool hasTankBounds;
    private readonly List<int> gridQueryResults = new List<int>();

    private void OnEnable()
    {
        if (!ActivePredators.Contains(this)) ActivePredators.Add(this);
    }

    private void OnDisable()
    {
        ActivePredators.Remove(this);
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
        burstTurnTimer = Random.Range(0f, burstTurnInterval);
        burstTurnRemainingTime = 0f;
    }

    public void UpdateMovement(float deltaTime)
    {
        if (!isMoving)
        {
            return;
        }

        TryEat();

        wiggleTimer += deltaTime;
        float interval = GetSafeWiggleInterval();
        if (wiggleTimer >= interval)
        {
            wiggleTimer -= interval;
            QueueRandomTurn();
        }

        Vector3 huntingForce = CalculateHuntingForce();
        Vector3 wallAvoidanceForce = CalculateWallAvoidanceForce(huntingForce);

        Vector3 desiredForward = transform.forward + huntingForce + wallAvoidanceForce;
        desiredForward = wiggleOffsetRotation * desiredForward;

        if (desiredForward.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.LookRotation(desiredForward.normalized, Vector3.up);
        }

        // Burst turn timer
        if (burstTurnRemainingTime > 0f)
        {
            burstTurnRemainingTime -= deltaTime;
        }
        else
        {
            burstTurnTimer += deltaTime;
            if (burstTurnTimer >= burstTurnInterval)
            {
                burstTurnTimer = 0f;
                burstTurnRemainingTime = burstTurnDuration;
            }
        }

        float currentTurnSpeed = burstTurnRemainingTime > 0f ? burstTurnSpeed : turnSpeed;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, currentTurnSpeed * deltaTime);
        transform.position += transform.forward * (moveSpeed * deltaTime);

        if (hasTankBounds)
        {
            Vector3 pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, tankCenter.x - tankExtents.x, tankCenter.x + tankExtents.x);
            pos.y = Mathf.Clamp(pos.y, tankCenter.y - tankExtents.y, tankCenter.y + tankExtents.y);
            pos.z = Mathf.Clamp(pos.z, tankCenter.z - tankExtents.z, tankCenter.z + tankExtents.z);
            transform.position = pos;
        }
    }

    private Vector3 CalculateHuntingForce()
    {
        FishSpatialGrid grid = FishMovement.SpatialGrid;
        Vector3 predatorPos = transform.position;
        float visionRadiusSqr = visionRadius * visionRadius;
        Vector3[] fishPositions = FishMovement.CachedPositions;
        int fishCount = FishMovement.CachedCount;

        bool useGrid = grid != null;
        int outerCount;

        if (useGrid)
        {
            grid.QueryRadius(predatorPos, visionRadius, gridQueryResults);
            outerCount = gridQueryResults.Count;
        }
        else
        {
            outerCount = fishCount;
        }

        // find center of mass of all visible fish
        Vector3 centerOfMass = Vector3.zero;
        int visibleCount = 0;

        for (int c = 0; c < outerCount; c++)
        {
            int i = useGrid ? gridQueryResults[c] : c;
            if (i >= fishCount) continue;

            Vector3 fishPos = fishPositions[i];
            Vector3 toFish = fishPos - predatorPos;

            if (toFish.sqrMagnitude > visionRadiusSqr) continue;

            centerOfMass += fishPos;
            visibleCount++;
        }

        if (visibleCount > 0)
        {
            centerOfMass /= visibleCount;
            Vector3 toCenter = (centerOfMass - predatorPos).normalized;
            return toCenter * chaseStrength;
        }

        return Vector3.zero; // wander if no fish visible
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

    private Vector3 CalculateWallAvoidanceForce(Vector3 guidanceForce)
    {
        if (!hasTankBounds || wallAvoidanceStrength <= 0f) return Vector3.zero;

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

        if (inwardWallNormal.sqrMagnitude <= 0.0001f || maxWallPressure <= 0f) return Vector3.zero;

        Vector3 wallNormal = inwardWallNormal.normalized;
        Vector3 guidanceDirection = transform.forward + guidanceForce;
        Vector3 tangentDirection = Vector3.ProjectOnPlane(guidanceDirection, wallNormal);

        if (tangentDirection.sqrMagnitude <= 0.0001f)
        {
            tangentDirection = GetFallbackWallTangent(wallNormal);
        }

        Vector3 wallDesiredForward = (tangentDirection.normalized + (wallNormal * 0.35f)).normalized;
        float wallBlend = Mathf.Clamp01(maxWallPressure * Mathf.Max(0f, wallAvoidanceStrength));
        return (wallDesiredForward - transform.forward) * wallBlend;
    }

    private static void AccumulateWallPressure(Vector3 directionAwayFromWall, float distanceToWall, float avoidanceDistance, ref Vector3 inwardWallNormal, ref float maxWallPressure)
    {
        if (distanceToWall >= avoidanceDistance) return;

        float normalizedDistance = 1f - Mathf.Clamp01(distanceToWall / avoidanceDistance);
        float pressure = normalizedDistance * normalizedDistance * (3f - (2f * normalizedDistance));
        inwardWallNormal += directionAwayFromWall * pressure;
        maxWallPressure = Mathf.Max(maxWallPressure, pressure);
    }

    private Vector3 GetFallbackWallTangent(Vector3 wallNormal)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(transform.right, wallNormal);
        if (tangent.sqrMagnitude > 0.0001f) return tangent;

        tangent = Vector3.ProjectOnPlane(transform.up, wallNormal);
        if (tangent.sqrMagnitude > 0.0001f) return tangent;

        tangent = Vector3.Cross(wallNormal, Vector3.up);
        if (tangent.sqrMagnitude > 0.0001f) return tangent;

        return Vector3.Cross(wallNormal, Vector3.right);
    }

    public void SetMovementActive(bool active)
    {
        isMoving = active;
        movementStateInitialized = true;
        targetRotation = transform.rotation;
        wiggleTimer = Random.Range(0f, GetSafeWiggleInterval());
        wiggleOffsetRotation = Quaternion.identity;
    }

    private void QueueRandomTurn()
    {
        float yawTurnAngle = Random.Range(-maxWiggleAngle, maxWiggleAngle);
        float pitchTurnAngle = Random.Range(-maxPitchWiggleAngle, maxPitchWiggleAngle);
        Quaternion yawRotation = Quaternion.AngleAxis(yawTurnAngle, Vector3.up);
        Quaternion pitchRotation = Quaternion.AngleAxis(pitchTurnAngle, transform.right);
        wiggleOffsetRotation = pitchRotation * yawRotation;
    }

    private float GetSafeWiggleInterval()
    {
        return Mathf.Max(0.05f, wiggleInterval);
    }

    private void TryEat()
    {
        int eaten = 0;

        for (int i = FishMovement.ActiveFish.Count - 1; i >= 0 && eaten < maxEatPerPass; i--)
        {
            FishMovement prey = FishMovement.ActiveFish[i];
            if (prey == null) continue;

            float dist = Vector3.Distance(transform.position, prey.transform.position);
            if (dist <= eatRadius)
            {
                FishHudSchoolController.OnFishEaten();
                Destroy(prey.gameObject);
                eaten++;
            }
        }
    }
}