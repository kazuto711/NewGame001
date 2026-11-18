using UnityEngine;

namespace Matoi.Characters
{
    /// <summary>
    /// Motor の状態（速度・旋回）を読み取り、Animator と見た目の傾きに反映する。
    /// 移動の計算はしない。モデルやアニメーションを差し替えても、この仕組みはそのまま使える。
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int AnimSpeedId = Animator.StringToHash("AnimSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");

        [SerializeField] CharacterMotor motor;
        [SerializeField] Animator animator;
        [Tooltip("傾ける見た目のルート（モデルの親）。当たり判定は傾かない")]
        [SerializeField] Transform leanRoot;

        [Header("足滑り防止")]
        [Tooltip("ダッシュアニメーション本来の速度 (m/s)。これを超えると再生速度を上げる。セットアップで自動計測")]
        [SerializeField] float sprintClipSpeed = 6f;
        [Tooltip("速度パラメータのなまし時間（秒）")]
        [SerializeField] float speedDampTime = 0.06f;

        [Header("体の傾き（重さ・勢いの表現）")]
        [Tooltip("旋回時に内側へ傾く最大角度")]
        [SerializeField] float maxBankAngle = 5f;
        [Tooltip("旋回の速さ×移動速度 → 傾きの係数")]
        [SerializeField] float bankFactor = 0.0025f;
        [Tooltip("これより遅い旋回では傾けない（度/秒）。まっすぐ歩く時の細かな向きの修正で体が揺れないように")]
        [SerializeField] float bankDeadZone = 25f;
        [Tooltip("加速・減速で前後に傾く最大角度")]
        [SerializeField] float maxPitchAngle = 3.5f;
        [SerializeField] float pitchFactor = 0.6f;
        [SerializeField] float leanSmoothing = 7f;

        [Header("単発の動作（攻撃・被弾・死亡）")]
        [Tooltip("全身の単発動作を再生するレイヤー名（Animator Controller 内）")]
        [SerializeField] string actionLayer = "Action";

        static readonly int DeadId = Animator.StringToHash("Dead");

        [Header("停止・旋回・切り返しの動き")]
        [Tooltip("オンのキャラだけ再生する（今はプレイヤーのみ）")]
        [SerializeField] bool useLocomotionTransitions;
        [Tooltip("この速さ以上で止まると、走りの停止になる (m/s)")]
        [SerializeField] float runStopSpeed = 3f;
        [Tooltip("この速さ以上で止まると、歩きの停止になる (m/s)")]
        [SerializeField] float walkStopSpeed = 0.8f;
        [Tooltip("止まったままこの角度以上回ると、足を踏みかえる（度）")]
        [SerializeField] float turnStepAngle = 30f;
        [SerializeField] float turnStepCooldown = 0.45f;

        static readonly int StopRunId = Animator.StringToHash("StopRun");
        static readonly int StopWalkId = Animator.StringToHash("StopWalk");
        static readonly int TurnLeftId = Animator.StringToHash("TurnLeft");
        static readonly int TurnRightId = Animator.StringToHash("TurnRight");
        static readonly int PivotId = Animator.StringToHash("Pivot");
        static readonly int WantsMoveId = Animator.StringToHash("WantsMove");
        static readonly int PivotingId = Animator.StringToHash("Pivoting");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int AirborneId = Animator.StringToHash("Airborne");
        static readonly int CrouchingId = Animator.StringToHash("Crouching");

        bool wasPivoting, wantedMove;
        float turnAccum, nextTurnStep;

        float bank, pitch, lastSpeed;
        int actionLayerIndex = -1;
        float actionWeight, actionUntil;
        bool dead;

        public Animator Animator => animator;
        public bool IsPlayingAction => Time.time < actionUntil;
        /// <summary>攻撃・被弾・回避・死亡など、全身のアニメーションが腕を動かしている間 true。</summary>
        public bool ActionOwnsArms => dead || IsPlayingAction;
        /// <summary>回避や死亡など、体ごと転がる動きの間 true（長い銃は背中に回す）。</summary>
        public bool TumblingBody => dead || (IsPlayingAction && currentAction == "Roll");
        string currentAction;

