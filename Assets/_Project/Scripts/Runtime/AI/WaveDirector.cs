using System.Collections.Generic;
using Matoi.Combat;
using Matoi.InputHandling;
using UnityEngine;
using UnityEngine.AI;

namespace Matoi.AI
{
    /// <summary>
    /// 戦闘テスト用のウェーブ進行。Start / F2 で開始し、敵が波のように押し寄せる。
    /// 全滅させると小休止 → 体力と弾を少し回復 → 次のウェーブ（数が増える）。
    /// 本編では「拠点防衛」「巣の掃討」などのイベントに発展させられる。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        public enum Phase { Inactive, Fighting, Break }

        [SerializeField] EnemyBrain enemyPrefab;
        [Tooltip("各ウェーブの敵の数。最後以降は 2 体ずつ増える")]
        [SerializeField] int[] waveSizes = { 3, 5, 7 };
        [Tooltip("プレイヤーからこの距離の輪の上に出現（画面のすぐ外）")]
        [SerializeField] Vector2 spawnRadius = new Vector2(14f, 18f);
        [SerializeField] float breakTime = 4f;
        [SerializeField] float healOnClear = 35f;
        [SerializeField] float corpseLifetime = 5f;

        readonly List<EnemyBrain> alive = new List<EnemyBrain>();
        readonly List<EnemyBrain> dead = new List<EnemyBrain>();
        Transform player;
        Health playerHealth;
        WeaponShooter playerShooter;
        float breakEnd;
        GUIStyle style;

        public Phase CurrentPhase { get; private set; } = Phase.Inactive;
        public int Wave { get; private set; }
        public int Kills { get; private set; }
        public int AliveCount => alive.Count;
        public IReadOnlyList<EnemyBrain> AliveEnemies => alive;

        void Start()
        {
            var p = GameObject.FindWithTag("Player");
            if (!p) return;
            player = p.transform;
            playerHealth = p.GetComponent<Health>();
            playerShooter = p.GetComponent<WeaponShooter>();
            var input = p.GetComponent<PlayerInputReader>();
            if (input) input.DebugWavesPressed += () => { if (CurrentPhase == Phase.Inactive) StartWaves(); };
        }

        public void StartWaves()
        {
            Wave = 0;
            Kills = 0;
            NextWave();
        }

        public int SizeOf(int wave) =>
            wave <= waveSizes.Length ? waveSizes[wave - 1] : waveSizes[waveSizes.Length - 1] + 2 * (wave - waveSizes.Length);

        void NextWave()
        {
            Wave++;
            int n = SizeOf(Wave);
            for (int i = 0; i < n; i++) Spawn(i, n);
            CurrentPhase = Phase.Fighting;
        }

        void Spawn(int i, int n)
        {
            // プレイヤーを囲む輪の上（少しずつ角度をずらす）。歩ける場所に置けなければ近くを探す
            float angle = (i / (float)n) * 360f + Random.Range(-20f, 20f);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float r = Random.Range(spawnRadius.x, spawnRadius.y);
                Vector3 p = player.position + Quaternion.Euler(0, angle + attempt * 37f, 0) * Vector3.forward * r;
                if (!NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) continue;
                var e = Instantiate(enemyPrefab, hit.position + Vector3.up * 0.05f,
                    Quaternion.LookRotation(Flat(player.position - hit.position)));
                e.name = $"Wave{Wave}_Enemy{i}";
                e.RespawnEnabled = false;
                e.ForceAggro();
                alive.Add(e);
                return;
            }
        }

        void Update()
        {
            if (CurrentPhase == Phase.Inactive || !player) return;

            for (int i = alive.Count - 1; i >= 0; i--)
            {
                var e = alive[i];
                if (e && !e.Health.IsDead) { if (e.Current == EnemyBrain.State.Idle) e.ForceAggro(); continue; }
                alive.RemoveAt(i);
                if (e) { dead.Add(e); Kills++; }
            }
            // 倒れた敵は少し残してから消す
            for (int i = dead.Count - 1; i >= 0; i--)
            {
                if (!dead[i]) { dead.RemoveAt(i); continue; }
                if (Time.time > dead[i].DeathTime + corpseLifetime) { Destroy(dead[i].gameObject); dead.RemoveAt(i); }
            }

            if (CurrentPhase == Phase.Fighting && alive.Count == 0)
            {
                CurrentPhase = Phase.Break;
                breakEnd = Time.time + breakTime;
                if (playerHealth) playerHealth.Heal(healOnClear);
                if (playerShooter) playerShooter.RefillAll();
            }
            else if (CurrentPhase == Phase.Break && Time.time >= breakEnd) NextWave();
        }

        /// <summary>テスト用：生きている敵を全て倒す。</summary>
        public void KillAllForTest()
        {
            foreach (var e in alive.ToArray())
                if (e) e.Health.TakeDamage(new DamageInfo { amount = 9999f, direction = Vector3.forward });
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v; }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            string text = CurrentPhase switch
            {
                Phase.Inactive => "Start / F2 : 戦闘テスト開始",
                Phase.Fighting => $"WAVE {Wave}　　残り {alive.Count}　　撃破 {Kills}",
                _ => $"WAVE {Wave} クリア！　次のウェーブまで {Mathf.CeilToInt(breakEnd - Time.time)}",
            };
            var r = new Rect(0, 14, Screen.width, 40);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            GUI.color = CurrentPhase == Phase.Break ? new Color(1f, 0.85f, 0.4f) : Color.white;
            GUI.Label(r, text, style);
        }
    }
}
