using System.Collections.Generic;
using UnityEngine;

public class FishMovement : MonoBehaviour
{
    private static readonly List<FishMovement> ActiveFish = new List<FishMovement>();

    [Header("Movement")]
    [SerializeField] private bool startMovingOnSpawn = false;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float turnSpeed = 4f;

    [Header("Wiggle")]
    [SerializeField] private float wiggleInterval = 0.75f;
    [SerializeField] private float maxWiggleAngle = 15f;
    [SerializeField] private float maxPitchWiggleAngle = 8f;

    [Header("Schooling")]
    [SerializeField] private float schoolingStrength = 0.85f;
    [SerializeField] private float preferredNeighborDistance = 1.1f;
    [SerializeField] private float attractionExponent = 3f;
    [SerializeField] private float repulsionExponent = 1f;
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

    private bool isMoving;
    private bool movementStateInitialized;
    private Quaternion targetRotation;
    private float wiggleTimer;
    private Quaternion wiggleOffsetRotation = Quaternion.identity;
    private Vector3 tankCenter;
    private Vector3 tankExtents = Vector3.one;
    private bool hasTankBounds;

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

    private void Update()
    {
        if (!isMoving)
        {
            return;
        }

        wiggleTimer += Time.deltaTime;
        float interval = GetSafeWiggleInterval();
        if (wiggleTimer >= interval)
        {
            wiggleTimer -= interval;
            QueueRandomWiggle();
        }

        Vector3 schoolingForce = CalculateSchoolingForce();
        Vector3 wallAvoidanceForce = CalculateWallAvoidanceForce(schoolingForce);
        Vector3 desiredForward = transform.forward + schoolingForce + wallAvoidanceForce;
        desiredForward = wiggleOffsetRotation * desiredForward;

        if (desiredForward.sqrMagnitude > 0.0001f)
        {
            targetRotation = Quaternion.LookRotation(desiredForward.normalized, Vector3.up);
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        transform.position += transform.forward * (moveSpeed * Time.deltaTime);
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
        Vector3 softRepulsionForce = Vector3.zero;
        Vector3 hardSeparationForce = Vector3.zero;
        Vector3 weightedNeighborPositionSum = Vector3.zero;
        Vector3 weightedNeighborForwardSum = Vector3.zero;
        float visibleNeighborWeightSum = 0f;
        Transform schoolRoot = transform.parent;
        Vector3 coneOrigin = GetFollowConeOrigin();
        float minimumDot = GetFollowConeMinimumDot();

        for (int i = 0; i < ActiveFish.Count; i++)
        {
            FishMovement other = ActiveFish[i];
            if (other == null || other == this || other.transform.parent != schoolRoot)
            {
                continue;
            }

            Vector3 displacement = transform.position - other.transform.position;
            float distance = displacement.magnitude;
            if (distance <= 0.0001f)
            {
                hardSeparationForce += GetFallbackSeparationDirection(other);
                continue;
            }

            if (distance < safeSeparationRadius)
            {
                Vector3 separationDirection = displacement / distance;
                float separationPressure = 1f - Mathf.Clamp01(distance / safeSeparationRadius);
                hardSeparationForce += separationDirection * separationPressure * separationPressure;
            }

            if (!allowSchooling || (safeInteractionRadius > 0f && distance > safeInteractionRadius))
            {
                continue;
            }

            float normalizedDistance = distance / safePreferredDistance;
            float forceScale = Mathf.Pow(normalizedDistance, attractionExponent) - Mathf.Pow(normalizedDistance, repulsionExponent);
            Vector3 pairForce = -forceScale * displacement;

            if (forceScale <= 0f)
            {
                softRepulsionForce += pairForce;
                continue;
            }

            if (!IsWithinFollowCone(other.transform.position))
            {
                continue;
            }

            Vector3 toOtherFromConeOrigin = other.transform.position - coneOrigin;
            float coneDistance = Mathf.Max(0.0001f, toOtherFromConeOrigin.magnitude);
            float forwardDot = Vector3.Dot(transform.forward, toOtherFromConeOrigin / coneDistance);
            float directionWeight = Mathf.InverseLerp(minimumDot, 1f, forwardDot);
            float distanceWeight = 1f / (1f + coneDistance);
            float neighborWeight = directionWeight * distanceWeight;

            weightedNeighborPositionSum += other.transform.position * neighborWeight;
            weightedNeighborForwardSum += other.transform.forward * neighborWeight;
            visibleNeighborWeightSum += neighborWeight;
        }

        Vector3 cohesionForce = Vector3.zero;
        if (allowSchooling && visibleNeighborWeightSum > 0f)
        {
            Vector3 localCenter = weightedNeighborPositionSum / visibleNeighborWeightSum;
            cohesionForce = (localCenter - transform.position) / safePreferredDistance;
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
        Vector3 schoolingForce = ((softRepulsionForce + cohesionForce) * Mathf.Max(0f, schoolingStrength)) + alignmentForce;
        schoolingForce = Vector3.ClampMagnitude(schoolingForce, Mathf.Max(0f, maxSchoolingForce));
        return separationForce + schoolingForce;
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

    private bool IsWithinFollowCone(Vector3 otherPosition)
    {
        Vector3 toOther = otherPosition - GetFollowConeOrigin();
        if (toOther.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 directionToOther = toOther.normalized;
        return Vector3.Dot(transform.forward, directionToOther) >= GetFollowConeMinimumDot();
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
}