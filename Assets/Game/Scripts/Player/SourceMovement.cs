using UnityEngine;

namespace FPSBase.Player
{
    public enum MoveState { Ground, Air, Slide, Mantle }

    /// <summary>
    /// Source/CS:GO-style movement (ground friction + acceleration, air strafing, bunny hopping,
    /// crouch-jumping, surfing on steep ramps) plus parkour moves: sprint, slide, mantle.
    /// All values are in meters. 1 Source unit = 0.0254 m.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public class SourceMovement : MonoBehaviour
    {
        const float U = 0.0254f; // Source units -> meters

        [Header("Speeds (CS:GO defaults)")]
        public float maxSpeed = 5.0f;               // jog speed (m/s)
        public float sprintMultiplier = 1.5f;      // hold Shift: 7.5 m/s
        public float crouchMultiplier = 0.34f;

        [Header("Ground")]
        public float accelerate = 5.5f;            // sv_accelerate
        public float friction = 5.2f;              // sv_friction
        public float stopSpeed = 80f * U;          // sv_stopspeed

        [Header("Air")]
        public float airAccelerate = 12f;          // sv_airaccelerate
        public float airWishSpeedCap = 30f * U;    // the magic 30u that makes air strafing work
        public float gravity = 800f * U;           // sv_gravity, 20.32 m/s^2
        public float jumpSpeed = 301.993f * U;     // CS:GO jump impulse
        public float jumpBuffer = 0.06f;           // seconds a jump press is remembered (0 = frame-perfect)
        public bool autoBhop => FPSBase.Core.GameSettings.AutoBhop; // hold space to bhop (toggle in Settings)

        [Header("Body")]
        public float standHeight = 1.83f;          // 72u
        public float crouchHeight = 1.37f;         // 54u
        public float standEye = 1.63f;             // 64u
        public float crouchEye = 1.17f;            // 46u
        public float crouchSpeed = 8f;             // eye-height lerp speed
        public float maxWalkSlope = 45.6f;         // ~0.7 normal.y, steeper = surf

        [Header("Slide")]
        public float slideMinSpeed = 6.0f;         // sprint, then crouch to slide
        public float slideBoost = 2.5f;
        public float slideFriction = 0.6f;
        public float slideStopSpeed = 2.5f;
        public float slideCooldown = 0.8f;

        public LayerMask worldMask = ~0;

        [Header("Mantle")]
        public float mantleReach = 0.75f;
        public float mantleMinHeight = 0.6f;
        public float mantleMaxHeight = 2.1f;
        public float mantleDuration = 0.32f;

        // ---- public state for camera / HUD ----
        public MoveState State { get; private set; }
        public Vector3 Velocity => velocity;
        public float HorizontalSpeed => new Vector3(velocity.x, 0f, velocity.z).magnitude;
        public float EyeHeight { get; private set; }
        public bool IsCrouched { get; private set; }
        public bool Grounded { get; private set; }

        CharacterController cc;
        PlayerInput input;
        Vector3 velocity;
        Vector3 groundNormal = Vector3.up;
        float jumpBufferTimer;
        float slideCooldownTimer;
        bool jumpedThisFrame;

        // mantle
        Vector3 mantleStart, mantleMid, mantleEnd;
        float mantleT;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            input = GetComponent<PlayerInput>();
            cc.height = standHeight;
            cc.center = new Vector3(0f, standHeight * 0.5f, 0f);
            cc.slopeLimit = 89f;        // we do our own slope handling (needed for surfing)
            cc.stepOffset = 0.45f;
            cc.minMoveDistance = 0f;
            EyeHeight = standEye;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            jumpedThisFrame = false;
            slideCooldownTimer -= dt;

            if (input.JumpPressed || (autoBhop && input.JumpHeld)) jumpBufferTimer = jumpBuffer + dt;
            else jumpBufferTimer -= dt;

            if (State == MoveState.Mantle) { UpdateMantle(dt); UpdateEye(dt); return; }

            CheckGround();
            UpdateCrouch();

            if (TryStartMantle()) { UpdateEye(dt); return; }

            switch (State)
            {
                case MoveState.Ground: GroundMove(dt); break;
                case MoveState.Slide: SlideMove(dt); break;
                default: AirMove(dt); break;
            }

            // Stick to ground while walking (not after jumping) so we don't fly off small bumps.
            Vector3 motion = velocity * dt;
            if (Grounded && !jumpedThisFrame) motion += Vector3.down * 2f * dt;
            CollisionFlags flags = cc.Move(motion);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;

            UpdateEye(dt);
        }

        // ------------------------------------------------------------------ ground / state

