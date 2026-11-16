using System.Collections.Generic;
using Matoi.Characters;
using Matoi.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Matoi.AI
{
    /// <summary>
    /// 敵の「頭脳」（シンプルな状態遷移）。
    ///   待機 → 気づく → 追跡 → ため（合図）→ 攻撃 → 追跡 …
    ///   被弾したらひるむ、遠く離れたら持ち場へ戻る、倒されたら（テスト用に）復活する。
    /// 体を動かすのはプレイヤーと同じ CharacterMotor / MeleeAttack。頭脳だけが違う。
    /// </summary>
    [DefaultExecutionOrder(-50)] // Motor より先に意図を渡す
    [RequireComponent(typeof(CharacterMotor), typeof(Health))]
    public class EnemyBrain : MonoBehaviour
    {
        public enum State { Idle, Chase, Windup, Attack, Stagger, Return, Dead }

        [SerializeField] EnemyDefinition definition;

        static readonly List<EnemyBrain> all = new List<EnemyBrain>();
        public static IReadOnlyList<EnemyBrain> All => all;

        CharacterMotor motor;
        MeleeAttack melee;
        HitReaction reaction;
        Health health;
        Transform target;
        Health targetHealth;

        Vector3 home;
        float homeYaw, stateStart, nextAttack, nextRepath, deathTime;
        bool aggro;
        NavMeshPath path;
        readonly List<Vector3> corners = new List<Vector3>();
        int cornerIndex;

        /// <summary>倒された後に持ち場で復活するか（ウェーブで出した敵は復活しない）</summary>
        public bool RespawnEnabled { get; set; } = true;
        public float DeathTime => deathTime;

        /// <summary>見えていなくても、すぐにプレイヤーを追いかけ始める。</summary>
        public void ForceAggro() { aggro = true; if (Current == State.Idle || Current == State.Return) Enter(State.Chase); }

        public State Current { get; private set; } = State.Idle;
        public EnemyDefinition Definition => definition;
        public Health Health => health;
        /// <summary>攻撃の前の「ため」を行った回数（テスト・調整用）</summary>
        public int WindupCount { get; private set; }

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            melee = GetComponent<MeleeAttack>();
            reaction = GetComponent<HitReaction>();
            health = GetComponent<Health>();
            path = new NavMeshPath();
            ApplyDefinition();
            health.OnDamaged += info =>
            {
                if (definition.aggroOnDamage && info.source && !Health.AreAllies(gameObject, info.source)) aggro = true;
            };
            health.OnDied += _ => { Enter(State.Dead); deathTime = Time.time; };
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Start()
        {
            home = transform.position;
            homeYaw = transform.eulerAngles.y;
            var p = GameObject.FindWithTag("Player");
            if (p) { target = p.transform; targetHealth = p.GetComponent<Health>(); }
        }

        void ApplyDefinition()
        {
            if (!definition) return;
            health.Configure(definition.maxHealth, Faction.Hostile);
            if (definition.movement) motor.SetProfile(definition.movement);
            if (melee)
            {
                melee.damage = definition.attackDamage;
                melee.knockback = definition.attackKnockback;
                melee.lunge = definition.attackLunge;
                melee.hitStop = 0.04f;
                melee.shake = 0.25f;
            }
            if (reaction)
            {
                reaction.staggerTime = definition.staggerTime;
                reaction.knockbackScale = definition.knockbackScale;
                reaction.staggerCooldown = definition.staggerCooldown;
            }
        }

        void Enter(State s) { Current = s; stateStart = Time.time; }
        float StateTime => Time.time - stateStart;

        void Update()
        {
            if (Current == State.Dead)
            {
                motor.SetIntent(MoveIntent.None);
                if (RespawnEnabled && definition.respawnDelay > 0f && Time.time > deathTime + definition.respawnDelay) Respawn();
                return;
            }
            if (reaction && reaction.IsStaggered)
            {
                if (Current != State.Stagger) Enter(State.Stagger);
                motor.SetIntent(MoveIntent.None);
                return;
            }

            bool targetAlive = target && targetHealth && !targetHealth.IsDead;
            Vector3 toTarget = targetAlive ? Flat(target.position - transform.position) : Vector3.zero;
            float dist = toTarget.magnitude;
            Vector3 dirToTarget = dist > 0.01f ? toTarget / dist : transform.forward;

            switch (Current)
            {
                case State.Idle:
                case State.Return:
                    if (targetAlive && (aggro || Notices(dist))) { aggro = true; Enter(State.Chase); break; }
                    if (Current == State.Return)
                    {
                        Vector3 toHome = Flat(home - transform.position);
                        if (toHome.magnitude < 0.6f) { Enter(State.Idle); motor.SetIntent(MoveIntent.None); }
                        else MoveTowards(home, false);
                    }
                    else motor.SetIntent(MoveIntent.None);
                    break;

                case State.Stagger:
                    Enter(State.Chase);
                    aggro = true;
                    break;

                case State.Chase:
                    if (!targetAlive || dist > definition.loseRange) { aggro = false; Enter(State.Return); break; }
                    if (dist <= definition.attackRange && Time.time >= nextAttack)
                    {
                        Enter(State.Windup);
                        WindupCount++;
                        motor.AddImpulse(-dirToTarget * 1.2f); // 少し身を引く＝攻撃の合図
                        break;
                    }
                    MoveTowards(target.position, dist > definition.sprintDistance);
                    break;

                case State.Windup:
                    // その場で相手の方を向いて「ため」る
                    motor.SetIntent(new MoveIntent { facing = dirToTarget });
                    if (StateTime >= definition.windupTime)
                    {
                        if (melee) melee.TryAttack(dirToTarget);
                        nextAttack = Time.time + definition.attackCooldown;
                        Enter(State.Attack);
                    }
                    break;

                case State.Attack:
                    motor.SetIntent(MoveIntent.None);
                    if (!melee || !melee.IsAttacking) Enter(State.Chase);
                    break;
            }
        }

        bool Notices(float dist)
        {
            if (dist <= definition.hearRange) return true;
            if (dist > definition.detectRange) return false;
            return CanSeeTarget();
        }

        bool CanSeeTarget()
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 chest = target.position + Vector3.up * 1.2f;
            if (!Physics.Linecast(eye, chest, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<Health>() == targetHealth;
        }

        /// <summary>ナビメッシュの経路に沿って目的地へ向かう。経路が無ければまっすぐ向かう。</summary>
        void MoveTowards(Vector3 destination, bool sprint)
        {
            if (Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.25f;
                corners.Clear();
                cornerIndex = 0;
                if (NavMesh.SamplePosition(transform.position, out var a, 2f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(destination, out var b, 2f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) &&
                    path.status != NavMeshPathStatus.PathInvalid)
                {
                    corners.AddRange(path.corners);
                    cornerIndex = corners.Count > 1 ? 1 : 0;
                }
            }

            Vector3 next = destination;
            while (cornerIndex < corners.Count)
            {
                next = corners[cornerIndex];
                if (Flat(next - transform.position).magnitude > 0.6f) break;
                cornerIndex++;
                next = destination;
            }

            Vector3 dir = Flat(next - transform.position).normalized;
            dir += Separation();
            motor.SetIntent(new MoveIntent { direction = Vector3.ClampMagnitude(dir, 1f), sprint = sprint });
        }

        /// <summary>他の敵と重ならないよう、近すぎる仲間から離れる向き。</summary>
        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            foreach (var e in all)
            {
                if (e == this || e.Current == State.Dead) continue;
                Vector3 d = Flat(transform.position - e.transform.position);
                float m = d.magnitude;
                if (m > 0.01f && m < 1.4f) push += d / m * (1.4f - m);
            }
            return push;
        }

        void Respawn()
        {
            if (reaction) reaction.Revive(); else health.ResetHealth();
            motor.Teleport(home, homeYaw);
            aggro = false;
            Enter(State.Idle);
        }

        /// <summary>テスト・デバッグ用：別の場所へ移し、そこを持ち場にする。</summary>
        public void PlaceAt(Vector3 position, float yaw)
        {
            motor.Teleport(position, yaw);
            home = position;
            homeYaw = yaw;
            aggro = false;
            Enter(State.Idle);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            if (!definition) return;
            Gizmos.color = new Color(1, 0.8f, 0, 0.5f);
            Gizmos.DrawWireSphere(transform.position, definition.detectRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, definition.attackRange);
        }
    }
}
