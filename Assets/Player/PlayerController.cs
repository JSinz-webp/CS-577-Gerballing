using UnityEngine;

/// <summary>
/// Hamster-ball player controller for Gerballing.
/// Physics-based rolling (Rigidbody + SphereCollider), camera-relative input,
/// jumping with coyote time / jump buffering, respawn on fall, and upgrade hooks.
///
/// Setup:
///  1. Create a Sphere (it already has a SphereCollider). Add this script; a Rigidbody is added automatically.
///  2. (Recommended) Give the SphereCollider a Physics Material with Dynamic/Static Friction ~0.6.
///  3. Put CameraFollow on the Main Camera and drag the ball into its Target slot.
///  4. (Optional) Put the hamster model as a SEPARATE object (not a child of the ball)
///     and drag it into "Hamster Visual" so it stays upright instead of spinning with the ball.
/// Controls: WASD / arrow keys to roll, Space to jump.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("How hard the player pushes the ball while on the ground.")]
    public float moveForce = 25f;
    [Tooltip("Fraction of moveForce available while in the air.")]
    [Range(0f, 1f)] public float airControl = 0.35f;
    [Tooltip("Player input stops adding speed past this. Gravity on slopes can still make you go faster.")]
    public float maxInputSpeed = 14f;
    [Tooltip("Must be high or the ball's top speed gets capped by Unity's default spin limit.")]
    public float maxAngularVelocity = 100f;

    [Header("Jumping")]
    public float jumpForce = 7f;
    [Tooltip("Extra jumps allowed in the air (0 = normal, 1 = double jump).")]
    public int extraAirJumps = 0;
    [Tooltip("Grace period after rolling off an edge where you can still jump.")]
    public float coyoteTime = 0.15f;
    [Tooltip("Pressing jump slightly before landing still counts.")]
    public float jumpBufferTime = 0.15f;
    [Tooltip("Surfaces steeper than this don't count as ground for jumping.")]
    [Range(0f, 89f)] public float maxGroundAngle = 50f;

    [Header("Respawn")]
    [Tooltip("If the ball falls below this Y, it respawns at the last checkpoint.")]
    public float fallY = -30f;

    [Header("Visuals (optional)")]
    [Tooltip("Hamster model that sits in the ball but doesn't roll with it. Must NOT be a child of the ball.")]
    public Transform hamsterVisual;
    public float hamsterTurnSpeed = 10f;

    [Header("State")]
    [Tooltip("Turn off during countdowns, the finish line, menus, etc.")]
    public bool canMove = true;

    // Upgrade tracking (read these from UI / level-select if needed)
    public bool HasJumpUpgrade { get; private set; }
    public bool HasShrinkUpgrade { get; private set; }
    public bool HasDoubleJumpUpgrade { get; private set; }

    public bool IsGrounded => grounded;
    public float Speed => GetVelocity().magnitude;

    Rigidbody rb;
    Transform cam;

    Vector3 inputDir;
    bool grounded;
    bool groundedThisStep;
    Vector3 groundNormal = Vector3.up;
    Vector3 groundNormalThisStep;
    float lastGroundedTime = -999f;
    float lastJumpPressedTime = -999f;
    int airJumpsLeft;

    Vector3 checkpointPos;
    Quaternion checkpointRot;
    Vector3 baseScale;
    float baseJumpForce;
    Vector3 hamsterBaseScale;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.maxAngularVelocity = maxAngularVelocity;
        rb.interpolation = RigidbodyInterpolation.Interpolate;           // smooth camera follow
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;  // no tunneling at high speed

        if (Camera.main != null) cam = Camera.main.transform;

        checkpointPos = transform.position;
        checkpointRot = transform.rotation;
        baseScale = transform.localScale;
        baseJumpForce = jumpForce;
        if (hamsterVisual != null) hamsterBaseScale = hamsterVisual.localScale;
    }

    void Update()
    {
        // --- Read input every frame (Update) and apply it in FixedUpdate ---
        if (!canMove)
        {
            inputDir = Vector3.zero;
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // Camera-relative direction, flattened onto the horizontal plane
        Vector3 forward = cam != null ? cam.forward : Vector3.forward;
        Vector3 right = cam != null ? cam.right : Vector3.right;
        forward.y = 0f; right.y = 0f;
        forward.Normalize(); right.Normalize();

        inputDir = Vector3.ClampMagnitude(forward * v + right * h, 1f);

        if (Input.GetButtonDown("Jump"))
            lastJumpPressedTime = Time.time;

        if (transform.position.y < fallY)
            Respawn();
    }

    void FixedUpdate()
    {
        // Ground info gathered by OnCollisionStay during the previous physics step
        grounded = groundedThisStep;
        groundNormal = grounded ? groundNormalThisStep.normalized : Vector3.up;
        groundedThisStep = false;
        groundNormalThisStep = Vector3.zero;

        if (grounded)
        {
            lastGroundedTime = Time.time;
            airJumpsLeft = extraAirJumps;
        }

        Move();
        TryJump();
    }

    void Move()
    {
        if (inputDir.sqrMagnitude < 0.001f) return;

        // On the ground, push along the slope so we don't push into / away from the track
        Vector3 dir = grounded ? Vector3.ProjectOnPlane(inputDir, groundNormal).normalized * inputDir.magnitude : inputDir;

        // Only add force if we're not already going faster than maxInputSpeed in that direction
        float speedInDir = Vector3.Dot(GetVelocity(), dir.normalized);
        if (speedInDir >= maxInputSpeed) return;

        float force = moveForce * (grounded ? 1f : airControl);
        rb.AddForce(dir * force, ForceMode.Acceleration);
    }

    void TryJump()
    {
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (!jumpBuffered || !canMove) return;

        bool canGroundJump = Time.time - lastGroundedTime <= coyoteTime;

        if (canGroundJump)
        {
            DoJump();
            lastGroundedTime = -999f; // no second coyote jump
        }
        else if (airJumpsLeft > 0)
        {
            airJumpsLeft--;
            DoJump();
        }
    }

    void DoJump()
    {
        lastJumpPressedTime = -999f;

        // Cancel downward speed so every jump has the same height
        Vector3 vel = GetVelocity();
        if (vel.y < 0f) vel.y = 0f;
        SetVelocity(vel);

        rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
    }

    void OnCollisionStay(Collision collision)
    {
        float minNormalY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 n = collision.GetContact(i).normal;
            if (n.y >= minNormalY)
            {
                groundedThisStep = true;
                groundNormalThisStep += n;
            }
        }
    }

    void LateUpdate()
    {
        // Keep the hamster centered in the ball, upright, and facing where it's going
        if (hamsterVisual == null) return;

        hamsterVisual.position = transform.position;

        Vector3 flatVel = GetVelocity();
        flatVel.y = 0f;
        if (flatVel.sqrMagnitude > 0.25f)
        {
            Quaternion target = Quaternion.LookRotation(flatVel.normalized, Vector3.up);
            hamsterVisual.rotation = Quaternion.Slerp(hamsterVisual.rotation, target, hamsterTurnSpeed * Time.deltaTime);
        }
    }

    // ------------------------------------------------------------------
    // Respawn / checkpoints
    // ------------------------------------------------------------------

    /// <summary>Call from a checkpoint trigger to update where the player respawns.</summary>
    public void SetCheckpoint(Vector3 position, Quaternion rotation)
    {
        checkpointPos = position;
        checkpointRot = rotation;
    }

    public void Respawn()
    {
        SetVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;
        rb.position = checkpointPos;
        rb.rotation = checkpointRot;
        transform.SetPositionAndRotation(checkpointPos, checkpointRot);
    }

    /// <summary>Stops the ball dead (e.g. on crossing the finish line).</summary>
    public void Freeze()
    {
        canMove = false;
        SetVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;
    }

    // ------------------------------------------------------------------
    // Upgrades (call these from the upgrade shop / save data)
    // ------------------------------------------------------------------

    /// <summary>Higher jump. multiplier 1.4 = 40% higher jump force.</summary>
    public void ApplyJumpUpgrade(float multiplier = 1.4f)
    {
        HasJumpUpgrade = true;
        jumpForce = baseJumpForce * multiplier;
    }

    /// <summary>Smaller ball — fits through tighter gaps. Doesn't change speed.</summary>
    public void ApplyShrinkUpgrade(float scale = 0.6f)
    {
        HasShrinkUpgrade = true;
        transform.localScale = baseScale * scale;
        if (hamsterVisual != null) hamsterVisual.localScale = hamsterBaseScale * scale;
    }

    /// <summary>Third upgrade idea: double jump. Doesn't touch speed.</summary>
    public void ApplyDoubleJumpUpgrade()
    {
        HasDoubleJumpUpgrade = true;
        extraAirJumps = 1;
    }

    public void ResetUpgrades()
    {
        HasJumpUpgrade = HasShrinkUpgrade = HasDoubleJumpUpgrade = false;
        jumpForce = baseJumpForce;
        transform.localScale = baseScale;
        if (hamsterVisual != null) hamsterVisual.localScale = hamsterBaseScale;
        extraAirJumps = 0;
    }

    // ------------------------------------------------------------------
    // Unity 6 renamed Rigidbody.velocity -> linearVelocity; this works on both
    // ------------------------------------------------------------------
    Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    void SetVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}
