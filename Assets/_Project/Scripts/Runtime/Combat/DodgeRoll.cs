using System;
using Matoi.Characters;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 回避（前転）。押した方向へ素早く転がり、途中の一瞬だけ無敵になる。
    /// 敵の「ため」を見てから押すと、攻撃をすり抜けられる。
    /// パワーアーマーでは「ステップ（短い横移動）」など、数値を変えて別の回避にできる。
    /// </summary>
    public class DodgeRoll : MonoBehaviour
    {
        [SerializeField] CharacterMotor motor;
        [SerializeField] CharacterAnimator characterAnimator;
        [SerializeField] Health health;
        [SerializeField] MeleeAttack melee;

        [Header("性能")]
        [Tooltip("転がり始めの速さ (m/s)")]
        public float speed = 8.5f;
        public float duration = 0.5f;
        [Tooltip("無敵の始まり・終わり（転がり始めからの秒）")]
        public Vector2 invulnerableWindow = new Vector2(0.03f, 0.38f);
        [Tooltip("回避が終わってから次に回避できるまで")]
        public float cooldown = 0.2f;

        float start = -10f;

        public bool IsRolling => Time.time - start < duration;
        public int RollCount { get; private set; }
        public event Action Rolled;

        void Awake()
        {
            if (!motor) motor = GetComponent<CharacterMotor>();
            if (!characterAnimator) characterAnimator = GetComponent<CharacterAnimator>();
            if (!health) health = GetComponent<Health>();
            if (!melee) melee = GetComponent<MeleeAttack>();
        }

        public bool TryRoll(Vector3 direction)
        {
            if (IsRolling || Time.time < start + duration + cooldown) return false;
            if (health && health.IsDead) return false;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction.Normalize();

            start = Time.time;
            RollCount++;
            if (melee) melee.Cancel();
            motor.Dash(direction * speed, duration);
            if (health) health.InvulnerableUntil = Time.time + invulnerableWindow.y;
            // 無敵の開始を少し遅らせたい場合のため、開始時刻も記録しておく（現状は即時）
            if (characterAnimator) characterAnimator.PlayAction("Roll", duration);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Whoosh, 0.5f, 0.15f);
            Rolled?.Invoke();
            return true;
        }
    }
}
