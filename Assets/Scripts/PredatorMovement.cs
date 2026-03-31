using UnityEngine;
using System.Collections.Generic;

public class PredatorMovement : MonoBehaviour
{
    public static readonly List<PredatorMovement> ActivePredators = new List<PredatorMovement>();

    [Header("Movement")]
    [SerializeField] private bool startMovingOnSpawn = false;
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float turnSpeed = 2.0f;

    [Header("Wiggle")]
    [SerializeField] private float wiggleInterval = 1.35f;
    [SerializeField] private float maxWiggleAngle = 35f;
    [SerializeField] private float maxPitchWiggleAngle = 14f;

    [Header("Hunting")]
    [SerializeField] private float visionRadius = 8f;
    [SerializeField] private float chaseStrength = 2f;
    [SerializeField] private float isolationPenalty = 3f; // adds penalty to target for every neighbor
    [SerializeField] private float isolationCheckRadius = 1.5f; // how close another fish is to count as neighbor

    [Header("Tank Avoidance")]
    [SerializeField] private float wallAvoidanceDistance = 1.25f;
    [SerializeField] private float wallAvoidanceStrength = 2f;

    [Header("Eating")]
    [SerializeField] private float eatRadius = 0.8f; // how close the shark needs to be to bite
    [SerializeField] private float eatCooldown = 1.5f; // bite cooldown
    
    private float lastEatTime;
    private bool isMoving;
    private bool movementStateInitialized;
    private Quaternion targetRotation;
    private float wiggleTimer;
    private Quaternion wiggleOffsetRotation = Quaternion.identity;

    // Tank boundaries
    private Vector3 tankCenter;
    private Vector3 tankExtents = Vector3.one;
    private bool hasTankBounds;

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
    }

    private void Update()
    {
        if (!isMoving)
        {
            return;
        }

        TryEat();

        wiggleTimer += Time.deltaTime;
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

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        transform.position += transform.forward * (moveSpeed * Time.deltaTime);
    }

    private Vector3 CalculateHuntingForce()
    {
        FishMovement bestTarget = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < FishMovement.ActiveFish.Count; i++)
        {
            FishMovement fish = FishMovement.ActiveFish[i];
            if (fish == null) continue;

            float distanceToPredator = Vector3.Distance(transform.position, fish.transform.position);
            
            // ignore fish outside of vision
            if (distanceToPredator > visionRadius) continue;

            // checking how many neighbors this fish has (isolation rule)
            int neighborCount = 0;
            for (int j = 0; j < FishMovement.ActiveFish.Count; j++)
            {
                if (i == j) continue;
                FishMovement otherFish = FishMovement.ActiveFish[j];
                if (Vector3.Distance(fish.transform.position, otherFish.transform.position) < isolationCheckRadius)
                {
                    neighborCount++;
                }
            }

            // lower score better, real distance + fake distance (group penalty)
            float score = distanceToPredator + (neighborCount * isolationPenalty);

            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = fish;
            }
        }

        // apply force towards the best target
        if (bestTarget != null)
        {
            Vector3 toTarget = (bestTarget.transform.position - transform.position).normalized;
            return toTarget * chaseStrength;
        }

        return Vector3.zero; // wander if no target found
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
        if (Time.time < lastEatTime + eatCooldown) { // dont eat if on cooldown
            return;
            } 

        FishMovement closestPrey = null;
        float closestDist = float.MaxValue;

        for (int i = 0; i < FishMovement.ActiveFish.Count; i++) // finding closest fish
        {
            FishMovement prey = FishMovement.ActiveFish[i];
            if (prey == null) 
            {
                continue;
            }

            float dist = Vector3.Distance(transform.position, prey.transform.position);

            if (dist <= eatRadius && dist < closestDist) // if fish is closest and inside mouth
            {
                closestDist = dist;
                closestPrey = prey;
            }
        }

        if (closestPrey != null) // if close enough eat
        {
            Eat(closestPrey);
        }
    }

    private void Eat(FishMovement prey)
    {
        lastEatTime = Time.time;

        Destroy(prey.gameObject);
    }
}