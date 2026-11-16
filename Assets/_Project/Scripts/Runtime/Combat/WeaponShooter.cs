using System;
using System.Collections.Generic;
using Matoi.CameraSystem;
using Matoi.Characters;
using UnityEngine;

namespace Matoi.Combat
{
    /// <summary>
    /// 射撃を担当する：引き金・連射・弾数・リロード・命中判定（ヒットスキャン）・反動・演出。
    /// 「誰が引き金を引くか」は知らない（プレイヤーも敵も SetTrigger / Reload を呼ぶだけ）。
    /// </summary>
    [RequireComponent(typeof(WeaponHolder))]
    public class WeaponShooter : MonoBehaviour
    {
        static readonly int ReloadId = Animator.StringToHash("Reload");

        [SerializeField] WeaponHolder holder;
        [SerializeField] CharacterMotor motor;
        [Tooltip("当たり判定の対象レイヤー")]
        [SerializeField] LayerMask hitMask = ~0;

        class AmmoState { public int mag; public int reserve; }
        readonly Dictionary<WeaponDefinition, AmmoState> ammo = new Dictionary<WeaponDefinition, AmmoState>();

        bool triggerHeld, triggerWasHeld;
        float nextFireTime, reloadEnd = -1f;
        Animator animator;
        Collider[] ownColliders;

        public bool IsReloading => reloadEnd > 0f;
        public int AmmoInMagazine => Current?.mag ?? 0;
        public int ReserveAmmo => Current?.reserve ?? 0;
        public int ShotsFired { get; private set; }

        /// <summary>撃った時（命中の有無にかかわらず）。</summary>
        public event Action<WeaponDefinition> Fired;
        /// <summary>何かに当たった時（当たった相手が IDamageable なら damageable が入る）。</summary>
        public event Action<RaycastHit, IDamageable> Hit;

        AmmoState Current
        {
            get
            {
                var def = holder ? holder.CurrentWeapon : null;
                if (!def) return null;
                if (!ammo.TryGetValue(def, out var s))
                    ammo[def] = s = new AmmoState { mag = def.magazineSize, reserve = def.reserveAmmo };
                return s;
            }
        }

        void Awake()
        {
            if (!holder) holder = GetComponent<WeaponHolder>();
            if (!motor) motor = GetComponent<CharacterMotor>();
            animator = GetComponentInChildren<Animator>();
            ownColliders = GetComponentsInChildren<Collider>();
            holder.WeaponChanged += _ => CancelReload();
        }

        /// <summary>引き金を引いている／いない。毎フレーム呼ぶ。</summary>
        public void SetTrigger(bool held) => triggerHeld = held;

        public void Reload()
        {
            var def = holder.CurrentWeapon;
            var s = Current;
            if (def == null || s == null || IsReloading || s.mag >= def.magazineSize || s.reserve == 0) return;
            reloadEnd = Time.time + def.reloadTime;
            holder.SupportHandWeight = 0f; // 左手を離してマガジンを替える
            if (animator) animator.SetTrigger(ReloadId);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Reload, 0.6f);
        }

        /// <summary>全武器の弾を満タンにする（ウェーブ報酬・補給所など）。</summary>
        public void RefillAll()
        {
            foreach (var kv in ammo)
            {
                kv.Value.mag = kv.Key.magazineSize;
                kv.Value.reserve = kv.Key.reserveAmmo;
            }
        }

        void CancelReload()
        {
            reloadEnd = -1f;
            holder.SupportHandWeight = 1f;
        }

        void Update()
        {
            var def = holder.CurrentWeapon;
            if (IsReloading && Time.time >= reloadEnd) FinishReload(def);

            bool pressedNow = triggerHeld && !triggerWasHeld;
            triggerWasHeld = triggerHeld;
            if (!def || !holder.IsAiming || IsReloading) return;

            bool wantFire = def.automatic ? triggerHeld : pressedNow;
            if (!wantFire || Time.time < nextFireTime) return;

            var s = Current;
            if (s.mag <= 0) { Reload(); return; }
            s.mag--;
            nextFireTime = Time.time + 1f / Mathf.Max(0.1f, def.fireRate);
            FireOne(def);
            if (s.mag <= 0) Reload();
        }

        void FinishReload(WeaponDefinition def)
        {
            reloadEnd = -1f;
            holder.SupportHandWeight = 1f;
            var s = Current;
            if (s == null || def == null) return;
            int need = def.magazineSize - s.mag;
            int take = s.reserve < 0 ? need : Mathf.Min(need, s.reserve);
            s.mag += take;
            if (s.reserve >= 0) s.reserve -= take;
        }

        void FireOne(WeaponDefinition def)
        {
            var weapon = holder.WeaponInstance;
            Transform muzzle = weapon ? weapon.Muzzle : transform;
            Vector3 origin = muzzle.position;

            // ばらつき：止まっている時は小さく、動くと大きく
            float moveT = motor && motor.Profile ? Mathf.Clamp01(motor.Speed / motor.Profile.jogSpeed) : 0f;
            float spread = def.spread + def.moveSpread * moveT;
            Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), Vector3.up) * holder.AimDirection;

            Vector3 end = origin + dir * def.range;
            if (RaycastIgnoringSelf(origin, dir, def.range, out var hit))
            {
                end = hit.point;
                var target = hit.collider.GetComponentInParent<IDamageable>();
                if (target != null && !target.IsDead && !Health.AreAllies(gameObject, hit.collider.gameObject))
                {
                    target.TakeDamage(new DamageInfo
                    {
                        amount = def.damage, point = hit.point, direction = dir,
                        knockback = def.knockback, source = gameObject,
                    });
                }
                CombatFx.Instance?.Impact(hit.point, hit.normal, target != null);
                Hit?.Invoke(hit, target);
            }

            ShotsFired++;
            holder.AddRecoil(def.recoilKick, def.recoilPitch);
            CameraShake.Shake(def.recoilShake);
            var fx = CombatFx.Instance;
            if (fx)
            {
                fx.MuzzleFlash(muzzle, def.stance == WeaponStance.Rifle ? 1.4f : 1f);
                fx.Tracer(origin, end);
                fx.PlaySound(ProceduralSfx.Shot(def.sound));
            }
            Fired?.Invoke(def);
        }

        readonly RaycastHit[] hits = new RaycastHit[16];

        bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, float range, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, range, hitMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (Array.IndexOf(ownColliders, hits[i].collider) >= 0) continue;
                if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = hits[i]; }
            }
            return bestDist < float.MaxValue;
        }
    }
}
