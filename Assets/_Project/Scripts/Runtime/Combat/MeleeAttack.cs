using System;
using System.Collections.Generic;
using Matoi.CameraSystem;
using Matoi.Characters;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 近接攻撃（殴る・銃床で殴る）。踏み込み → 当たり判定 → ヒットストップ・画面揺れ・吹き飛ばし。
    /// 近接武器（バット・刀など）を追加する時は、この数値をデータ化して武器ごとに持たせる。
    /// </summary>
    public class MeleeAttack : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] CharacterMotor motor;
        [SerializeField] CharacterAnimator characterAnimator;

        [Header("性能")]
        public float damage = 35f;
        [Tooltip("体の前方、この距離を中心に当たり判定")]
        public float reach = 1.0f;
        public float radius = 0.75f;
        [Tooltip("攻撃開始から当たり判定が出るまで（秒）")]
        public float hitTime = 0.17f;
        [Tooltip("攻撃全体の長さ（秒）。この間は他の行動ができない")]
        public float duration = 0.5f;
        public float cooldown = 0.12f;

        [Header("手応え")]
        [Tooltip("踏み込みの勢い (m/s)")]
        public float lunge = 4f;
        public float knockback = 6f;
        public float hitStop = 0.07f;
        public float shake = 0.35f;

        float start = -10f, endTime = -10f;
        bool hitDone;
        Collider[] own;
        readonly Collider[] overlap = new Collider[16];

        public bool IsAttacking => Time.time - start < duration;
        public int HitCount { get; private set; }
        public event Action<int> Struck; // 当たった数

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!motor) motor = GetComponent<CharacterMotor>();
            if (!characterAnimator) characterAnimator = GetComponent<CharacterAnimator>();
            own = GetComponentsInChildren<Collider>();
        }

        /// <summary>攻撃を途中で取り消す（被弾でひるんだ時など）。</summary>
        public void Cancel() { start = -10f; hitDone = true; }

        /// <summary>指定した向きへ攻撃する。攻撃中・硬直中なら false。</summary>
        public bool TryAttack(Vector3 direction)
        {
            if (IsAttacking || Time.time < endTime + cooldown) return false;
            start = Time.time;
            endTime = start + duration;
            hitDone = false;
            if (motor)
            {
                motor.FaceImmediately(direction);
                motor.AddImpulse(transform.forward * lunge);
            }
            if (characterAnimator) characterAnimator.PlayAction("Melee", duration);
            else if (animator) animator.SetTrigger("Melee");
            return true;
        }

        void Update()
        {
            float t = Time.time - start;
            if (t < duration && !hitDone && t >= hitTime) { hitDone = true; DoHit(); }
        }

        void DoHit()
        {
            Vector3 center = transform.position + Vector3.up * 1.0f + transform.forward * reach;
            int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            var done = new HashSet<IDamageable>();
            for (int i = 0; i < n; i++)
            {
                if (Array.IndexOf(own, overlap[i]) >= 0) continue;
                if (Health.AreAllies(gameObject, overlap[i].gameObject)) continue; // 味方には当たらない
                var d = overlap[i].GetComponentInParent<IDamageable>();
                if (d == null || d.IsDead || !done.Add(d)) continue;
                Vector3 dir = overlap[i].transform.position - transform.position; dir.y = 0;
                d.TakeDamage(new DamageInfo
                {
                    amount = damage, point = overlap[i].ClosestPoint(center), direction = dir.normalized,
                    knockback = knockback, source = gameObject, isMelee = true,
                });
            }
            HitCount += done.Count;
            var fx = CombatFx.Instance;
            if (done.Count > 0)
            {
                HitStop.Do(hitStop);
                CameraShake.Shake(shake);
                fx?.PlaySound(ProceduralSfx.Punch);
                fx?.Impact(center, -transform.forward, true);
            }
            else fx?.PlaySound(ProceduralSfx.Whoosh, 0.6f);
            Struck?.Invoke(done.Count);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up + transform.forward * reach, radius);
        }
    }
}
