using UnityEngine;

namespace Matoi.Characters
{
    /// <summary>
    /// 移動の「意図」。プレイヤー操作でも敵AIでも、この形で Motor に渡す。
    /// </summary>
    public struct MoveIntent
    {
        /// <summary>移動したい方向（ワールド座標・水平）。長さ 0〜1 が入力の強さ。</summary>
        public Vector3 direction;
        public bool sprint;
        public bool walk;
        /// <summary>
        /// 向きたい方向。ゼロなら「進む方向を向く」通常モード。
        /// 指定すると「向きと移動を別々にする」モード（銃を構えての移動・後ずさりなど）。
        /// </summary>
        public Vector3 facing;
        /// <summary>速度の倍率（構え中は遅くする等）。0 は 1 として扱う。</summary>
        public float speedMultiplier;

        public static MoveIntent None => default;
    }

    /// <summary>
    /// キャラクターを実際に動かす部品。加速・減速・旋回・切り返し・重力を担当する。
    /// 入力（誰が操作しているか）やアニメーションのことは知らない。
    /// 状態（速度・切り返し中など）を公開し、アニメーション側がそれを読む。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        [SerializeField] MovementProfile profile;

        CharacterController controller;
        MoveIntent intent;
        Vector3 smoothedInput;
        Vector3 smoothedInputVel;
        float speed;               // 水平速度の大きさ (m/s)
        Vector3 planarVelocity;    // 水平速度（向き付き）
        float verticalSpeed;
        float lastYaw;

        // ---- 公開状態（アニメーション・UI・AI が読む） ----
        public MovementProfile Profile => profile;
        public float Speed => speed;
        /// <summary>体の正面方向の速度。後ずさり中はマイナス（アニメーションの逆再生に使う）</summary>
        public float ForwardSpeed { get; private set; }
        /// <summary>0=停止, 1=小走り, 1以上=ダッシュ（アニメーションのブレンド用）</summary>
        public float NormalizedSpeed => profile ? speed / Mathf.Max(0.01f, profile.jogSpeed) : 0f;
        public Vector3 Velocity { get; private set; }
        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool IsPivoting { get; private set; }
        public bool IsSprinting { get; private set; }
        /// <summary>向きと移動が別々のモード中か（構え移動）</summary>
        public bool IsFreeFacing { get; private set; }
        /// <summary>現在の旋回速度（度/秒、右回りが正）</summary>
        public float TurnRate { get; private set; }
        public float DesiredSpeed { get; private set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            lastYaw = transform.eulerAngles.y;
        }

        public void SetIntent(MoveIntent value) => intent = value;

        Vector3 externalVelocity;
        [Tooltip("外から加えた勢い（踏み込み・吹き飛び）が消える速さ")]
        [SerializeField] float externalDamping = 9f;

        /// <summary>踏み込み・ノックバックなど、外から勢いを加える（水平・m/s）。</summary>
        public void AddImpulse(Vector3 velocity)
        {
            velocity.y = 0f;
            externalVelocity += velocity;
        }

        Vector3 dashVelocity;
        float dashStart, dashDuration;

        /// <summary>回避などで、一定時間だけ決まった速度で動かす（入力は無視、だんだん減速）。</summary>
        public void Dash(Vector3 velocity, float duration)
        {
            velocity.y = 0f;
            dashVelocity = velocity;
            dashStart = Time.time;
            dashDuration = duration;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }

        public bool IsDashing => Time.time < dashStart + dashDuration;

        /// <summary>向きを即座に変える（近接攻撃の振り向きなど）。</summary>
        public void FaceImmediately(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (!IsFreeFacing) planarVelocity = transform.forward * speed;
        }

        public void SetProfile(MovementProfile value)
        {
            if (value != null) profile = value;
        }

