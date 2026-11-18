using Matoi.Combat;
using UnityEngine;

namespace Matoi.AI
{
    /// <summary>
    /// 敵が吐く弾。ためている間は口元でふくらみ、放つとまっすぐ飛ぶ。
    /// 弾の速さは目で見て避けられる程度にしてある。
    /// </summary>
    public class EnemyProjectile : MonoBehaviour
    {
        const float Radius = 0.17f, LifeTime = 4f, ChargeScale = 0.42f;

        readonly RaycastHit[] hits = new RaycastHit[12];
        GameObject shooter;
        float damage, speed, dieAt, chargeTime, chargeLength = 1f;
        Vector3 velocity;
        bool launched;

        /// <summary>口元に弾を作り、ため始める。</summary>
        public static EnemyProjectile BeginCharge(GameObject owner, EnemyDefinition definition, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "EnemyProjectile";
            var sphere = go.GetComponent<Collider>();
            sphere.enabled = false;
            Destroy(sphere);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.05f;

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (definition.projectileMaterial) meshRenderer.sharedMaterial = definition.projectileMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = definition.projectileColor;
            glow.range = 3.5f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;

            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = meshRenderer.sharedMaterial;
            trail.time = 0.18f;
            trail.startWidth = 0.26f;
            trail.endWidth = 0f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;

            var projectile = go.AddComponent<EnemyProjectile>();
            projectile.shooter = owner;
            projectile.damage = definition.projectileDamage;
            projectile.speed = definition.projectileSpeed;
            projectile.chargeLength = Mathf.Max(0.05f, definition.rangedWindup);
            return projectile;
        }

        /// <summary>ためている間、口元についていく。</summary>
        public void HoldAt(Vector3 position)
        {
            if (!launched) transform.position = position;
        }

        /// <summary>指定した位置へ向けて放つ。</summary>
        public void Launch(Vector3 target)
        {
            launched = true;
            transform.localScale = Vector3.one * ChargeScale;
            velocity = (target - transform.position).normalized * speed;
            dieAt = Time.time + LifeTime;
            var trail = GetComponent<TrailRenderer>();
            if (trail) trail.emitting = true;
            CombatFx.Instance?.PlaySound(ProceduralSfx.Whoosh, 0.7f);
        }

        void Update()
        {
            if (!launched)
            {
                chargeTime += Time.deltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(0.05f, ChargeScale, chargeTime / chargeLength);
                return;
            }
            if (Time.time >= dieAt) { Destroy(gameObject); return; }

            Vector3 step = velocity * Time.deltaTime;
            float distance = step.magnitude;
            if (distance <= 0f) return;
            if (FindHit(step / distance, distance, out var hit)) { Strike(hit); return; }
            transform.position += step;
        }

        bool FindHit(Vector3 direction, float distance, out RaycastHit best)
        {
            best = default;
            float nearest = float.MaxValue;
            int n = Physics.SphereCastNonAlloc(transform.position, Radius, direction, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var other = hits[i].collider.gameObject;
                if (shooter && (other.transform.IsChildOf(shooter.transform) || Health.AreAllies(shooter, other))) continue;
                if (hits[i].distance < nearest) { nearest = hits[i].distance; best = hits[i]; }
            }
            return nearest < float.MaxValue;
        }

        void Strike(RaycastHit hit)
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            Vector3 point = hit.distance > 0f ? hit.point : transform.position;
            if (target != null && !target.IsDead)
                target.TakeDamage(new DamageInfo { amount = damage, point = point, direction = velocity.normalized, knockback = 2f, source = shooter });
            CombatFx.Instance?.Impact(point, hit.normal, target != null);
            Destroy(gameObject);
        }
    }
}
