using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 訓練用の的（かかし）。撃たれると揺れて白く光り、倒すと倒れて数秒後に起き上がる。
    /// 敵キャラクターの「被弾リアクション」の試作も兼ねる。
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour
    {
        [Tooltip("揺らす見た目（根元が原点）")]
        [SerializeField] Transform visual;
        [SerializeField] float respawnDelay = 3f;
        [Tooltip("ばねの硬さ・減衰（揺れの戻り方）")]
        [SerializeField] float stiffness = 90f, damping = 9f;
        [SerializeField] float hitTiltPerKnockback = 7f;

        Health health;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        Color[] baseColors;
        Vector2 tilt, tiltVel;     // x: 前後, y: 左右（度）
        float flash, deadUntil = -1f;

        public Health Health => health;

        void Awake()
        {
            health = GetComponent<Health>();
            if (!visual) visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            renderers = visual.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                baseColors[i] = renderers[i].sharedMaterial && renderers[i].sharedMaterial.HasProperty("_BaseColor")
                    ? renderers[i].sharedMaterial.GetColor("_BaseColor") : Color.white;
            mpb = new MaterialPropertyBlock();
            health.OnDamaged += OnDamaged;
            health.OnDied += OnDied;
        }

        void OnDamaged(DamageInfo info)
        {
            Vector3 local = transform.InverseTransformDirection(info.direction);
            tiltVel += new Vector2(local.z, -local.x) * info.knockback * hitTiltPerKnockback * 6f;
            flash = 1f;
        }

        void OnDied(DamageInfo info) => deadUntil = Time.time + respawnDelay;

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 target = Vector2.zero;
            if (health.IsDead)
            {
                target = new Vector2(80f, 0f); // 後ろへ倒れる
                if (Time.time >= deadUntil) { health.ResetHealth(); }
            }
            tiltVel += (-(tilt - target) * stiffness - tiltVel * damping) * dt;
            tilt += tiltVel * dt;
            tilt.x = Mathf.Clamp(tilt.x, -85f, 85f);
            tilt.y = Mathf.Clamp(tilt.y, -60f, 60f);
            visual.localRotation = Quaternion.Euler(tilt.x, 0f, tilt.y);

            if (flash > 0f || mpb != null)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].GetPropertyBlock(mpb);
                    mpb.SetColor("_BaseColor", Color.Lerp(baseColors[i], new Color(1f, 0.95f, 0.85f), flash));
                    renderers[i].SetPropertyBlock(mpb);
                }
            }
        }
    }
}
