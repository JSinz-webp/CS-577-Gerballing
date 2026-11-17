using UnityEngine;

/// <summary>
/// Third-person orbit camera for the hamster ball.
/// Put this on the Main Camera and drag the ball into Target.
/// Mouse moves the camera; the camera must NOT be a child of the ball (the ball spins).
/// Press Escape to unlock the cursor.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float distance = 7f;
    public float lookHeight = 1f;          // aim a bit above the ball's center
    public float mouseSensitivity = 3f;
    public float minPitch = -5f;
    public float maxPitch = 60f;
    public float startPitch = 20f;
    public float followSmoothing = 12f;
    public bool lockCursor = true;

    float yaw;
    float pitch;

    void Start()
    {
        pitch = startPitch;
        yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
        if (Input.GetMouseButtonDown(0) && lockCursor) Cursor.lockState = CursorLockMode.Locked;

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Vector3 focus = target.position + Vector3.up * lookHeight;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desired = focus - rot * Vector3.forward * distance;

        float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, t);
        transform.LookAt(focus);
    }
}
