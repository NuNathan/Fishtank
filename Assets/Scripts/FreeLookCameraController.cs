using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class FreeLookCameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private bool lockCursorOnStart = true;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        Vector3 startAngles = transform.eulerAngles;
        yaw = startAngles.y;
        pitch = NormalizePitch(startAngles.x);

        if (lockCursorOnStart)
        {
            SetCursorLock(true);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLock(false);
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUi())
            {
                SetCursorLock(true);
            }

            return;
        }

        UpdateLook(mouse);
        UpdateMovement(keyboard);
    }

    private void UpdateLook(Mouse mouse)
    {
        if (mouse == null)
        {
            return;
        }

        Vector2 lookDelta = mouse.delta.ReadValue();
        yaw += lookDelta.x * lookSensitivity;
        pitch = Mathf.Clamp(pitch - (lookDelta.y * lookSensitivity), minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement(Keyboard keyboard)
    {
        if (keyboard == null)
        {
            return;
        }

        Vector2 moveInput = Vector2.zero;

        if (keyboard.wKey.isPressed) moveInput.y += 1f;
        if (keyboard.sKey.isPressed) moveInput.y -= 1f;
        if (keyboard.dKey.isPressed) moveInput.x += 1f;
        if (keyboard.aKey.isPressed) moveInput.x -= 1f;

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 moveDirection = (forward * moveInput.y) + (right * moveInput.x);

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }

    private static float NormalizePitch(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private static void SetCursorLock(bool isLocked)
    {
        Cursor.lockState = isLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isLocked;
    }

    private static bool IsPointerOverUi()
    {
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.IsPointerOverGameObject();
    }
}
