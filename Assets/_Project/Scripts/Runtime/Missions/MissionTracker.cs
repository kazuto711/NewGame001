using System;
using System.Collections.Generic;
using Matoi.AI;
using Matoi.Crafting;
using Matoi.SaveSystem;
using UnityEngine;
using UnityEngine.AI;

namespace Matoi.Missions
{
    /// <summary>ミッションの進行を管理する。目的を順番に確認し、無線の字幕を流し、区切りごとに記録する。</summary>
    public class MissionTracker : MonoBehaviour
    {
        public static MissionTracker Instance { get; private set; }

        [SerializeField] MissionDefinition mission;
        [Tooltip("無線の字幕 1 行を表示する秒数")]
        [SerializeField] float lineSeconds = 4.2f;

        readonly Queue<RadioLine> radioQueue = new Queue<RadioLine>();
        float lineUntil;
        PlayerCrafting crafting;
        SaveManager save;

        public MissionDefinition Mission => mission;
        public bool IsActive { get; private set; }
        public bool IsComplete { get; private set; }
        public int StepIndex { get; private set; }
        public int Progress { get; private set; }
        public RadioLine CurrentLine { get; private set; }

        public ObjectiveStep Current => IsActive && !IsComplete && mission && StepIndex < mission.steps.Length ? mission.steps[StepIndex] : null;

        /// <summary>目的が変わった時。</summary>
        public event Action<ObjectiveStep> StepChanged;
        /// <summary>すべての目的が終わった時。</summary>
        public event Action Completed;

        /// <summary>今の目的で待っている合図か。</summary>
        public bool IsWaitingFor(string signal) => Current != null && Current.kind == ObjectiveKind.Signal && Current.signal == signal;

        void Awake()
        {
            crafting = GetComponent<PlayerCrafting>();
            save = GetComponent<SaveManager>();
        }

        void OnEnable()
        {
            Instance = this;
            MissionSignals.Raised += OnSignal;
            EnemyBrain.AnyDefeated += OnEnemyDefeated;
            if (crafting) crafting.Crafted += OnCrafted;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            MissionSignals.Raised -= OnSignal;
            EnemyBrain.AnyDefeated -= OnEnemyDefeated;
            if (crafting) crafting.Crafted -= OnCrafted;
        }

        void Start()
        {
            if (GameFlow.ConsumeStartMission()) Begin();
        }

        /// <summary>ミッションを最初から始める。</summary>
        public void Begin()
        {
            if (!mission || mission.steps == null || mission.steps.Length == 0) return;
            IsActive = true;
            IsComplete = false;
            EnterStep(0, false);
        }

        /// <summary>セーブデータから進行を戻す。</summary>
        public void Restore(bool active, int step, int progress, bool complete)
        {
            if (!active || !mission || mission.steps == null) return;
            IsActive = true;
            IsComplete = complete;
            if (complete) { StepIndex = mission.steps.Length; return; }
            EnterStep(Mathf.Clamp(step, 0, mission.steps.Length - 1), true);
            Progress = progress;
        }

        void EnterStep(int index, bool restoring)
        {
            StepIndex = index;
            Progress = 0;
            var step = mission.steps[index];
            if (!restoring && step.radio != null)
                foreach (var line in step.radio) radioQueue.Enqueue(line);
            if (step.spawns != null)
                foreach (var spawn in step.spawns) SpawnEnemy(spawn);
            StepChanged?.Invoke(step);
        }

        static void SpawnEnemy(EncounterSpawn spawn)
        {
            var marker = MissionMarker.Find(spawn.marker);
            if (!marker || !spawn.prefab) return;
            Vector3 position = marker.transform.position;
            if (NavMesh.SamplePosition(position, out var hit, 4f, NavMesh.AllAreas)) position = hit.position;
            var enemy = Instantiate(spawn.prefab, position + Vector3.up * 0.05f, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            enemy.name = "Mission_" + spawn.enemyId;
            enemy.RespawnEnabled = false;
        }

        void Advance()
        {
            int next = StepIndex + 1;
            if (next >= mission.steps.Length)
            {
                IsComplete = true;
                StepIndex = next;
                if (mission.endingRadio != null)
                    foreach (var line in mission.endingRadio) radioQueue.Enqueue(line);
                save?.Save();
                Completed?.Invoke();
                return;
            }
            EnterStep(next, false);
            save?.Save();   // 目的が進むたびに自動で記録する
        }

        void Update()
        {
            UpdateRadio();
            var step = Current;
            if (step == null) return;
            if (step.kind == ObjectiveKind.HaveItem && crafting && crafting.Have(step.item) >= step.count) Advance();
            else if (step.kind == ObjectiveKind.ReachMarker && IsNearMarker(step)) Advance();
        }

        void UpdateRadio()
        {
            if (CurrentLine != null && Time.time < lineUntil) return;
            CurrentLine = radioQueue.Count > 0 ? radioQueue.Dequeue() : null;
            lineUntil = Time.time + lineSeconds;
        }

        bool IsNearMarker(ObjectiveStep step)
        {
            foreach (var marker in MissionMarker.All)
            {
                if (marker.Group != step.markerGroup) continue;
                Vector3 d = marker.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude <= step.radius * step.radius) return true;
            }
            return false;
        }

        void OnSignal(string signal)
        {
            if (IsWaitingFor(signal)) Advance();
        }

        void OnCrafted(RecipeDefinition recipe)
        {
            var step = Current;
            if (step != null && step.kind == ObjectiveKind.Craft && step.recipe == recipe) Advance();
        }

        void OnEnemyDefeated(EnemyBrain enemy)
        {
            var step = Current;
            if (step == null || step.kind != ObjectiveKind.DefeatEnemies) return;
            if (!string.IsNullOrEmpty(step.enemyName) && enemy.Definition.displayName != step.enemyName) return;
            Progress++;
            if (Progress >= step.count) Advance();
        }

        /// <summary>今の目的の目印を集める。</summary>
        public void CollectMarkers(List<MissionMarker> result)
        {
            result.Clear();
            var step = Current;
            if (step == null || string.IsNullOrEmpty(step.markerGroup)) return;
            foreach (var marker in MissionMarker.All)
                if (marker.Group == step.markerGroup) result.Add(marker);
        }

        /// <summary>画面に出す目的の文（無ければ空）。</summary>
        public string ObjectiveText
        {
            get
            {
                var step = Current;
                if (step == null) return "";
                if (step.kind == ObjectiveKind.HaveItem && step.count > 1)
                    return $"{step.text}（{Mathf.Min(crafting ? crafting.Have(step.item) : 0, step.count)} / {step.count}）";
                if (step.kind == ObjectiveKind.DefeatEnemies && step.count > 1) return $"{step.text}（{Progress} / {step.count}）";
                return step.text;
            }
        }
    }
}
