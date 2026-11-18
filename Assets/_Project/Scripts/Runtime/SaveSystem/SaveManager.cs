using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
using Matoi.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Matoi.SaveSystem
{
    /// <summary>
    /// ゲームの記録と読み込み。寝床で休んだ時・ゲーム終了時・主人公を切り替える時に記録し、
    /// 起動時に記録があれば自動で続きから始める。
    /// </summary>
    [DefaultExecutionOrder(500)] // 他の部品の準備が終わってから読み込む
    public class SaveManager : MonoBehaviour
    {
        const string FileName = "matoi_save.json";

        /// <summary>テスト用：保存先のフォルダを差し替える（本番の記録に触らないため）。</summary>
        public static string DirectoryOverride;

        static string Folder => DirectoryOverride ?? Application.persistentDataPath;
        public static string SavePath => Path.Combine(Folder, FileName);
        static string TempPath => SavePath + ".tmp";
        static string BackupPath => SavePath + ".bak";

        /// <summary>自動実行（テストや撮影）の時は、本番の記録を読み書きしない。</summary>
        static bool FileAccessAllowed => DirectoryOverride != null || !Application.isBatchMode;

        public static bool HasSave => FileAccessAllowed && (File.Exists(SavePath) || File.Exists(BackupPath));

        Inventory inventory;
        ItemStorage storage;
        PlayerCrafting crafting;
        WeaponShooter shooter;
        Health health;
        PlayerLife life;
        CharacterMotor motor;
        ExplorationTracker tracker;
        Matoi.Missions.MissionTracker mission;

        readonly Dictionary<string, WorldPickup> placedPickups = new Dictionary<string, WorldPickup>();
        readonly Dictionary<string, int> placedCounts = new Dictionary<string, int>();
        readonly HashSet<string> takenPickups = new HashSet<string>();
        bool resetting;

        GameCatalog Catalog => crafting ? crafting.Catalog : null;

        void Awake()
        {
            inventory = GetComponent<Inventory>();
            storage = GetComponent<ItemStorage>();
            crafting = GetComponent<PlayerCrafting>();
            shooter = GetComponent<WeaponShooter>();
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<CharacterMotor>();
            tracker = GetComponent<ExplorationTracker>();
            mission = GetComponent<Matoi.Missions.MissionTracker>();
        }

        void Start()
        {
            foreach (var pickup in FindObjectsByType<WorldPickup>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(pickup.SaveId)) continue;
                placedPickups[pickup.SaveId] = pickup;
                placedCounts[pickup.SaveId] = pickup.Count;
            }
            if (HasSave) Load();
        }

        void OnApplicationQuit()
        {
            if (!resetting) Save();
        }

        // ---------- 記録 ----------
        /// <summary>今の状態をファイルに記録する。成功すると true。</summary>
        public bool Save()
        {
            if (!FileAccessAllowed) return false;
            try
            {
                string json = JsonUtility.ToJson(Capture(), true);
                Directory.CreateDirectory(Folder);
                // 一時ファイルに全部書いてから入れ替える。書き込み中に落ちても前回の記録は壊れない
                File.WriteAllText(TempPath, json);
                if (File.Exists(SavePath)) File.Replace(TempPath, SavePath, BackupPath);
                else File.Move(TempPath, SavePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Matoi] 記録に失敗しました: " + e.Message);
                return false;
            }
        }

        SaveData Capture()
        {
            bool dead = health && health.IsDead;
            var data = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                position = dead && life ? life.RespawnPosition : transform.position,
                yaw = dead && life ? life.RespawnYaw : transform.eulerAngles.y,
                health = health ? (dead ? health.Max : health.Current) : 100f,
                respawnPosition = life ? life.RespawnPosition : transform.position,
                respawnYaw = life ? life.RespawnYaw : transform.eulerAngles.y,
                missionActive = mission && mission.IsActive,
                missionComplete = mission && mission.IsComplete,
                missionStep = mission ? mission.StepIndex : 0,
                missionProgress = mission ? mission.Progress : 0,
            };

            if (inventory) data.inventory = ToSaved(inventory.Stacks);
            if (storage) data.storage = ToSaved(storage.Stacks);

            var catalog = Catalog;
            if (catalog && catalog.weapons != null && shooter)
                foreach (var weapon in catalog.weapons)
                    if (weapon && shooter.TryGetAmmo(weapon, out int magazine, out int reserve))
                        data.ammo.Add(new SavedAmmo { weapon = weapon.name, magazine = magazine, reserve = reserve });
            if (catalog && crafting)
                foreach (var (weapon, kind, level) in crafting.AllLevels())
                {
                    var upgrade = catalog.UpgradeOf(kind);
                    if (weapon && upgrade) data.upgrades.Add(new SavedUpgrade { weapon = weapon.name, upgrade = upgrade.name, level = level });
                }

            foreach (var point in ExplorationPoint.All)
                if (point.Discovered) data.discovered.Add(point.DisplayName);
            foreach (var box in FindObjectsByType<LootContainer>(FindObjectsSortMode.None))
                if (box.Searched && !string.IsNullOrEmpty(box.SaveId)) data.searched.Add(box.SaveId);
            foreach (var node in FindObjectsByType<GatherNode>(FindObjectsSortMode.None))
                if (node.RemainingRespawn > 0f && !string.IsNullOrEmpty(node.SaveId))
                    data.gatherTimers.Add(new SavedTimer { id = node.SaveId, seconds = node.RemainingRespawn });

            data.takenPickups.AddRange(takenPickups);
            foreach (var pair in placedPickups)
            {
                if (takenPickups.Contains(pair.Key)) continue;
                if (!pair.Value) data.takenPickups.Add(pair.Key);
                else if (pair.Value.Count != placedCounts[pair.Key]) data.partialPickups.Add(new SavedCount { id = pair.Key, count = pair.Value.Count });
            }
            foreach (var pickup in FindObjectsByType<WorldPickup>(FindObjectsSortMode.None))
                if (string.IsNullOrEmpty(pickup.SaveId) && pickup.Item)
                    data.drops.Add(new SavedDrop { item = pickup.Item.name, count = pickup.Count, position = pickup.transform.position });
            return data;
        }

        static List<SavedCount> ToSaved(IEnumerable<ItemStack> stacks) =>
            stacks.Where(s => s.item).Select(s => new SavedCount { id = s.item.name, count = s.count }).ToList();

        // ---------- 読み込み ----------
        /// <summary>記録を読み込む。本体が壊れていれば、ひとつ前の記録を使う。</summary>
        public bool Load()
        {
            if (!FileAccessAllowed) return false;
            var data = Read(SavePath) ?? Read(BackupPath);
            if (data == null) return false;
            Apply(data);
            return true;
        }

        static SaveData Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data != null && data.version >= 1 && data.version <= SaveData.CurrentVersion) return data;
                Debug.LogWarning("[Matoi] 読み込めない記録です: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matoi] 記録の読み込みに失敗しました: {path} {e.Message}");
            }
            return null;
        }

        void Apply(SaveData data)
        {
            var catalog = Catalog;
            if (!catalog) return;

            inventory?.SetContents(FromSaved(data.inventory, catalog));
            storage?.SetContents(FromSaved(data.storage, catalog));

            // 強化を先に戻す（弾倉の大きさが変わるため）
            foreach (var saved in data.upgrades)
            {
                var weapon = catalog.FindWeapon(saved.weapon);
                var upgrade = catalog.FindUpgrade(saved.upgrade);
                if (weapon && upgrade) crafting.SetLevel(weapon, upgrade.kind, saved.level);
            }
            foreach (var saved in data.ammo)
            {
                var weapon = catalog.FindWeapon(saved.weapon);
                if (weapon && shooter) shooter.SetAmmo(weapon, saved.magazine, saved.reserve);
            }

            foreach (var point in ExplorationPoint.All) point.Discovered = data.discovered.Contains(point.DisplayName);
            tracker?.RecountDiscovered();

            var searched = new HashSet<string>(data.searched);
            foreach (var box in FindObjectsByType<LootContainer>(FindObjectsSortMode.None))
                box.Searched = searched.Contains(box.SaveId);
            foreach (var node in FindObjectsByType<GatherNode>(FindObjectsSortMode.None))
            {
                var timer = data.gatherTimers.Find(t => t.id == node.SaveId);
                node.RemainingRespawn = timer != null ? timer.seconds : 0f;
            }

            foreach (string id in data.takenPickups)
            {
                takenPickups.Add(id);
                if (!placedPickups.TryGetValue(id, out var pickup) || !pickup) continue;
                pickup.gameObject.SetActive(false);   // 同じフレームのうちに調べられないよう、先に止める
                Destroy(pickup.gameObject);
            }
            foreach (var saved in data.partialPickups)
                if (placedPickups.TryGetValue(saved.id, out var pickup) && pickup) pickup.RestoreCount(saved.count);
            foreach (var drop in data.drops)
            {
                var item = catalog.FindItem(drop.item);
                if (item) WorldPickup.Spawn(item, drop.count, drop.position + Vector3.up * 0.5f);
            }

            if (life) life.SetRespawnPoint(data.respawnPosition, data.respawnYaw);
            if (health) health.RestoreCurrent(data.health);
            if (motor) motor.Teleport(data.position + Vector3.up * 0.05f, data.yaw);
            mission?.Restore(data.missionActive, data.missionStep, data.missionProgress, data.missionComplete);
        }

        static IEnumerable<ItemStack> FromSaved(List<SavedCount> saved, GameCatalog catalog)
        {
            foreach (var s in saved)
            {
                var item = catalog.FindItem(s.id);
                if (item) yield return new ItemStack(item, s.count);
            }
        }

        // ---------- 最初から ----------
        /// <summary>記録を消して、最初からやり直す。</summary>
        public void ResetGame()
        {
            resetting = true;
            if (FileAccessAllowed)
            {
                TryDelete(SavePath);
                TryDelete(BackupPath);
                TryDelete(TempPath);
            }
            Time.timeScale = 1f;
            Matoi.UI.MenuState.IsOpen = false;
            Matoi.Missions.GameFlow.SkipTitleOnce = true;
            Matoi.Missions.GameFlow.StartMissionOnLoad = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[Matoi] 記録を消せませんでした: " + e.Message); }
        }
    }
}