        /// <summary>瞬間移動（リスポーン・テスト用）。</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = true;
            speed = 0f;
            planarVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalSpeed = 0f;
            smoothedInput = Vector3.zero;
            smoothedInputVel = Vector3.zero;
            lastYaw = yaw;
        }

        void Update()
        {
            if (profile == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 1) 入力をなめらかにする（スティックの細かいブレを吸収）
            Vector3 raw = Vector3.ClampMagnitude(new Vector3(intent.direction.x, 0f, intent.direction.z), 1f);
            smoothedInput = profile.inputSmoothing > 0f
                ? Vector3.SmoothDamp(smoothedInput, raw, ref smoothedInputVel, profile.inputSmoothing)
                : raw;
            float inputMag = smoothedInput.magnitude;
            // 離した時は即座に「止まりたい」と判断する（なましで止まりが遅れないように）
            if (raw.sqrMagnitude < 0.0001f) inputMag = 0f;

            // 2) 目標速度を決める
            float mult = intent.speedMultiplier > 0f ? intent.speedMultiplier : 1f;
            DesiredSpeed = ComputeDesiredSpeed(inputMag) * mult;
            IsSprinting = intent.sprint && DesiredSpeed > profile.jogSpeed;

            Vector3 facing = new Vector3(intent.facing.x, 0f, intent.facing.z);
            bool freeFacing = facing.sqrMagnitude > 0.0001f;
            if (freeFacing && !IsFreeFacing) planarVelocity = transform.forward * speed; // 切り替え時に速度を引き継ぐ
            IsFreeFacing = freeFacing;

            if (IsDashing)
            {
                // 回避中：速度は決め打ち（最初が最速、最後に減速）。終わった時の勢いはそのまま引き継ぐ
                float t = (Time.time - dashStart) / Mathf.Max(0.01f, dashDuration);
                float curve = Mathf.Lerp(1f, 0.25f, t * t);
                planarVelocity = dashVelocity * curve;
                speed = planarVelocity.magnitude;
                IsPivoting = false;
                freeFacing = true; // 下の移動処理で planarVelocity をそのまま使う
                IsFreeFacing = true;
            }
            else if (freeFacing) UpdateFreeFacing(dt, inputMag, facing.normalized);
            else UpdateFaceMovement(dt, inputMag);

            // 5) 重力
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(verticalSpeed + profile.gravity * dt, profile.maxFallSpeed);

            // 6) 移動
            Vector3 horizontal = freeFacing ? planarVelocity : transform.forward * speed;
            if (!freeFacing) planarVelocity = horizontal;
            ForwardSpeed = Vector3.Dot(horizontal, transform.forward);
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero,
                externalDamping * Mathf.Max(1f, externalVelocity.magnitude) * dt);
            controller.Move((horizontal + externalVelocity + Vector3.up * verticalSpeed) * dt);
            Velocity = controller.velocity;

            float yaw = transform.eulerAngles.y;
            TurnRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
        }

        /// <summary>通常モード：進みたい方向を向き、向いている方向へ進む（横滑りしない）。</summary>
        void UpdateFaceMovement(float dt, float inputMag)
        {
            Vector3 forward = transform.forward;
            float angleToInput = 0f;
            if (inputMag > 0.01f)
            {
                Vector3 wantDir = smoothedInput / inputMag;
                angleToInput = Vector3.Angle(forward, wantDir);

                // 切り返し判定：走っている最中に、ほぼ反対方向へ入力した
                if (!IsPivoting && angleToInput >= profile.pivotAngle && speed >= profile.pivotMinSpeed)
                    IsPivoting = true;

                if (IsPivoting)
                {
                    // まず踏ん張って止まる（この間は向きを変えない）
                    speed = Mathf.MoveTowards(speed, 0f, profile.pivotDeceleration * dt);
                    if (speed <= 0.6f) IsPivoting = false;
                }
                else
                {
                    float turn = profile.GetTurnSpeed(speed) * dt;
                    Quaternion target = Quaternion.LookRotation(wantDir, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turn);
                }
            }
            else
            {
                IsPivoting = false;
            }

            // 向きが大きくずれている間は減速して、大回りの滑りを防ぐ
            if (!IsPivoting)
            {
                float turnFactor = Mathf.Lerp(profile.sharpTurnSpeedFactor, 1f,
                    Mathf.Clamp01(Mathf.Cos(angleToInput * Mathf.Deg2Rad)));
                float target = DesiredSpeed * (inputMag > 0.01f ? turnFactor : 1f);
                float rate = target > speed
                    ? (IsSprinting ? profile.sprintAcceleration : profile.acceleration)
                    : profile.deceleration;
                speed = Mathf.MoveTowards(speed, target, rate * dt);
            }
        }

        /// <summary>向き指定モード：指定の方向を向いたまま、入力方向へ進む。</summary>
        void UpdateFreeFacing(float dt, float inputMag, Vector3 facing)
        {
            IsPivoting = false;
            Vector3 want = inputMag > 0.01f ? smoothedInput / inputMag * DesiredSpeed : Vector3.zero;
            float rate = want.sqrMagnitude > planarVelocity.sqrMagnitude ? profile.acceleration : profile.deceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, want, rate * dt);
            speed = planarVelocity.magnitude;

            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, profile.aimTurnSpeed * dt);
        }

        float ComputeDesiredSpeed(float inputMag)
        {
            if (inputMag < 0.01f) return 0f;
            if (intent.walk) return profile.walkSpeed * Mathf.Clamp01(inputMag / profile.walkInputThreshold);
            if (intent.sprint && inputMag > profile.walkInputThreshold) return profile.sprintSpeed;
            if (inputMag <= profile.walkInputThreshold)
                return profile.walkSpeed * (inputMag / profile.walkInputThreshold);
            float t = (inputMag - profile.walkInputThreshold) / (1f - profile.walkInputThreshold);
            return Mathf.Lerp(profile.walkSpeed, profile.jogSpeed, t);
        }
    }
}