        void Reset()
        {
            motor = GetComponentInParent<CharacterMotor>();
            animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            if (!motor) motor = GetComponentInParent<CharacterMotor>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator) actionLayerIndex = animator.GetLayerIndex(actionLayer);
            if (motor && useLocomotionTransitions) motor.Jumped += () => { if (animator) animator.SetTrigger(JumpId); };
        }

        public const int HitFront = 0, HitBack = 1, HitLeft = 2, HitRight = 3, HitHeavy = 4;
        static readonly int HitDirId = Animator.StringToHash("HitDir");

        /// <summary>次の被弾アニメーションの種類（HitFront など）を指定する。</summary>
        public void SetHitDirection(int direction) { if (animator) animator.SetInteger(HitDirId, direction); }

        /// <summary>全身の単発動作（"Melee" "Hit" など Animator のトリガー名）を、指定秒数だけ再生する。</summary>
        public void PlayAction(string trigger, float duration)
        {
            if (!animator || dead) return;
            animator.SetTrigger(trigger);
            currentAction = trigger;
            actionUntil =Mathf.Max(actionUntil, Time.time + duration);
        }

        /// <summary>死亡状態にする／戻す。死亡中は倒れたアニメーションのまま止まる。</summary>
        public void SetDead(bool value)
        {
            dead = value;
            if (!animator) return;
            animator.SetBool(DeadId, value);
            if (!value) { actionUntil = 0f; animator.ResetTrigger("Melee"); animator.ResetTrigger("Hit"); }
        }

        void LateUpdate()
        {
            if (!motor || !animator) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);

            bool actionActive = dead || Time.time < actionUntil;
            actionWeight = Mathf.MoveTowards(actionWeight, actionActive ? 1f : 0f, dt * (actionActive ? 20f : 6f));
            if (actionLayerIndex >= 0) animator.SetLayerWeight(actionLayerIndex, actionWeight);
            float speed = motor.Speed;
            // 体の正面方向の速度（後ずさり中はマイナス → アニメーションを逆再生）
            float fwd = motor.ForwardSpeed;

            animator.SetFloat(SpeedId, fwd, speedDampTime, dt);
            float absFwd = Mathf.Abs(fwd);
            animator.SetFloat(AnimSpeedId, absFwd > sprintClipSpeed ? absFwd / sprintClipSpeed : 1f);
            animator.SetBool(GroundedId, motor.IsGrounded);
            if (useLocomotionTransitions) UpdateLocomotionTransitions(dt, actionActive);

            if (leanRoot && !dead)
            {
                float accel = (speed - lastSpeed) / dt;
                float turn = Mathf.Sign(motor.TurnRate) * Mathf.Max(0f, Mathf.Abs(motor.TurnRate) - bankDeadZone);
                float targetBank = Mathf.Clamp(-turn * speed * bankFactor, -maxBankAngle, maxBankAngle);
                float targetPitch = Mathf.Clamp(accel * pitchFactor, -maxPitchAngle, maxPitchAngle);
                float k = 1f - Mathf.Exp(-leanSmoothing * dt);
                bank = Mathf.Lerp(bank, targetBank, k);
                pitch = Mathf.Lerp(pitch, targetPitch, k);
                leanRoot.localRotation = Quaternion.Euler(pitch, 0f, bank);
            }
            lastSpeed = speed;
        }

        /// <summary>Motor の状態の変化を見て、停止・切り返し・その場旋回のアニメーションを始める。</summary>
        void UpdateLocomotionTransitions(float dt, bool busy)
        {
            bool wantsMove = motor.DesiredSpeed > 0.05f;
            bool pivoting = motor.IsPivoting;
            animator.SetBool(WantsMoveId, wantsMove);
            animator.SetBool(PivotingId, pivoting);
            animator.SetBool(AirborneId, motor.IsAirborne);
            animator.SetBool(CrouchingId, motor.IsCrouching);
            if (!busy && !dead && motor.IsGrounded && !motor.IsDashing && !motor.IsCrouching && !motor.IsAirborne)
            {
                if (pivoting && !wasPivoting) animator.SetTrigger(PivotId);
                else if (wantedMove && !wantsMove && !motor.IsFreeFacing)
                {
                    if (lastSpeed >= runStopSpeed) animator.SetTrigger(StopRunId);
                    else if (lastSpeed >= walkStopSpeed) animator.SetTrigger(StopWalkId);
                }

                // ほぼ止まったまま向きが変わった分をためて、一定角度ごとに足を踏みかえる
                if (motor.Speed < 0.3f && !wantsMove)
                {
                    turnAccum += motor.TurnRate * dt;
                    if (Mathf.Abs(turnAccum) >= turnStepAngle && Time.time >= nextTurnStep)
                    {
                        animator.SetTrigger(turnAccum > 0f ? TurnRightId : TurnLeftId);
                        nextTurnStep = Time.time + turnStepCooldown;
                        turnAccum = 0f;
                    }
                    else if (Mathf.Abs(motor.TurnRate) < 5f) turnAccum = Mathf.MoveTowards(turnAccum, 0f, 60f * dt);
                }
                else turnAccum = 0f;
            }
            wasPivoting = pivoting;
            wantedMove = wantsMove;
        }
    }
}
