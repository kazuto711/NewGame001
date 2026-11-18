using System.Collections.Generic;
using Matoi.Combat;
using Matoi.Missions;
using UnityEngine;

namespace Matoi.AI
{
    /// <summary>ボス戦の場所。目的が「ボスを倒す」の時に入ると戦闘が始まり、決着まで結界で出られなくなる。</summary>
    public class BossArena : MonoBehaviour
    {
        public static BossArena Instance { get; private set; }

        [SerializeField] BossBrain boss;
        [SerializeField] GameObject walls;
        [Tooltip("結界の広さの半分（x, z）")]
        [SerializeField] Vector2 halfSize = new Vector2(11f, 8f);

        readonly List<EnemyBrain> hiddenEnemies = new List<EnemyBrain>();
        Transform player;
        Health playerHealth;
        CharacterController playerController;

        public BossBrain Boss => boss;
        public bool IsFighting { get; private set; }

        public void Configure(BossBrain arenaBoss, GameObject barrier, Vector2 size)
        {
            boss = arenaBoss;
            walls = barrier;
            halfSize = size;
        }

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (!p) return;
            player = p.transform;
            playerHealth = p.GetComponent<Health>();
            playerController = p.GetComponent<CharacterController>();
        }

        /// <summary>その位置が結界の内側か。margin の分だけ内側で判定する。</summary>
        public bool Contains(Vector3 position, float margin = 0f)
        {
            Vector3 d = position - transform.position;
            return Mathf.Abs(d.x) <= halfSize.x - margin && Mathf.Abs(d.z) <= halfSize.y - margin;
        }

        void Update()
        {
            if (!player) return;
            if (!IsFighting)
            {
                var tracker = MissionTracker.Instance;
                if (tracker && tracker.IsWaitingFor(MissionSignals.BossDefeated) && !playerHealth.IsDead && Contains(player.position, 1f)) BeginFight();
                return;
            }

            if (boss.IsDefeated) { EndFight(); MissionSignals.Raise(MissionSignals.BossDefeated); return; }
            if (playerHealth.IsDead)
            {
                // 負けた時は元に戻す。持ち物は失わず、何度でも挑戦できる
                boss.Sleep();
                boss.gameObject.SetActive(false);
                EndFight();
                return;
            }
            KeepPlayerInside();
        }

        void BeginFight()
        {
            IsFighting = true;
            hiddenEnemies.Clear();
            foreach (var enemy in EnemyBrain.All)
                if (Contains(enemy.transform.position)) hiddenEnemies.Add(enemy);
            foreach (var enemy in hiddenEnemies) enemy.gameObject.SetActive(false);
            if (walls) walls.SetActive(true);
            boss.gameObject.SetActive(true);
            boss.Awaken(this, player);
        }

        void EndFight()
        {
            IsFighting = false;
            if (walls) walls.SetActive(false);
            foreach (var enemy in hiddenEnemies)
                if (enemy) enemy.gameObject.SetActive(true);
            hiddenEnemies.Clear();
        }

        void KeepPlayerInside()
        {
            const float margin = 0.6f;
            Vector3 d = player.position - transform.position;
            float x = Mathf.Clamp(d.x, -halfSize.x + margin, halfSize.x - margin);
            float z = Mathf.Clamp(d.z, -halfSize.y + margin, halfSize.y - margin);
            Vector3 push = new Vector3(x - d.x, 0f, z - d.z);
            if (push.sqrMagnitude > 1e-6f && playerController && playerController.enabled) playerController.Move(push);
        }
    }
}
