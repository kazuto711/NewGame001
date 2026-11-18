using System.Collections.Generic;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Matoi.AI
{
    /// <summary>
    /// ボス「社の主」。体力が減るごとに行動が増える。
    ///   第 1 段階：叩きつけ、突進
    ///   第 2 段階：3 方向に広がる弾を追加
    ///   第 3 段階：弾が 5 方向になり、手下を呼び、動きが速くなる
    /// どの攻撃も、当たる前に地面の印や動きで予告する。
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(Health))]
    public class BossBrain : MonoBehaviour
    {
        public enum State { Dormant, Intro, Chase, SlamWindup, ChargeWindup, Charging, VolleyWindup, Recover, Dead }

        [Header("体力")]
        [SerializeField] float maxHealth = 1100f;
        [Tooltip("銃のダメージに掛ける倍率")]
        [SerializeField, Range(0.1f, 1f)] float gunDamageScale = 0.8f;

        [Header("叩きつけ")]
        [SerializeField] float slamDamage = 28f;
        [SerializeField] float slamRadius = 4f;
        [SerializeField] float slamWindup = 1.05f;
        [Tooltip("この距離より近いと叩きつけを選ぶ (m)")]
        [SerializeField] float slamRange = 5.5f;

        [Header("突進")]
        [SerializeField] float chargeDamage = 24f;
        [SerializeField] float chargeWindup = 0.85f;
        [SerializeField] float chargeTime = 1.1f;
        [SerializeField] float chargeLength = 10f;
        [SerializeField] float chargeWidth = 2.2f;

        [Header("弾")]
        [SerializeField] float volleyWindup = 0.85f;
        [SerializeField] float volleySpread = 18f;
        [SerializeField] EnemyDefinition shotDefinition;

        [Header("手下")]
        [SerializeField] EnemyBrain minionPrefab;
        [SerializeField] float summonInterval = 18f;
        [SerializeField] int maxMinions = 2;

        [Header("間合い")]
        [SerializeField] float recoverTime = 0.8f;
        [SerializeField] float actionInterval = 1.6f;
        [SerializeField] Material telegraphMaterial;

        readonly List<EnemyBrain> minions = new List<EnemyBrain>();
        CharacterMotor motor;
        Health health;
        CharacterAnimator characterAnimator;
        BossArena arena;
        Transform target;
        Health targetHealth;
        EnemyProjectile chargingShot;
        Vector3 home, actionCenter, chargeDirection;
        float homeYaw, stateStart, nextAction, nextSummon;
        bool homeKnown, chargeHit;

        public State Current { get; private set; } = State.Dormant;
        public Health Health => health;
        public bool IsDefeated => Current == State.Dead;
        public int MinionCount { get { minions.RemoveAll(m => !m || m.Health.IsDead); return minions.Count; } }
        /// <summary>今の段階（1〜3）。</summary>
        public int Phase
        {
            get
            {
                float f = health.Current / Mathf.Max(1f, health.Max);
                return f > 2f / 3f ? 1 : f > 1f / 3f ? 2 : 3;
            }
        }

        float Tempo => Phase == 3 ? 0.75f : 1f;
        float StateTime => Time.time - stateStart;

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            health = GetComponent<Health>();
            characterAnimator = GetComponent<CharacterAnimator>();
            health.Configure(maxHealth, Faction.Hostile);
            health.DamageFilter = info =>
            {
                if (Current == State.Dormant || Current == State.Intro) info.amount = 0f;   // 登場の演出中は攻撃が効かない
                else if (!info.isMelee) info.amount *= gunDamageScale;
                return info;
            };
            health.OnDied += _ => Die();
        }

        void Enter(State state) { Current = state; stateStart = Time.time; }

        /// <summary>戦闘を始める。</summary>
        public void Awaken(BossArena owner, Transform player)
        {
            arena = owner;
            target = player;
            targetHealth = player.GetComponent<Health>();
            if (!homeKnown)
            {
                homeKnown = true;
                home = transform.position;
                homeYaw = transform.eulerAngles.y;
            }
            nextAction = Time.time + 2.4f;
            nextSummon = 0f;
            Enter(State.Intro);
            CameraShake.Shake(0.8f);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Stomp, 1f);
        }

        /// <summary>戦闘をやめて元の状態へ戻す（プレイヤーが倒れた時）。</summary>
        public void Sleep()
        {
            ClearMinions();
            CancelShot();
            health.ResetHealth();
            if (characterAnimator) characterAnimator.SetDead(false);
            if (homeKnown) motor.Teleport(home, homeYaw);
            motor.SetIntent(MoveIntent.None);
            Enter(State.Dormant);
        }

        void Die()
        {
            CancelShot();
            ClearMinions();
            motor.SetIntent(MoveIntent.None);
            if (characterAnimator) characterAnimator.SetDead(true);
            Enter(State.Dead);
            CameraShake.Shake(1f);
            HitStop.Do(0.25f, 0.1f);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Stomp, 1f);
        }

        void ClearMinions()
        {
            foreach (var minion in minions)
                if (minion) Destroy(minion.gameObject);
            minions.Clear();
        }

        void CancelShot()
        {
            if (chargingShot) Destroy(chargingShot.gameObject);
            chargingShot = null;
        }

        void OnDisable() => CancelShot();

        void Update()
        {
            if (Current == State.Dormant || Current == State.Dead) return;
            if (!target || !targetHealth || targetHealth.IsDead) { motor.SetIntent(MoveIntent.None); return; }

            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 direction = distance > 0.01f ? to / distance : transform.forward;

            switch (Current)
            {
                case State.Intro:
                    motor.SetIntent(new MoveIntent { facing = direction });
                    if (StateTime >= 1.6f) Enter(State.Chase);
                    break;

                case State.Chase:
                    UpdateChase(distance, direction);
                    break;

                case State.SlamWindup:
                    motor.SetIntent(MoveIntent.None);
                    if (StateTime >= slamWindup * Tempo) Slam();
                    break;

                case State.ChargeWindup:
                    motor.SetIntent(new MoveIntent { facing = chargeDirection });
                    if (StateTime >= chargeWindup * Tempo) { chargeHit = false; Enter(State.Charging); }
                    break;

                case State.Charging:
                    UpdateCharge(distance);
                    break;

                case State.VolleyWindup:
                    motor.SetIntent(new MoveIntent { facing = direction });
                    if (chargingShot) chargingShot.HoldAt(Mouth);
                    if (StateTime >= volleyWindup * Tempo) FireVolley();
                    break;

                case State.Recover:
                    motor.SetIntent(new MoveIntent { facing = direction });
                    if (StateTime >= recoverTime * Tempo)
                    {
                        nextAction = Time.time + actionInterval * Tempo;
                        Enter(State.Chase);
                    }
                    break;
            }
        }

        Vector3 Mouth => transform.position + Vector3.up * 2.5f + transform.forward * 1f;

        void UpdateChase(float distance, Vector3 direction)
        {
            if (Phase == 3 && Time.time >= nextSummon && MinionCount < maxMinions) Summon();
            if (Time.time >= nextAction) { ChooseAction(distance, direction); return; }
            motor.SetIntent(distance > 3f ? new MoveIntent { direction = direction, facing = direction } : new MoveIntent { facing = direction });
        }

        void ChooseAction(float distance, Vector3 direction)
        {
            if (distance <= slamRange) BeginSlam(direction);
            else if (Phase >= 2 && Random.value < 0.55f) BeginVolley(direction);
            else BeginCharge(direction);
        }

        // ---------- 叩きつけ ----------
        void BeginSlam(Vector3 direction)
        {
            motor.FaceImmediately(direction);
            actionCenter = transform.position + direction * 1.6f;
            float windup = slamWindup * Tempo;
            GroundTelegraph.Circle(telegraphMaterial, actionCenter, slamRadius, windup);
            if (characterAnimator) characterAnimator.PlayAction("EnemyAttack", windup + 0.4f);
            Enter(State.SlamWindup);
        }

        void Slam()
        {
            CameraShake.Shake(0.7f);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Stomp, 1f);
            CombatFx.Instance?.Impact(actionCenter, Vector3.up, false);
            Vector3 d = target.position - actionCenter;
            d.y = 0f;
            if (d.magnitude <= slamRadius) HitTarget(slamDamage, d.normalized, 9f);
            Enter(State.Recover);
        }

        // ---------- 突進 ----------
        void BeginCharge(Vector3 direction)
        {
            chargeDirection = direction;
            motor.FaceImmediately(direction);
            GroundTelegraph.Line(telegraphMaterial, transform.position, direction, chargeLength, chargeWidth, chargeWindup * Tempo);
            CombatFx.Instance?.PlaySound(ProceduralSfx.Whoosh, 0.8f);
            Enter(State.ChargeWindup);
        }

        void UpdateCharge(float distance)
        {
            motor.SetIntent(new MoveIntent { direction = chargeDirection, facing = chargeDirection, sprint = true });
            if (!chargeHit && distance <= 2f)
            {
                chargeHit = true;
                HitTarget(chargeDamage, chargeDirection, 11f);
            }
            bool leaving = arena && !arena.Contains(transform.position + chargeDirection * 1.5f, 0.5f);
            if (StateTime >= chargeTime || leaving)
            {
                motor.SetIntent(MoveIntent.None);
                Enter(State.Recover);
            }
        }

        // ---------- 弾 ----------
        void BeginVolley(Vector3 direction)
        {
            motor.FaceImmediately(direction);
            if (shotDefinition) chargingShot = EnemyProjectile.BeginCharge(gameObject, shotDefinition, Mouth);
            if (characterAnimator) characterAnimator.PlayAction("EnemyAttack", volleyWindup * Tempo + 0.35f);
            Enter(State.VolleyWindup);
        }

        void FireVolley()
        {
            if (shotDefinition)
            {
                Vector3 origin = Mouth;
                Vector3 aim = target.position + Vector3.up * 1.1f - origin;
                int side = Phase >= 3 ? 2 : 1;
                for (int i = -side; i <= side; i++)
                {
                    Vector3 shotTarget = origin + Quaternion.AngleAxis(i * volleySpread, Vector3.up) * aim;
                    if (i == 0 && chargingShot) { chargingShot.Launch(shotTarget); chargingShot = null; }
                    else EnemyProjectile.BeginCharge(gameObject, shotDefinition, origin).Launch(shotTarget);
                }
            }
            Enter(State.Recover);
        }

        // ---------- 手下 ----------
        void Summon()
        {
            nextSummon = Time.time + summonInterval;
            if (!minionPrefab) return;
            for (int i = minions.Count; i < maxMinions; i++)
            {
                Vector3 p = transform.position + Quaternion.Euler(0f, i * 150f + 60f, 0f) * Vector3.forward * 5f;
                if (!NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) continue;
                var minion = Instantiate(minionPrefab, hit.position + Vector3.up * 0.05f, Quaternion.identity);
                minion.name = "Boss_Minion";
                minion.RespawnEnabled = false;
                minion.ForceAggro();
                minions.Add(minion);
            }
        }

        void HitTarget(float damage, Vector3 direction, float knockback)
        {
            targetHealth.TakeDamage(new DamageInfo
            {
                amount = damage, point = target.position + Vector3.up, direction = direction,
                knockback = knockback, source = gameObject, heavy = true,
            });
        }
    }
}
