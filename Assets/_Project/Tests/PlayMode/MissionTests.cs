// 第一話の確認：最初から最後までの進行、記録、ボス戦、タイトルと一時停止。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
using Matoi.Missions;
using Matoi.Player;
using Matoi.SaveSystem;
using Matoi.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class MissionTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Health health;
        Inventory inventory;
        PlayerCrafting crafting;
        PlayerInteractor interactor;
        MissionTracker tracker;
        string saveFolder, previousOverride;

        ItemDefinition Item(string id) => crafting.Catalog.FindItem("Item_" + id);
        RecipeDefinition Recipe(string id) => crafting.Catalog.recipes.First(r => r.name == "Recipe_" + id);

        [SetUp]
        public void UseTemporarySaveFolder()
        {
            pad = null;
            previousOverride = SaveManager.DirectoryOverride;
            saveFolder = Path.Combine(Application.temporaryCachePath, "MatoiMissionTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveFolder);
            SaveManager.DirectoryOverride = saveFolder;
        }

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            MenuState.IsOpen = false;
            GameFlow.StartMissionOnLoad = false;
            GameFlow.SkipTitleOnce = false;
            SaveManager.DirectoryOverride = previousOverride;
            if (Directory.Exists(saveFolder)) Directory.Delete(saveFolder, true);
        }

        IEnumerator LoadField()
        {
            pad ??= InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.TopDown);
            foreach (var enemy in EnemyBrain.All.ToArray()) enemy.gameObject.SetActive(false);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            health = player.GetComponent<Health>();
            inventory = player.GetComponent<Inventory>();
            crafting = player.GetComponent<PlayerCrafting>();
            interactor = player.GetComponent<PlayerInteractor>();
            tracker = player.GetComponent<MissionTracker>();
            yield return new WaitForSeconds(0.2f);
        }

        void Protect() => health.InvulnerableUntil = Time.time + 999f;

        IEnumerator GoTo(string markerGroup)
        {
            Vector3 p = MissionMarker.Find(markerGroup).transform.position;
            Assert.IsTrue(NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas), $"{markerGroup} へ行けない");
            motor.Teleport(hit.position + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(0.2f);
        }

        IEnumerator StartBossFight()
        {
            tracker.Restore(true, tracker.Mission.steps.ToList().FindIndex(s => s.signal == MissionSignals.BossDefeated), 0, false);
            yield return GoTo("shrine");
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(BossArena.Instance.IsFighting, "高台に入ってもボス戦が始まらない");
        }

        // ---------- データ ----------
        [UnityTest]
        public IEnumerator Mission_EveryStepHasWhatItNeeds()
        {
            yield return LoadField();
            foreach (var step in tracker.Mission.steps)
            {
                Assert.IsNotEmpty(step.text);
                Assert.IsNotNull(MissionMarker.Find(step.markerGroup), $"目印がない: {step.text}");
                if (step.kind == ObjectiveKind.HaveItem) Assert.IsNotNull(step.item, step.text);
                if (step.kind == ObjectiveKind.Craft) Assert.IsNotNull(step.recipe, step.text);
                if (step.spawns == null) continue;
                foreach (var spawn in step.spawns)
                {
                    Assert.IsNotNull(spawn.prefab, $"敵が設定されていない: {spawn.enemyId}");
                    Assert.IsNotNull(MissionMarker.Find(spawn.marker), $"出現場所がない: {spawn.marker}");
                }
            }
        }

        [UnityTest]
        public IEnumerator RadioParts_CanAllBeReachedOnFoot()
        {
            yield return LoadField();
            var parts = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).Where(p => p.Item == Item("RadioPart")).ToArray();
            Assert.AreEqual(3, parts.Length, "無線機の部品が 3 つない");
            NavMesh.SamplePosition(player.transform.position, out var start, 3f, NavMesh.AllAreas);
            foreach (var part in parts)
            {
                Assert.IsTrue(NavMesh.SamplePosition(part.transform.position, out var near, 2f, NavMesh.AllAreas), $"{part.name} のそばに立てない");
                var path = new NavMeshPath();
                NavMesh.CalculatePath(start.position, near.position, NavMesh.AllAreas, path);
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, $"{part.transform.position} まで歩いて行けない");
            }
        }

        [UnityTest]
        public IEnumerator Shrine_CanBeReachedOnFoot()
        {
            yield return LoadField();
            NavMesh.SamplePosition(player.transform.position, out var start, 3f, NavMesh.AllAreas);
            Assert.IsTrue(NavMesh.SamplePosition(MissionSetupCenter, out var top, 2f, NavMesh.AllAreas), "高台の上に歩ける床がない");
            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, top.position, NavMesh.AllAreas, path);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "石段から高台へ上がれない");
        }

        Vector3 MissionSetupCenter => BossArena.Instance.transform.position;

        // ---------- 通し ----------
        [UnityTest]
        public IEnumerator FirstChapter_CanBePlayedFromStartToEnd()
        {
            yield return LoadField();
            Protect();
            tracker.Begin();
            int Step() => tracker.StepIndex;

            Assert.AreEqual(0, Step());
            inventory.Add(Item("PistolAmmo"), 1);
            yield return null; yield return null;
            Assert.AreEqual(1, Step(), "弾を拾っても進まない");

            yield return new WaitForSeconds(0.1f);
            var first = EnemyBrain.All.Single(e => !e.RespawnEnabled);
            first.Health.TakeDamage(new DamageInfo { amount = 9999f, direction = Vector3.forward });
            yield return null;
            Assert.AreEqual(2, Step(), "敵を倒しても進まない");

            yield return GoTo("base");
            Assert.AreEqual(3, Step(), "納屋に着いても進まない");

            Object.FindObjectsByType<BaseStation>(FindObjectsSortMode.None).First(s => s.StationKind == BaseStation.Kind.Bed).Interact(interactor);
            yield return null;
            Assert.AreEqual(4, Step(), "寝床で休んでも進まない");
            Assert.IsTrue(File.Exists(SaveManager.SavePath), "進行が記録されていない");

            inventory.Add(Item("Cloth"), 2);
            inventory.Add(Item("Herb"), 1);
            Assert.IsTrue(crafting.Craft(Recipe("Bandage")));
            yield return null;
            Assert.AreEqual(5, Step(), "包帯を作っても進まない");

            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(4, EnemyBrain.All.Count(e => !e.RespawnEnabled && !e.Health.IsDead), "部品を守る敵が出ていない");
            foreach (var part in Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).Where(p => p.Item == Item("RadioPart")).ToArray())
                part.Interact(interactor);
            yield return null; yield return null;
            Assert.AreEqual(6, Step(), "部品を集めても進まない");

            Assert.IsTrue(crafting.Craft(Recipe("Radio")), "無線機を修理できない");
            yield return null;
            Assert.AreEqual(7, Step(), "無線機を直しても進まない");

            yield return GoTo("shrine");
            yield return new WaitForSeconds(0.2f);
            var arena = BossArena.Instance;
            Assert.IsTrue(arena.IsFighting, "ボス戦が始まらない");
            yield return new WaitForSeconds(1.8f);
            arena.Boss.Health.TakeDamage(new DamageInfo { amount = 99999f, direction = Vector3.forward, isMelee = true });
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(arena.IsFighting, "ボスを倒しても戦闘が終わらない");
            Assert.AreEqual(8, Step(), "ボスを倒しても進まない");

            var lamp = Object.FindAnyObjectByType<MissionLamp>();
            Assert.IsTrue(lamp.CanInteract, "灯籠に火をともせない");
            lamp.Interact(interactor);
            yield return null;
            Assert.IsTrue(tracker.IsComplete, "第一話が終わらない");
            Assert.IsTrue(player.GetComponent<EndingScreen>().IsPending, "終わりの画面が予約されていない");
        }

        [UnityTest]
        public IEnumerator Progress_IsRestoredAfterReloading()
        {
            yield return LoadField();
            tracker.Begin();
            inventory.Add(Item("PistolAmmo"), 1);
            yield return null; yield return null;
            Assert.AreEqual(1, tracker.StepIndex);
            Assert.IsTrue(player.GetComponent<SaveManager>().Save());

            yield return LoadField();
            Assert.IsTrue(tracker.IsActive, "ミッションが続いていない");
            Assert.AreEqual(1, tracker.StepIndex, "目的の進み具合が戻っていない");
        }

        [UnityTest]
        public IEnumerator KeyItems_CannotBeDropped()
        {
            yield return LoadField();
            inventory.Add(Item("RadioPart"), 1);
            Assert.IsFalse(inventory.Drop(Item("RadioPart")), "大切な物を捨てられた");
            Assert.AreEqual(1, inventory.CountOf(Item("RadioPart")));
        }

        // ---------- ボス ----------
        [UnityTest]
        public IEnumerator Boss_DoesNotStart_BeforeItsObjective()
        {
            yield return LoadField();
            tracker.Begin();
            yield return GoTo("shrine");
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(BossArena.Instance.IsFighting, "目的の前にボス戦が始まった");
        }

        [UnityTest]
        public IEnumerator Boss_GetsStrongerAsItLosesHealth()
        {
            yield return LoadField();
            Protect();
            yield return StartBossFight();
            var boss = BossArena.Instance.Boss;
            yield return new WaitForSeconds(1.8f);
            Assert.AreEqual(1, boss.Phase);
            boss.Health.TakeDamage(new DamageInfo { amount = boss.Health.Max * 0.4f, direction = Vector3.forward, isMelee = true });
            Assert.AreEqual(2, boss.Phase, "第 2 段階にならない");
            boss.Health.TakeDamage(new DamageInfo { amount = boss.Health.Max * 0.35f, direction = Vector3.forward, isMelee = true });
            Assert.AreEqual(3, boss.Phase, "第 3 段階にならない");
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(boss.MinionCount, 0, "第 3 段階で手下を呼ばない");
        }

        [UnityTest]
        public IEnumerator Boss_AttackIsTelegraphed_AndHurtsThePlayer()
        {
            yield return LoadField();
            yield return StartBossFight();
            var boss = BossArena.Instance.Boss;
            motor.Teleport(boss.transform.position + boss.transform.forward * 3f + Vector3.up * 0.2f, 0f);
            float end = Time.time + 8f;
            bool telegraphed = false;
            while (Time.time < end && health.Current >= health.Max)
            {
                telegraphed |= GameObject.Find("Telegraph") != null;
                yield return null;
            }
            Debug.Log($"[MatoiTest] boss hit: telegraphed={telegraphed} hp={health.Current}");
            Assert.IsTrue(telegraphed, "攻撃の予告が出ていない");
            Assert.Less(health.Current, health.Max, "ボスの攻撃が当たらない");
        }

        [UnityTest]
        public IEnumerator Barrier_KeepsThePlayerInside_DuringTheFight()
        {
            yield return LoadField();
            Protect();
            yield return StartBossFight();
            var arena = BossArena.Instance;
            motor.Teleport(arena.transform.position + new Vector3(0f, 0.3f, -14f), 0f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(arena.Contains(player.transform.position), "結界の外へ出られた");
        }

        [UnityTest]
        public IEnumerator LosingToTheBoss_ResetsTheFight_AndKeepsItems()
        {
            yield return LoadField();
            yield return StartBossFight();
            var arena = BossArena.Instance;
            var boss = arena.Boss;
            yield return new WaitForSeconds(1.8f);
            boss.Health.TakeDamage(new DamageInfo { amount = 300f, direction = Vector3.forward, isMelee = true });
            inventory.Add(Item("Bandage"), 2);
            health.TakeDamage(new DamageInfo { amount = 9999f, direction = Vector3.forward, point = player.transform.position });
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(arena.IsFighting, "倒れても戦闘が続いている");
            yield return new WaitForSeconds(3.2f);
            Assert.IsFalse(health.IsDead, "復活していない");
            Assert.AreEqual(2, inventory.CountOf(Item("Bandage")), "持ち物を失った");
            Assert.AreEqual(boss.Health.Max, boss.Health.Current, "ボスの体力が戻っていない");
            Assert.IsTrue(tracker.IsWaitingFor(MissionSignals.BossDefeated), "目的が変わってしまった");
        }

        // ---------- タイトルと一時停止 ----------
        [UnityTest]
        public IEnumerator TitleScreen_NewGame_StartsTheMission_AfterConfirmingOverSave()
        {
            yield return LoadField();
            inventory.Add(Item("Can"), 2);
            Assert.IsTrue(player.GetComponent<SaveManager>().Save());
            var title = player.GetComponent<TitleScreen>();
            title.Open();
            yield return null;
            Assert.AreEqual(TitleScreen.Choice.Continue, title.Choices[0], "記録があるのに「つづきから」が先頭にない");
            title.Select(TitleScreen.Choice.NewGame);
            Assert.IsTrue(File.Exists(SaveManager.SavePath), "確認の前に記録を消した");
            title.Select(TitleScreen.Choice.NewGame);
            // 画面の操作で読み直されたシーンを、そのまま確認する（もう一度読み直すと開始の合図が消える）
            yield return null;
            yield return null;
            yield return new WaitForSeconds(0.2f);
            player = GameObject.FindWithTag("Player");
            inventory = player.GetComponent<Inventory>();
            crafting = player.GetComponent<PlayerCrafting>();
            tracker = player.GetComponent<MissionTracker>();
            Assert.AreEqual(0, inventory.CountOf(Item("Can")), "前の記録の持ち物が残っている");
            Assert.IsTrue(tracker.IsActive, "はじめからでミッションが始まらない");
            Assert.AreEqual(0, tracker.StepIndex);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator PauseMenu_OpensWithStart_AndResumes()
        {
            yield return LoadField();
            var pause = player.GetComponent<PauseMenu>();
            Press(pad.startButton); yield return null; Release(pad.startButton); yield return null;
            Assert.IsTrue(pause.IsOpen, "Start で一時停止できない");
            Assert.AreEqual(0f, Time.timeScale, "一時停止でゲームが止まっていない");
            pause.Select(1);
            Assert.IsTrue(pause.ShowingHelp, "操作の説明が開かない");
            Press(pad.buttonSouth); yield return null; Release(pad.buttonSouth); yield return null;
            Assert.IsFalse(pause.ShowingHelp, "操作の説明を閉じられない");
            Press(pad.buttonEast); yield return null; Release(pad.buttonEast); yield return null;
            Assert.IsFalse(pause.IsOpen, "B で閉じない");
            Assert.AreEqual(1f, Time.timeScale, "閉じた後にゲームが動かない");
        }

        [UnityTest]
        public IEnumerator ClosingAMenuWithB_DoesNotOpenPause()
        {
            yield return LoadField();
            var inventoryScreen = player.GetComponent<InventoryScreen>();
            var pause = player.GetComponent<PauseMenu>();
            inventoryScreen.Open();
            yield return null;
            inventoryScreen.Close();
            Press(pad.startButton); yield return null; Release(pad.startButton);
            yield return null;
            Assert.IsTrue(!pause.IsOpen || MenuState.ClosedFrame != Time.frameCount, "メニューを閉じた同じフレームに一時停止が開いた");
            pause.Close();
        }

        [UnityTest]
        public IEnumerator ArenaShots()
        {
            yield return LoadField();
            Protect();
            yield return StartBossFight();
            yield return new WaitForSeconds(0.8f);
            var arena = BossArena.Instance;
            Shot("MISSION_arena", arena.transform.position + new Vector3(-8f, 6f, -11f), arena.transform.position + new Vector3(0f, 1f, 2f));
        }

        static void Shot(string name, Vector3 eye, Vector3 target)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 70f;
            go.transform.position = eye;
            go.transform.LookAt(target);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(go);
        }
    }
}
