using System;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>1回の攻撃の情報。</summary>
    public struct DamageInfo
    {
        public float amount;
        /// <summary>当たった位置（ワールド）</summary>
        public Vector3 point;
        /// <summary>攻撃が進んできた向き（ノックバック方向）</summary>
        public Vector3 direction;
        /// <summary>吹き飛ばす強さ (m/s)</summary>
        public float knockback;
        public GameObject source;
        public bool isMelee;
    }

    /// <summary>攻撃を受けられるもの（敵・プレイヤー・壊せる物）。</summary>
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsDead { get; }
    }

    /// <summary>勢力。同じ勢力どうしは攻撃が当たらない（Neutral は誰からも当たる）。</summary>
    public enum Faction
    {
        Neutral,
        Player,
        Hostile,
    }

    /// <summary>
    /// 体力。プレイヤー・敵・訓練用の的などで共通に使う。
    /// 見た目の反応（ひるみ・点滅など）は OnDamaged / OnDied を受け取る別の部品が担当する。
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] float maxHealth = 100f;
        [SerializeField] Faction faction = Faction.Neutral;

        float current;
        public Faction Faction => faction;

        /// <summary>a と b が味方どうし（攻撃が当たらない）か。</summary>
        public static bool AreAllies(GameObject a, GameObject b)
        {
            if (!a || !b) return false;
            var ha = a.GetComponentInParent<Health>();
            var hb = b.GetComponentInParent<Health>();
            return ha && hb && ha.faction != Faction.Neutral && ha.faction == hb.faction;
        }

        public void Configure(float max, Faction f)
        {
            maxHealth = max;
            faction = f;
            current = max;
        }
        public float Max => maxHealth;
        public float Current => current;
        public bool IsDead => current <= 0f;

        public event Action<DamageInfo> OnDamaged;
        public event Action<DamageInfo> OnDied;

        void Awake() => current = maxHealth;

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || info.amount <= 0f) return;
            current = Mathf.Max(0f, current - info.amount);
            OnDamaged?.Invoke(info);
            if (IsDead) OnDied?.Invoke(info);
        }

        public void ResetHealth() => current = maxHealth;
    }
}
