using System;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;

namespace Matoi.Player
{
    /// <summary>敵からどう見られているか（しゃがみ中の画面表示に使う）。</summary>
    public enum StealthStatus
    {
        Hidden,    // 隠密：誰にも気づかれていない
        Caution,   // 警戒：怪しまれている
        Detected,  // 発見：見つかっている
    }

    /// <summary>
    /// ステルス：しゃがんでいる間は敵に見つかりにくくなり、気づかれていない敵への攻撃が強くなる。
    /// 音（足音・銃声・着地）を周りの敵に伝え、敵全体の様子をまとめて画面表示へ渡す。
    /// </summary>
    public class PlayerStealth : MonoBehaviour
    {
        public static PlayerStealth Instance { get; private set; }

        [Header("足音が聞こえる距離 (m)")]
        [Tooltip("しゃがんで止まっている時")]
        [SerializeField] float stillNoise = 0f;
        [Tooltip("しゃがみ歩きの最高速の時")]
        [SerializeField] float crouchWalkNoise = 2.5f;

        [Header("大きな音が届く距離 (m)")]
        [SerializeField] float pistolNoise = 20f;
        [SerializeField] float rifleNoise = 30f;
        [SerializeField] float landingNoise = 6f;

        [Header("隠密攻撃の倍率")]
        public float meleeMultiplier = 3f;
        public float gunMultiplier = 2f;

        CharacterMotor motor;
        WeaponShooter shooter;
        Matoi.Equipment.PowerArmorUser armor;

        public bool IsSneaking => motor && motor.IsCrouching && !(armor && armor.IsArmored);
        public StealthStatus Status { get; private set; } = StealthStatus.Hidden;
        /// <summary>一番気づいている敵の警戒度（0〜1）。</summary>
        public float Awareness { get; private set; }
        public int SneakAttackCount { get; private set; }
        /// <summary>隠密攻撃が決まった時（引数はダメージの倍率）。</summary>
        public event Action<float> SneakAttacked;

        /// <summary>今の足音が聞こえる距離 (m)。</summary>
        public float FootstepNoise => motor && motor.Profile
            ? Mathf.Lerp(stillNoise, crouchWalkNoise, Mathf.Clamp01(motor.Speed / Mathf.Max(0.1f, motor.Profile.crouchSpeed))) : 0f;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            shooter = GetComponent<WeaponShooter>();
            armor = GetComponent<Matoi.Equipment.PowerArmorUser>();
        }

        void OnEnable()
        {
            Instance = this;
            if (shooter) shooter.Fired += OnFired;
            if (motor) motor.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (shooter) shooter.Fired -= OnFired;
            if (motor) motor.Landed -= OnLanded;
        }

        void OnFired(WeaponDefinition def) =>
            EmitNoise(transform.position, def.stance == WeaponStance.Rifle ? rifleNoise : pistolNoise);

        void OnLanded(float fallSpeed) => EmitNoise(transform.position, landingNoise);

        /// <summary>音を立てる。届く範囲の敵は、音のした場所を調べに来る。</summary>
        public void EmitNoise(Vector3 position, float radius)
        {
            foreach (var enemy in EnemyBrain.All) enemy.HearNoise(position, radius);
        }

        /// <summary>敵が隠密攻撃を受けた時に呼ばれる。</summary>
        public void NotifySneakAttack(float multiplier)
        {
            SneakAttackCount++;
            SneakAttacked?.Invoke(multiplier);
        }

        void Update()
        {
            var status = StealthStatus.Hidden;
            float awareness = 0f;
            foreach (var enemy in EnemyBrain.All)
            {
                if (enemy.Current == EnemyBrain.State.Dead) continue;
                awareness = Mathf.Max(awareness, enemy.Awareness);
                if (enemy.IsHuntingPlayer) status = StealthStatus.Detected;
                else if (enemy.IsSuspicious && status == StealthStatus.Hidden) status = StealthStatus.Caution;
            }
            Status = status;
            Awareness = status == StealthStatus.Detected ? 1f : awareness;
        }
    }
}
