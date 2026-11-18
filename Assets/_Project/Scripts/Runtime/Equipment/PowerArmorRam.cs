using System.Collections.Generic;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Equipment
{
    /// <summary>
    /// パワーアーマーでダッシュ中、前にいる敵をはね飛ばす（体当たり）。重さの表現と、囲まれた時の脱出手段。
    /// </summary>
    public class PowerArmorRam : MonoBehaviour
    {
        [SerializeField] CharacterMotor motor;
        [SerializeField] float damage = 25f;
        [SerializeField] float knockback = 9f;
        [SerializeField] float radius = 0.9f;
        [SerializeField] float reach = 0.8f;
        [Tooltip("同じ相手に続けて当たらない時間")]
        [SerializeField] float perTargetCooldown = 0.8f;
        [Tooltip("この速さ以上で体当たりになる")]
        [SerializeField] float minSpeed = 3.2f;

        readonly Dictionary<IDamageable, float> lastHit = new Dictionary<IDamageable, float>();
        readonly Collider[] buffer = new Collider[16];
        Collider[] own;

        public int RamHits { get; private set; }

        void Awake()
        {
            if (!motor) motor = GetComponent<CharacterMotor>();
            own = GetComponentsInChildren<Collider>();
        }

        void Update()
        {
            if (motor.Speed < minSpeed) return;
            Vector3 c = transform.position + Vector3.up * 1.0f + transform.forward * reach;
            int n = Physics.OverlapSphereNonAlloc(c, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                if (System.Array.IndexOf(own, col) >= 0 || Health.AreAllies(gameObject, col.gameObject)) continue;
                var d = col.GetComponentInParent<IDamageable>();
                if (d == null || d.IsDead) continue;
                if (lastHit.TryGetValue(d, out float t) && Time.time < t + perTargetCooldown) continue;
                lastHit[d] = Time.time;
                // 進行方向＋少し横へはじく（道を空けるように）
                Vector3 side = Vector3.Cross(Vector3.up, transform.forward);
                Vector3 toTarget = col.transform.position - transform.position;
                Vector3 dir = (transform.forward + side * Mathf.Sign(Vector3.Dot(toTarget, side)) * 0.6f).normalized;
                d.TakeDamage(new DamageInfo
                {
                    amount = damage, point = col.ClosestPoint(c), direction = dir, knockback = knockback,
                    source = gameObject, isMelee = true,
                });
                RamHits++;
                CameraShake.Shake(0.4f);
                CombatFx.Instance?.PlaySound(ProceduralSfx.Punch, 1f);
                CombatFx.Instance?.Impact(col.ClosestPoint(c), -transform.forward, true);
                HitStop.Do(0.05f);
            }
        }
    }
}
