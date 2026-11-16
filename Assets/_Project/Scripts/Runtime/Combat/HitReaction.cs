using System;
using Matoi.Characters;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// キャラクターの「攻撃を受けた時の反応」：白く光る・後ろへ押される・ひるむ・倒れる。
    /// プレイヤーと敵で共通。ひるみの長さや吹き飛びやすさは数値で変えられる（重い敵はひるみにくく等）。
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HitReaction : MonoBehaviour
    {
        [SerializeField] CharacterMotor motor;
        [SerializeField] CharacterAnimator characterAnimator;
        [SerializeField] MeleeAttack melee;

        [Header("反応")]
        [Tooltip("ひるんで動けない時間（秒）")]
        public float staggerTime = 0.35f;
        [Tooltip("吹き飛びやすさ（0=押されない, 1=標準）")]
        public float knockbackScale = 1f;
        [Tooltip("この量以上のダメージでのみひるむ（小さな攻撃は無視する等）")]
        public float staggerThreshold = 1f;
        [Tooltip("一度ひるんだら、この秒数はひるまない（連射でハメ続けられないように）")]
        public float staggerCooldown = 0f;
        [Tooltip("白く光らせる")]
        public bool flash = true;

        float lastStagger = -10f;
        public int StaggerCount { get; private set; }

        Health health;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;
        Color[] baseColors;
        float flashAmount;

        public float StaggeredUntil { get; private set; }
        public bool IsStaggered => Time.time < StaggeredUntil;
        public Health Health => health;
        public event Action<DamageInfo> Reacted;

        void Awake()
        {
            health = GetComponent<Health>();
            if (!motor) motor = GetComponent<CharacterMotor>();
            if (!characterAnimator) characterAnimator = GetComponent<CharacterAnimator>();
            if (!melee) melee = GetComponent<MeleeAttack>();
            renderers = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial;
                baseColors[i] = m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            }
            health.OnDamaged += OnDamaged;
            health.OnDied += OnDied;
        }

        void OnDamaged(DamageInfo info)
        {
            if (flash) flashAmount = 1f;
            if (motor) motor.AddImpulse(info.direction * info.knockback * knockbackScale);
            if (health.IsDead || info.amount < staggerThreshold) return;
            // 近接攻撃は必ずひるませる（殴る手応えを優先）。射撃はクールダウン中ならひるまない
            if (!info.isMelee && Time.time < lastStagger + staggerTime + staggerCooldown) return;
            lastStagger = Time.time;
            StaggerCount++;
            StaggeredUntil = Time.time + staggerTime;
            if (melee) melee.Cancel();
            if (characterAnimator) characterAnimator.PlayAction("Hit", staggerTime + 0.1f);
            Reacted?.Invoke(info);
        }

        void OnDied(DamageInfo info)
        {
            if (melee) melee.Cancel();
            if (motor) motor.SetIntent(MoveIntent.None);
            if (characterAnimator) characterAnimator.SetDead(true);
        }

        /// <summary>生き返らせる（リスポーン時）。</summary>
        public void Revive()
        {
            health.ResetHealth();
            StaggeredUntil = 0f;
            if (characterAnimator) characterAnimator.SetDead(false);
        }

        void LateUpdate()
        {
            if (flashAmount <= 0f && !flash) return;
            float prev = flashAmount;
            flashAmount = Mathf.MoveTowards(flashAmount, 0f, Time.deltaTime * 7f);
            if (prev <= 0f) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                renderers[i].GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.Lerp(baseColors[i], Color.white * 1.6f, flashAmount));
                renderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
