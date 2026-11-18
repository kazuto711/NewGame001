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
        public enum State { Idle, Chase, Windup, Attack, Stagger, Return, Dead, Search, RangedWindup }

        [SerializeField] EnemyDefinition definition;

        static readonly List<EnemyBrain> all = new List<EnemyBrain>();
        public static IReadOnlyList<EnemyBrain> All => all;
        /// <summary>どれかの敵が倒された時（ミッションの進行に使う）。</summary>
        public static event System.Action<EnemyBrain> AnyDefeated;

        CharacterMotor motor;
        MeleeAttack melee;
        HitReaction reaction;
        CharacterAnimator characterAnimator;
        Health health;
        Transform target;
        Health targetHealth;

        Vector3 home;
        float homeYaw, stateStart, nextAttack, nextRepath, deathTime;
        bool aggro;
        NavMeshPath path;
        readonly List<Vector3> corners = new List<Vector3>();
        int cornerIndex;
        EnemyProjectile chargingShot;
        float nextRangedAttack;

        /// <summary>弾を放った回数（テスト・調整用）</summary>
        public int ShotsFired { get; private set; }

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

        // ---- ステルス ----
        const float SuspiciousAt = 0.35f;
        Vector3 searchPoint;
        float lostSightTime, searchWaitStart = -10f;

        /// <summary>しゃがんだプレイヤーへの警戒度（0〜1）。1 で発見。</summary>
        public float Awareness { get; private set; }
        /// <summary>プレイヤーを見つけて追っているか。</summary>
        public bool IsHuntingPlayer => aggro && Current != State.Dead && Current != State.Return && Current != State.Idle && Current != State.Search;
        /// <summary>怪しんでいるか（調べに来ている、または見られかけている）。</summary>
        public bool IsSuspicious => Current == State.Search || Awareness >= SuspiciousAt;
        /// <summary>プレイヤーに気づいていないか（隠密攻撃が決まる条件）。</summary>
        public bool IsUnawareOfPlayer => !aggro && Current != State.Dead;

        /// <summary>音を聞く。届く距離なら、音のした場所を調べに行く。</summary>
        public void HearNoise(Vector3 position, float radius)
        {
            if (aggro || Current == State.Dead || radius <= 0f || BlockedByBase(position)) return;
            if (Flat(position - transform.position).magnitude > radius) return;
            searchPoint = position;
            Awareness = Mathf.Max(Awareness, SuspiciousAt);
            Enter(State.Search);
        }

        /// <summary>拠点の中には踏み込まない（戦闘テストのウェーブで出した敵だけは例外）。</summary>
        bool BlockedByBase(Vector3 position) => RespawnEnabled && Matoi.Crafting.BaseZone.Contains(position);

        /// <summary>倒された時に物を落とす。</summary>
        void DropLoot()
        {
            if (!definition || !definition.drops) return;
            foreach (var stack in definition.drops.Roll())
            {
                Vector2 offset = Random.insideUnitCircle * 0.6f;
                Matoi.Items.WorldPickup.Spawn(stack.item, stack.count, transform.position + new Vector3(offset.x, 0.6f, offset.y));
            }
        }

        /// <summary>気づいていない敵への、しゃがんだプレイヤーの攻撃は倍率が掛かる。</summary>
        DamageInfo ApplySneakBonus(DamageInfo info)
        {
            var stealth = Matoi.Player.PlayerStealth.Instance;
            if (stealth && stealth.IsSneaking && info.source == stealth.gameObject && IsUnawareOfPlayer)
            {
                float multiplier = info.isMelee ? stealth.meleeMultiplier : stealth.gunMultiplier;
                info.amount *= multiplier;
                stealth.NotifySneakAttack(multiplier);
            }
            if (!info.isMelee && definition) info.amount *= definition.gunDamageScale;   // 硬い敵は銃が効きにくい
            return info;
        }

        void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            melee = GetComponent<MeleeAttack>();
            reaction = GetComponent<HitReaction>();
            characterAnimator = GetComponent<CharacterAnimator>();
            health = GetComponent<Health>();
            path = new NavMeshPath();
            ApplyDefinition();
            health.DamageFilter = ApplySneakBonus;
            health.OnDamaged += info =>
            {
                if (definition.aggroOnDamage && info.source && !Health.AreAllies(gameObject, info.source)) aggro = true;
            };
            health.OnDied += _ => { Enter(State.Dead); deathTime = Time.time; DropLoot(); AnyDefeated?.Invoke(this); };
        }

        void OnEnable() => all.Add(this);
        void OnDisable()
        {
            all.Remove(this);
            CancelCharge();
        }

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
                melee.reach = definition.attackReach;
                melee.radius = definition.attackRadius;
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
            if (chargingShot && Current != State.RangedWindup) CancelCharge();   // ひるむ・倒されると弾は消える
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
                case State.Search:
                    if (targetAlive && (aggro || Notices(dist))) { aggro = true; Awareness = 1f; Enter(State.Chase); break; }
                    // 怪しい場所まで歩いて、しばらく見回してから持ち場へ戻る
                    if (Flat(searchPoint - transform.position).magnitude > 1.2f && StateTime < definition.searchTime + 6f)
                        MoveTowards(searchPoint, false);
                    else
                    {
                        motor.SetIntent(new MoveIntent { facing = Quaternion.Euler(0f, Mathf.Sin(StateTime * 1.5f) * 70f, 0f) * transform.forward });
                        if (searchWaitStart < stateStart) searchWaitStart = Time.time;
                        if (Time.time - searchWaitStart > definition.searchTime) Enter(State.Return);
                    }
                    break;

                case State.Idle:
                case State.Return:
                    if (targetAlive && (aggro || Notices(dist))) { aggro = true; Awareness = 1f; Enter(State.Chase); break; }
                    if (Current == State.Idle && Awareness >= SuspiciousAt && targetAlive) { searchPoint = target.position; Enter(State.Search); break; }
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
                    if (!targetAlive || dist > definition.loseRange || BlockedByBase(target.position) || BlockedByBase(transform.position))
                    { aggro = false; Awareness = 0f; Enter(State.Return); break; }
                    // しゃがんで物陰に隠れ続けると、見失ってあきらめる
                    var sneaker = Matoi.Player.PlayerStealth.Instance;
                    if (sneaker && sneaker.IsSneaking && !CanSeeTarget() && dist > definition.hearRange)
                    {
                        lostSightTime += Time.deltaTime;
                        if (lostSightTime >= definition.giveUpTime)
                        {
                            aggro = false;
                            lostSightTime = 0f;
                            Awareness = SuspiciousAt;
                            searchPoint = target.position;
                            Enter(State.Search);
                            break;
                        }
                    }
                    else lostSightTime = 0f;
                    if (dist <= definition.attackRange && Time.time >= nextAttack)
                    {
                        Enter(State.Windup);
                        WindupCount++;
                        // 腕を振りかぶる動きを、ための時間から攻撃の終わりまで通して再生する
                        if (characterAnimator && melee)
                            characterAnimator.PlayAction("EnemyAttack", definition.windupTime + melee.duration);
                        motor.AddImpulse(-dirToTarget * 1.2f); // 少し身を引く＝攻撃の合図
                        break;
                    }
                    if (definition.ranged && dist > definition.attackRange) { UpdateRanged(dist, dirToTarget); break; }
                    MoveTowards(target.position, dist > definition.sprintDistance);
                    break;

                case State.RangedWindup:
                    motor.SetIntent(new MoveIntent { facing = dirToTarget });
                    if (!targetAlive) { CancelCharge(); Enter(State.Chase); break; }
                    if (chargingShot) chargingShot.HoldAt(MouthPosition);
                    if (StateTime >= definition.rangedWindup) ReleaseShot();
                    break;

                case State.Windup:
                    // その場で相手の方を向いて「ため」る
                    motor.SetIntent(new MoveIntent { facing = dirToTarget });
                    if (StateTime >= definition.windupTime)
                    {
                        if (melee) melee.TryAttack(dirToTarget, playAnimation: false);
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

        // ---------- 遠距離攻撃 ----------
        Vector3 MouthPosition => transform.position + Vector3.up * definition.projectileHeight + transform.forward * 0.45f;

        /// <summary>距離を保ちながら、見えていれば弾を吐く。</summary>
        void UpdateRanged(float dist, Vector3 dirToTarget)
        {
            bool sees = CanSeeTarget();
            if (sees && dist <= definition.rangedRange && Time.time >= nextRangedAttack)
            {
                Enter(State.RangedWindup);
                WindupCount++;
                chargingShot = EnemyProjectile.BeginCharge(gameObject, definition, MouthPosition);
                if (characterAnimator) characterAnimator.PlayAction("EnemyAttack", definition.rangedWindup + 0.35f);
                motor.SetIntent(new MoveIntent { facing = dirToTarget });
            }
            else if (dist < definition.rangedMinDistance)
                motor.SetIntent(new MoveIntent { direction = -dirToTarget, facing = dirToTarget });
            else if (!sees || dist > definition.rangedPreferredDistance)
                MoveTowards(target.position, false);
            else
                motor.SetIntent(new MoveIntent { facing = dirToTarget });
        }

        void ReleaseShot()
        {
            if (chargingShot)
            {
                // 動いている相手の少し先を狙う（向きを変えれば避けられる）
                Vector3 aim = target.position + Vector3.up * 1.1f;
                if (target.TryGetComponent<CharacterMotor>(out var targetMotor))
                {
                    float flight = Vector3.Distance(MouthPosition, aim) / Mathf.Max(1f, definition.projectileSpeed);
                    aim += Flat(targetMotor.Velocity) * (flight * 0.5f);
                }
                chargingShot.Launch(aim);
                chargingShot = null;
                ShotsFired++;
            }
            nextRangedAttack = Time.time + definition.rangedCooldown;
            Enter(State.Chase);
        }

        void CancelCharge()
        {
            if (chargingShot) Destroy(chargingShot.gameObject);
            chargingShot = null;
        }

        bool Notices(float dist)
        {
            if (BlockedByBase(target.position) || BlockedByBase(transform.position)) { Awareness = 0f; return false; }
            var stealth = Matoi.Player.PlayerStealth.Instance;
            if (stealth && stealth.IsSneaking) return NoticesSneakingPlayer(stealth, dist);
            Awareness = Mathf.MoveTowards(Awareness, 0f, Time.deltaTime / definition.awarenessDecayTime);
            if (dist <= definition.hearRange) return true;
            if (dist > definition.detectRange) return false;
            return CanSeeTarget();
        }

        /// <summary>しゃがんでいるプレイヤーは、見られ続ける・足音を聞かれると、少しずつ気づかれる。</summary>
        bool NoticesSneakingPlayer(Matoi.Player.PlayerStealth stealth, float dist)
        {
            float sightRange = definition.detectRange * definition.sneakSightScale;
            Vector3 toPlayer = Flat(target.position - transform.position);
            bool inView = dist <= sightRange && Vector3.Angle(transform.forward, toPlayer) <= definition.sneakFieldOfView * 0.5f;
            bool seen = inView && CanSeeTarget();
            bool heard = dist <= stealth.FootstepNoise;

            if (seen || heard)
            {
                // 近いほど早く気づく。止まっていると気づかれにくい
                float nearness = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(1.5f, sightRange, dist));
                float stillness = motor && target.TryGetComponent<CharacterMotor>(out var playerMotor) && playerMotor.Speed < 0.1f ? 0.45f : 1f;
                float rate = seen ? nearness * stillness : 0.35f;
                Awareness = Mathf.Min(1f, Awareness + rate / definition.sneakDetectTime * Time.deltaTime);
            }
            else Awareness = Mathf.MoveTowards(Awareness, 0f, Time.deltaTime / definition.awarenessDecayTime);
            return Awareness >= 1f;
        }

        bool CanSeeTarget()
        {
            Vector3 eye = transform.position + Vector3.up * 1.6f;
            var stealth = Matoi.Player.PlayerStealth.Instance;
            // しゃがんでいると胸の位置が低くなり、低い塀にも隠れられる
            Vector3 chest = target.position + Vector3.up * (stealth && stealth.IsSneaking ? 0.7f : 1.2f);
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
            Awareness = 0f;
            Enter(State.Idle);
        }

        /// <summary>テスト・デバッグ用：別の場所へ移し、そこを持ち場にする。</summary>
        public void PlaceAt(Vector3 position, float yaw)
        {
            motor.Teleport(position, yaw);
            home = position;
            homeYaw = yaw;
            aggro = false;
            Awareness = 0f;
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
