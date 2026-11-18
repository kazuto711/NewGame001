using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Matoi.Player
{
    /// <summary>
    /// プレイヤーの被弾演出と、死亡 → 復活を担当する。
    ///  - 被弾：画面のふちが赤く光る＋画面揺れ
    ///  - 死亡：武器をしまって倒れ、数秒後にスタート地点で復活
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerLife : MonoBehaviour
    {
        [Tooltip("被弾時に一瞬だけ重みを上げるポストプロセス（赤いふち）")]
        [SerializeField] Volume damageVolume;
        [SerializeField] float damageFlashFade = 2.5f;
        [SerializeField] float respawnDelay = 3f;

        Health health;
        HitReaction reaction;
        CharacterMotor motor;
        WeaponHolder weapons;
        Vector3 spawnPos;
        float spawnYaw, deathTime = -1f, lowHealthPulse;

        public bool IsDead => health.IsDead;
        public int DeathCount { get; private set; }
        public Vector3 RespawnPosition => spawnPos;
        public float RespawnYaw => spawnYaw;

        /// <summary>復活する場所を変える（寝床で休んだ時）。</summary>
        public void SetRespawnPoint(Vector3 position, float yaw)
        {
            spawnPos = position;
            spawnYaw = yaw;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            reaction = GetComponent<HitReaction>();
            motor = GetComponent<CharacterMotor>();
            weapons = GetComponent<WeaponHolder>();
            health.OnDamaged += OnDamaged;
            health.OnDied += _ =>
            {
                deathTime = Time.time;
                DeathCount++;
                if (weapons) weapons.SetDrawn(false);
                CameraShake.Shake(0.6f);
            };
        }

        void Start()
        {
            spawnPos = transform.position;
            spawnYaw = transform.eulerAngles.y;
            if (damageVolume) damageVolume.weight = 0f;
        }

        void OnDamaged(DamageInfo info)
        {
            if (damageVolume) damageVolume.weight = 1f;
            CameraShake.Shake(0.3f);
        }

        void Update()
        {
            if (damageVolume)
            {
                // 体力が少ない時は、赤いふちがゆっくり脈打つ
                float low = health.Current / health.Max < 0.3f && !health.IsDead ? 0.35f + Mathf.Sin(Time.time * 5f) * 0.15f : 0f;
                damageVolume.weight = Mathf.Max(low, Mathf.MoveTowards(damageVolume.weight, 0f, damageFlashFade * Time.deltaTime));
            }
            if (health.IsDead && deathTime > 0f && Time.time > deathTime + respawnDelay) Respawn();
        }

        public void Respawn()
        {
            deathTime = -1f;
            if (reaction) reaction.Revive(); else health.ResetHealth();
            motor.Teleport(spawnPos, spawnYaw);
        }
    }
}