        void CheckGround()
        {
            bool wasGrounded = Grounded;
            Grounded = false;
            groundNormal = Vector3.up;

            if (velocity.y <= jumpSpeed * 0.5f)
            {
                float r = cc.radius * 0.95f;
                Vector3 origin = transform.position + Vector3.up * (r + 0.05f);
                if (Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, 0.12f + cc.skinWidth, worldMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.normal.y >= Mathf.Cos(maxWalkSlope * Mathf.Deg2Rad))
                    {
                        Grounded = true;
                        groundNormal = hit.normal;
                    }
                }
            }

            if (Grounded)
            {
                if (State == MoveState.Air) State = MoveState.Ground;
                if (!wasGrounded && velocity.y < 0f) velocity.y = 0f;
            }
            else if (State == MoveState.Ground || State == MoveState.Slide)
            {
                State = MoveState.Air;
            }
        }

        void UpdateCrouch()
        {
            bool want = input.CrouchHeld;
            if (want && !IsCrouched)
            {
                IsCrouched = true;
                float diff = standHeight - crouchHeight;
                SetHeight(crouchHeight);
                // CS-style duck in the air: pull the feet up instead of dropping the head.
                if (!Grounded)
                {
                    cc.enabled = false;
                    transform.position += Vector3.up * diff;
                    cc.enabled = true;
                    EyeHeight -= diff;
                }
                if (Grounded && HorizontalSpeed >= slideMinSpeed && slideCooldownTimer <= 0f) StartSlide();
            }
            else if (!want && IsCrouched)
            {
                float diff = standHeight - crouchHeight;
                if (Grounded)
                {
                    // only the room above the crouched head matters: the crouched body is already standing there
                    // (checking down at the feet hits the ground on a slope or a dirt pile and kept you crouched)
                    if (!HeadroomFree(transform.position)) return;
                    SetHeight(standHeight);
                }
                else if (SpaceFree(transform.position + Vector3.down * diff, standHeight))
                {
                    cc.enabled = false;
                    transform.position += Vector3.down * diff;
                    cc.enabled = true;
                    EyeHeight += diff;
                    SetHeight(standHeight);
                }
                else if (HeadroomFree(transform.position)) SetHeight(standHeight);
                else return;
                IsCrouched = false;
                if (State == MoveState.Slide) State = Grounded ? MoveState.Ground : MoveState.Air;
            }
        }

        void SetHeight(float h)
        {
            cc.height = h;
            cc.center = new Vector3(0f, h * 0.5f, 0f);
        }

        /// <summary>Is there room to stand up: the space between the crouched head and the standing head height.</summary>
        bool HeadroomFree(Vector3 feet)
        {
            float r = cc.radius * 0.9f;
            Vector3 bottom = feet + Vector3.up * Mathf.Max(r + 0.3f, crouchHeight - r);
            Vector3 top = feet + Vector3.up * (standHeight - r);
            return !Blocked(bottom, top, r);
        }

        /// <summary>Anything solid in this capsule, apart from the player's own body.</summary>
        bool Blocked(Vector3 bottom, Vector3 top, float r)
        {
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, overlap, worldMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlap[i] != null && !overlap[i].transform.IsChildOf(transform)) return true;
            return false;
        }
        readonly Collider[] overlap = new Collider[16];

        bool SpaceFree(Vector3 feet, float height)
        {
            float r = cc.radius * 0.98f;
            Vector3 bottom = feet + Vector3.up * (r + 0.02f);
            Vector3 top = feet + Vector3.up * (height - r);
            return !Blocked(bottom, top, r);
        }

        void UpdateEye(float dt)
        {
            float target = IsCrouched ? crouchEye : standEye;
            EyeHeight = Mathf.MoveTowards(EyeHeight, target, crouchSpeed * dt);
        }

        // ------------------------------------------------------------------ Source movement core

        Vector3 WishDir(out float wishSpeed)
        {
            Vector3 dir = transform.right * input.Move.x + transform.forward * input.Move.y;
            dir.y = 0f;
            float mag = Mathf.Clamp01(dir.magnitude);
            dir = mag > 0f ? dir.normalized : Vector3.zero;
            float speed = maxSpeed;
            if (IsCrouched) speed *= crouchMultiplier;
            else if (input.SprintHeld && input.Move.y > 0f) speed *= sprintMultiplier;
            wishSpeed = speed * mag;
            return dir;
        }

        void ApplyFriction(float dt, float fric, float stop)
        {
            Vector3 v = velocity; v.y = 0f;
            float speed = v.magnitude;
            if (speed < 0.001f) { velocity.x = velocity.z = 0f; return; }
            float control = speed < stop ? stop : speed;
            float drop = control * fric * dt;
            float newSpeed = Mathf.Max(speed - drop, 0f) / speed;
            velocity.x *= newSpeed;
            velocity.z *= newSpeed;
        }

        void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            float current = Vector3.Dot(velocity, wishDir);
            float add = wishSpeed - current;
            if (add <= 0f) return;
            float acc = Mathf.Min(accel * dt * wishSpeed, add);
            velocity += wishDir * acc;
        }

        void AirAccelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            float capped = Mathf.Min(wishSpeed, airWishSpeedCap);
            float current = Vector3.Dot(velocity, wishDir);
            float add = capped - current;
            if (add <= 0f) return;
            float acc = Mathf.Min(accel * wishSpeed * dt, add); // note: uses uncapped wishSpeed, like Source
            velocity += wishDir * acc;
        }

        bool ConsumeJump()
        {
            if (jumpBufferTimer <= 0f) return false;
            jumpBufferTimer = 0f;
            return true;
        }

        void DoJump()
        {
            velocity.y = jumpSpeed;
            Grounded = false;
            jumpedThisFrame = true;
            State = MoveState.Air;
        }

        void GroundMove(float dt)
        {
            // Jumping on the landing frame skips friction: this is what makes bhopping keep speed.
            if (ConsumeJump()) { DoJump(); AirMove(dt); return; }

            ApplyFriction(dt, friction, stopSpeed);
            Vector3 wish = WishDir(out float wishSpeed);
            Accelerate(wish, wishSpeed, accelerate, dt);
            velocity = Vector3.ProjectOnPlane(velocity, groundNormal);
        }

        void AirMove(float dt)
        {
            Vector3 wish = WishDir(out float wishSpeed);
            AirAccelerate(wish, IsCrouched ? maxSpeed : wishSpeed, airAccelerate, dt);
            velocity.y -= gravity * dt;
        }

        // ------------------------------------------------------------------ slide

        void StartSlide()
        {
            State = MoveState.Slide;
            slideCooldownTimer = slideCooldown;
            Vector3 h = new Vector3(velocity.x, 0f, velocity.z);
            velocity += h.normalized * slideBoost;
        }

        void SlideMove(float dt)
        {
            if (ConsumeJump()) { DoJump(); AirMove(dt); return; } // slide-jump keeps momentum

            ApplyFriction(dt, slideFriction, 0f);
            // Gravity pulls you faster down slopes.
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal);
            velocity += downhill * gravity * dt;
            // Small amount of steering.
            Vector3 wish = WishDir(out _);
            Accelerate(wish, 1.5f, 4f, dt);
            velocity = Vector3.ProjectOnPlane(velocity, groundNormal);

            if (HorizontalSpeed < slideStopSpeed) State = MoveState.Ground;
        }

        // ------------------------------------------------------------------ mantle

        bool TryStartMantle()
        {
            if (!input.JumpHeld || input.Move.y <= 0f || velocity.y > jumpSpeed * 0.8f) return false;

            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 chest = transform.position + Vector3.up * 1.0f;
            if (!Physics.Raycast(chest, fwd, out RaycastHit wall, cc.radius + mantleReach, worldMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(wall.normal.y) > 0.3f) return false;

            Vector3 probe = wall.point - wall.normal * (cc.radius + 0.15f) + Vector3.up * (mantleMaxHeight + 0.1f);
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, mantleMaxHeight + 0.2f, worldMask, QueryTriggerInteraction.Ignore)) return false;
            if (top.normal.y < 0.7f) return false;

            float ledgeHeight = top.point.y - transform.position.y;
            if (ledgeHeight < mantleMinHeight || ledgeHeight > mantleMaxHeight) return false;
            if (!SpaceFree(top.point + Vector3.up * 0.05f, IsCrouched ? crouchHeight : standHeight)) return false;

            State = MoveState.Mantle;
            mantleStart = transform.position;
            mantleMid = new Vector3(mantleStart.x, top.point.y + 0.05f, mantleStart.z);
            mantleEnd = top.point + Vector3.up * 0.05f;
            mantleT = 0f;
            cc.enabled = false;
            return true;
        }

        void UpdateMantle(float dt)
        {
            mantleT += dt / mantleDuration;
            float t = Mathf.Clamp01(mantleT);
            // Up first, then forward over the ledge.
            Vector3 a = Vector3.Lerp(mantleStart, mantleMid, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f)));
            Vector3 p = Vector3.Lerp(a, mantleEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f)));
            transform.position = p;

            if (t >= 1f)
            {
                cc.enabled = true;
                Vector3 fwd = transform.forward; fwd.y = 0f;
                velocity = fwd.normalized * 3f;
                State = MoveState.Ground;
            }
        }

        // ------------------------------------------------------------------ collisions

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Clip velocity against what we hit (Source ClipVelocity). Steep ramps become surfable.
            float into = Vector3.Dot(velocity, hit.normal);
            if (into < 0f)
            {
                if (Grounded && hit.normal.y > 0.7f) return; // floor while walking - handled by ground projection
                velocity -= hit.normal * into;
            }
        }

        public void Teleport(Vector3 position)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
            velocity = Vector3.zero;
            State = MoveState.Air;
        }
    }
}
