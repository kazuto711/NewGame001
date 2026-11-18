using System;
using System.Collections.Generic;
using Matoi.CameraSystem;
using Matoi.Characters;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>コンボの 1 段分。基本の値（damage など）に掛ける倍率で表す。</summary>
    [Serializable]
    public struct MeleeComboStep
    {
        [Tooltip("Animator の MeleeStep の値（0=クロス, 1=ジャブ, 2=フック）")]
        public int animation;
        [Tooltip("攻撃開始から当たり判定が出るまで（秒）。こぶしが伸びきる瞬間に合わせる")]
        public float hitTime;
        [Tooltip("この段の長さ（秒）")]
        public float duration;
        public float damageScale;
        public float reachScale;
        public float lungeScale;
        public float knockbackScale;
        public float hitStopScale;
        public float shakeScale;
        [Tooltip("強い攻撃（相手が大きくよろける）")]
        public bool heavy;
    }

    /// <summary>
    /// 近接攻撃。踏み込み → 当たり判定 → ノックバック・ヒットストップ・画面揺れ。
    /// comboEnabled がオンのキャラは、続けて押すと 3 段コンボになる。
    /// </summary>
    public class MeleeAttack : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] CharacterMotor motor;
        [SerializeField] CharacterAnimator characterAnimator;

        static readonly int MeleeStepId = Animator.StringToHash("MeleeStep");

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

        [Header("コンボ（プレイヤーのみオン）")]
        public bool comboEnabled;
        [Tooltip("攻撃が終わってから、この秒数以内に押すと次の段につながる")]
        public float comboWindow = 0.35f;
        [Tooltip("段の進み具合がこれ以上の時に押すと、先行入力として受け付ける")]
        [Range(0f, 1f)] public float bufferFrom = 0.3f;
        [Tooltip("先行入力がある時、段の進み具合がここまで来たら次の段を始める")]
        [Range(0f, 1f)] public float cancelAt = 0.72f;
        public MeleeComboStep[] combo =
        {
            new MeleeComboStep { animation = 1, hitTime = 0.12f, duration = 0.42f, damageScale = 1f, reachScale = 1f, lungeScale = 0.8f, knockbackScale = 0.5f, hitStopScale = 0.8f, shakeScale = 0.7f },
            new MeleeComboStep { animation = 0, hitTime = 0.15f, duration = 0.48f, damageScale = 1.2f, reachScale = 1.05f, lungeScale = 1f, knockbackScale = 0.7f, hitStopScale = 1f, shakeScale = 1f },
            new MeleeComboStep { animation = 2, hitTime = 0.22f, duration = 0.72f, damageScale = 1.8f, reachScale = 1.15f, lungeScale = 1.4f, knockbackScale = 1.6f, hitStopScale = 1.8f, shakeScale = 1.8f, heavy = true },
        };

        float start = -10f, endTime = -10f;
        bool hitDone, queued;
        Vector3 queuedDirection;
        int step = -1;
        MeleeComboStep current;
        Collider[] own;
        WeaponHolder weapons;
        readonly Collider[] overlap = new Collider[16];

        float CurrentDuration => step >= 0 ? current.duration : duration;
        float CurrentHitTime => step >= 0 ? current.hitTime : hitTime;

        public bool IsAttacking => Time.time - start < CurrentDuration;
        /// <summary>攻撃の進み具合（0〜1）。攻撃していない時は -1。</summary>
        public float Progress => IsAttacking ? (Time.time - start) / CurrentDuration : -1f;
        /// <summary>当たり判定が出る瞬間の進み具合（0〜1）。</summary>
        public float HitProgress => CurrentHitTime / Mathf.Max(0.01f, CurrentDuration);
        /// <summary>今のコンボの段（0 から。コンボでなければ -1）。</summary>
        public int CurrentStep => step;
        /// <summary>今の攻撃の、当たり判定が出るまでの時間（秒）。</summary>
        public float CurrentAttackHitTime => CurrentHitTime;
        public bool CurrentIsHeavy => step >= 0 && current.heavy;
        public int HitCount { get; private set; }
        public event Action<int> Struck; // 当たった数

        /// <summary>ライフルを構えている間は銃で突くので、コンボにしない。</summary>
        bool CanCombo => comboEnabled && combo != null && combo.Length > 0
                         && !(weapons && weapons.IsDrawn && weapons.CurrentWeapon && weapons.CurrentWeapon.stance == WeaponStance.Rifle);

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!motor) motor = GetComponent<CharacterMotor>();
            if (!characterAnimator) characterAnimator = GetComponent<CharacterAnimator>();
            weapons = GetComponent<WeaponHolder>();
            own = GetComponentsInChildren<Collider>();
        }

        /// <summary>攻撃を途中で取り消す（被弾でひるんだ時など）。</summary>
        public void Cancel()
        {
            start = -10f;
            hitDone = true;
            queued = false;
            step = -1;
        }

        /// <summary>指定した向きへ攻撃する。攻撃中・硬直中なら false。</summary>
        /// <param name="playAnimation">false なら腕の動きは呼び出し側が再生する（敵の予備動作つき攻撃など）</param>
        public bool TryAttack(Vector3 direction, bool playAnimation = true)
        {
            bool canCombo = CanCombo;
            if (IsAttacking)
            {
                // 攻撃中に押した時は、次の段の先行入力として覚えておく
                if (!canCombo || step < 0 || step >= combo.Length - 1 || Progress < bufferFrom) return false;
                queued = true;
                queuedDirection = direction;
                return true;
            }

            bool chain = canCombo && step >= 0 && step < combo.Length - 1 && Time.time <= endTime + comboWindow;
            if (!chain && Time.time < endTime + cooldown) return false;
            Begin(canCombo ? (chain ? step + 1 : 0) : -1, direction, playAnimation);
            return true;
        }

        void Begin(int nextStep, Vector3 direction, bool playAnimation)
        {
            step = nextStep;
            if (step >= 0) current = combo[step];
            queued = false;
            start = Time.time;
            endTime = start + CurrentDuration;
            hitDone = false;
            if (motor)
            {
                motor.FaceImmediately(direction);
                motor.AddImpulse(transform.forward * (lunge * (step >= 0 ? current.lungeScale : 1f)));
            }
            if (!playAnimation) return;
            if (animator) animator.SetInteger(MeleeStepId, step >= 0 ? current.animation : 0);
            if (characterAnimator) characterAnimator.PlayAction("Melee", CurrentDuration);
            else if (animator) animator.SetTrigger("Melee");
        }

        void Update()
        {
            float t = Time.time - start;
            if (t < CurrentDuration && !hitDone && t >= CurrentHitTime) { hitDone = true; DoHit(); }
            // 先行入力：動きの後半に入ったら、次の段へつなぐ
            if (queued && hitDone && t >= CurrentDuration * cancelAt) Begin(step + 1, queuedDirection, true);
        }

        void DoHit()
        {
            float scaledReach = reach * (step >= 0 ? current.reachScale : 1f);
            Vector3 center = transform.position + Vector3.up * 1.0f + transform.forward * scaledReach;
            int n = Physics.OverlapSphereNonAlloc(center, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            var done = new HashSet<IDamageable>();
            bool killed = false;
            for (int i = 0; i < n; i++)
            {
                if (Array.IndexOf(own, overlap[i]) >= 0) continue;
                if (Health.AreAllies(gameObject, overlap[i].gameObject)) continue; // 味方には当たらない
                var d = overlap[i].GetComponentInParent<IDamageable>();
                if (d == null || d.IsDead || !done.Add(d)) continue;
                Vector3 dir = overlap[i].transform.position - transform.position; dir.y = 0;
                d.TakeDamage(new DamageInfo
                {
                    amount = damage * (step >= 0 ? current.damageScale : 1f),
                    point = overlap[i].ClosestPoint(center),
                    direction = dir.normalized,
                    knockback = knockback * (step >= 0 ? current.knockbackScale : 1f),
                    source = gameObject,
                    isMelee = true,
                    heavy = CurrentIsHeavy,
                });
                killed |= d.IsDead;
            }
            HitCount += done.Count;
            var fx = CombatFx.Instance;
            if (done.Count > 0)
            {
                // 強い攻撃・とどめほど長く深く止める
                float stop = hitStop * (step >= 0 ? current.hitStopScale : 1f) * (killed ? 1.3f : 1f);
                HitStop.Do(stop, CurrentIsHeavy || killed ? 0.03f : 0.1f);
                CameraShake.Shake(shake * (step >= 0 ? current.shakeScale : 1f));
                if (CurrentIsHeavy && CompareTag("Player")) ThirdPersonCameraController.Instance?.AddFovKick(3f);
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
