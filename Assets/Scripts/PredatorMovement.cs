using UnityEngine;

public class PredatorMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private bool startMovingOnSpawn = false;
    [SerializeField] private float moveSpeed = 2.2f;
    [SerializeField] private float turnSpeed = 2.6f;

    [Header("Wiggle")]
    [SerializeField] private float wiggleInterval = 1.35f;
    [SerializeField] private float maxWiggleAngle = 35f;
    [SerializeField] private float maxPitchWiggleAngle = 14f;

    private bool isMoving;
    private bool movementStateInitialized;
    private Quaternion targetRotation;
    private float wiggleTimer;
    private Quaternion wiggleOffsetRotation = Quaternion.identity;

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
            QueueRandomTurn();
        }

        Vector3 desiredForward = wiggleOffsetRotation * transform.forward;
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
}