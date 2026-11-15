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
        [SerializeField] float maxBankAngle = 9f;
        [Tooltip("旋回の速さ×移動速度 → 傾きの係数")]
        [SerializeField] float bankFactor = 0.0035f;
        [Tooltip("加速・減速で前後に傾く最大角度")]
        [SerializeField] float maxPitchAngle = 6f;
        [SerializeField] float pitchFactor = 0.9f;
        [SerializeField] float leanSmoothing = 10f;

        [Header("単発の動作（攻撃・被弾・死亡）")]
        [Tooltip("全身の単発動作を再生するレイヤー名（Animator Controller 内）")]
        [SerializeField] string actionLayer = "Action";

        static readonly int DeadId = Animator.StringToHash("Dead");

        float bank, pitch, lastSpeed;
        int actionLayerIndex = -1;
        float actionWeight, actionUntil;
        bool dead;

        public Animator Animator => animator;
        public bool IsPlayingAction => Time.time < actionUntil;

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
        }

        /// <summary>全身の単発動作（"Melee" "Hit" など Animator のトリガー名）を、指定秒数だけ再生する。</summary>
        public void PlayAction(string trigger, float duration)
        {
            if (!animator || dead) return;
            animator.SetTrigger(trigger);
            actionUntil = Mathf.Max(actionUntil, Time.time + duration);
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

            if (leanRoot && !dead)
            {
                float accel = (speed - lastSpeed) / dt;
                float targetBank = Mathf.Clamp(-motor.TurnRate * speed * bankFactor, -maxBankAngle, maxBankAngle);
                float targetPitch = Mathf.Clamp(accel * pitchFactor, -maxPitchAngle, maxPitchAngle);
                float k = 1f - Mathf.Exp(-leanSmoothing * dt);
                bank = Mathf.Lerp(bank, targetBank, k);
                pitch = Mathf.Lerp(pitch, targetPitch, k);
                leanRoot.localRotation = Quaternion.Euler(pitch, 0f, bank);
            }
            lastSpeed = speed;
        }
    }
}
