// 拠点・クラフト・武器強化・セーブの確認。本番の記録には触らない（保存先を一時フォルダに差し替える）。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
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
    public class BaseAndSaveTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Inventory inventory;
        ItemStorage storage;
        PlayerCrafting crafting;
        PlayerInteractor interactor;
        WeaponShooter shooter;
        string saveFolder, previousOverride;

        GameCatalog Catalog => crafting.Catalog;
        ItemDefinition Item(string id) => Catalog.FindItem("Item_" + id);
        RecipeDefinition Recipe(string id) => Catalog.recipes.First(r => r.name == "Recipe_" + id);
        WeaponDefinition Pistol => Catalog.weapons.First(w => w.name == "Weapon_Pistol");
        WeaponDefinition Rifle => Catalog.weapons.First(w => w.name == "Weapon_AK47");

        [SetUp]
        public void UseTemporarySaveFolder()
        {
            pad = null;   // 仮想コントローラーはテストごとに作り直される
            previousOverride = SaveManager.DirectoryOverride;
            saveFolder = Path.Combine(Application.temporaryCachePath, "MatoiSaveTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveFolder);
            SaveManager.DirectoryOverride = saveFolder;
        }

        [TearDown]
        public void RestoreSaveFolder()
        {
            Time.timeScale = 1f;
            MenuState.IsOpen = false;
            SaveManager.DirectoryOverride = previousOverride;
            if (Directory.Exists(saveFolder)) Directory.Delete(saveFolder, true);
        }

        IEnumerator LoadField(bool hideEnemies = true)
        {
            pad ??= InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            if (hideEnemies)
                foreach (var enemy in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)) enemy.gameObject.SetActive(false);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            inventory = player.GetComponent<Inventory>();
            storage = player.GetComponent<ItemStorage>();
            crafting = player.GetComponent<PlayerCrafting>();
            interactor = player.GetComponent<PlayerInteractor>();
            shooter = player.GetComponent<WeaponShooter>();
            yield return new WaitForSeconds(0.2f);
        }

        BaseStation StationOf(BaseStation.Kind kind) =>
            Object.FindObjectsByType<BaseStation>(FindObjectsSortMode.None).First(s => s.StationKind == kind);

        IEnumerator StandAt(BaseStation station)
        {
            Vector3 center = BaseZone.Instance.transform.position;
            Vector3 d = Vector3.ProjectOnPlane(center - station.transform.position, Vector3.up).normalized;
            Assert.IsTrue(NavMesh.SamplePosition(station.transform.position + d * 1.3f, out var hit, 1.5f, NavMesh.AllAreas), $"{station.Label}の前に立てない");
            Vector3 to = station.Point - hit.position;
            motor.Teleport(hit.position + Vector3.up * 0.1f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator PressInteract()
        {
            Press(pad.dpad.up); yield return null; yield return null;
            Release(pad.dpad.up); yield return null;
        }

        // ---------- 拠点 ----------
        [UnityTest]
        public IEnumerator Base_HasEveryStation_AndTheyCanBeReached()
        {
            yield return LoadField();
            Assert.IsNotNull(BaseZone.Instance, "拠点の範囲が無い");
            foreach (var kind in new[] { BaseStation.Kind.Workbench, BaseStation.Kind.Storage, BaseStation.Kind.Bed })
            {
                var station = StationOf(kind);
                yield return StandAt(station);
                Assert.AreEqual(station, interactor.Current, $"{station.Label}を調べられない");
            }
        }

        [UnityTest]
        public IEnumerator Workbench_OpensByInteract_AndClosesWithB()
        {
            yield return LoadField();
            var screen = player.GetComponent<WorkbenchScreen>();
            yield return StandAt(StationOf(BaseStation.Kind.Workbench));
            yield return PressInteract();
            Assert.IsTrue(screen.IsOpen, "作業台の画面が開かない");
            Assert.AreEqual(0, screen.Cursor, "開いたボタンの押下でカーソルが動いた");
            Assert.AreEqual(0f, Time.timeScale, "作業台の画面でゲームが止まっていない");
            Press(pad.buttonEast); yield return null; Release(pad.buttonEast); yield return null;
            Assert.IsFalse(screen.IsOpen, "B ボタンで閉じない");
            Assert.AreEqual(1f, Time.timeScale, "閉じた後にゲームが動かない");
        }

        // ---------- クラフト ----------
        [UnityTest]
        public IEnumerator Craft_UsesMaterialsFromBothPlaces_AndNeedsEnough()
        {
            yield return LoadField();
            var recipe = Recipe("Bandage");
            Assert.IsFalse(crafting.Craft(recipe), "素材が無いのに作れた");

            inventory.Add(Item("Cloth"), 1);
            storage.Add(Item("Cloth"), 1);
            storage.Add(Item("Herb"), 1);
            Assert.IsTrue(crafting.Craft(recipe), "持ち物と保管箱の素材を合わせて作れない");
            Assert.AreEqual(2, inventory.CountOf(Item("Bandage")));
            Assert.AreEqual(0, crafting.Have(Item("Cloth")), "素材が減っていない");
            Assert.AreEqual(0, crafting.Have(Item("Herb")), "素材が減っていない");
        }

        [UnityTest]
        public IEnumerator Craft_PutsWhatDoesNotFitIntoStorage()
        {
            yield return LoadField();
            var heavy = ScriptableObject.CreateInstance<ItemDefinition>();
            heavy.weight = 1f;
            inventory.Add(heavy, 30);
            storage.Add(Item("Scrap"), 2);
            Assert.IsTrue(crafting.Craft(Recipe("PistolAmmo")));
            Assert.AreEqual(1, storage.CountOf(Item("PistolAmmo")), "持ちきれない物が保管箱に入っていない");
        }

        [UnityTest]
        public IEnumerator WorkbenchScreen_CraftsTheSelectedRecipe()
        {
            yield return LoadField();
            var screen = player.GetComponent<WorkbenchScreen>();
            inventory.Add(Item("Scrap"), 2);
            screen.Open();
            yield return null;
            screen.SelectRow(System.Array.IndexOf(Catalog.recipes, Recipe("PistolAmmo")));
            Press(pad.buttonSouth); yield return null; Release(pad.buttonSouth); yield return null;
            Assert.AreEqual(1, inventory.CountOf(Item("PistolAmmo")), "A ボタンで作れない");
            screen.Close();
        }

        // ---------- 保管箱 ----------
        [UnityTest]
        public IEnumerator Storage_MovesItems_AndRespectsTheWeightLimit()
        {
            yield return LoadField();
            var screen = player.GetComponent<StorageScreen>();
            var scrap = Item("Scrap");
            inventory.Add(scrap, 30);
            inventory.Add(Item("Can"), 0);
            Assert.AreEqual(10, storage.Deposit(inventory, scrap, 10));
            Assert.AreEqual(20, inventory.CountOf(scrap));
            storage.Add(scrap, 40);
            Assert.AreEqual(10, storage.Withdraw(inventory, scrap, 50), "重さの上限を超えて取り出せた");
            Assert.AreEqual(40, storage.CountOf(scrap));

            inventory.Add(Item("Herb"), 0);
            Assert.AreEqual(30, screen.DepositMaterials(), "素材をまとめて預けられない");
            Assert.AreEqual(0f, inventory.CurrentWeight, 0.01f);
        }

        // ---------- 武器強化 ----------
        [UnityTest]
        public IEnumerator Upgrades_RaiseTheWeaponStats_WithoutChangingItsData()
        {
            yield return LoadField();
            var pistol = Pistol;
            int baseMagazine = pistol.magazineSize;
            float baseDamage = pistol.damage;
            var magazine = Catalog.UpgradeOf(UpgradeKind.Magazine);
            var damage = Catalog.UpgradeOf(UpgradeKind.Damage);
            var stability = Catalog.UpgradeOf(UpgradeKind.Stability);

            Assert.IsFalse(crafting.TryUpgrade(pistol, damage), "素材が無いのに強化できた");
            storage.Add(Item("Scrap"), 60);
            storage.Add(Item("Wood"), 10);
            storage.Add(Item("Parts"), 30);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(crafting.TryUpgrade(pistol, damage));
                Assert.IsTrue(crafting.TryUpgrade(pistol, magazine));
            }
            Assert.IsTrue(crafting.TryUpgrade(pistol, stability));
            Assert.IsFalse(crafting.TryUpgrade(pistol, damage), "最大段階を超えて強化できた");

            Assert.AreEqual(1.3f, crafting.DamageScale(pistol), 0.001f);
            Assert.AreEqual(Mathf.RoundToInt(baseMagazine * 1.6f), crafting.MagazineSize(pistol));
            Assert.AreEqual(0.85f, crafting.StabilityScale(pistol), 0.001f);
            Assert.AreEqual(0, crafting.Level(Rifle, UpgradeKind.Damage), "別の武器まで強化された");
            Assert.AreEqual(baseMagazine, pistol.magazineSize, "武器のデータを書き換えている");
            Assert.AreEqual(baseDamage, pistol.damage, "武器のデータを書き換えている");
        }

        [UnityTest]
        public IEnumerator UpgradedMagazine_IsFilledByReload()
        {
            yield return LoadField();
            var holder = player.GetComponent<WeaponHolder>();
            holder.Select(0);
            yield return new WaitForSeconds(0.3f);
            storage.Add(Item("Scrap"), 10);
            storage.Add(Item("Wood"), 2);
            Assert.IsTrue(crafting.TryUpgrade(holder.CurrentWeapon, Catalog.UpgradeOf(UpgradeKind.Magazine)));
            int capacity = shooter.MagazineCapacity;
            Assert.Greater(capacity, holder.CurrentWeapon.magazineSize);
            shooter.Reload();
            yield return new WaitForSeconds(holder.CurrentWeapon.reloadTime + 0.3f);
            Assert.AreEqual(capacity, shooter.AmmoInMagazine, "増えた弾倉まで弾が入っていない");
        }

        // ---------- 弾 ----------
        [UnityTest]
        public IEnumerator PistolAmmo_IsFinite_AndCanBeRefilled()
        {
            yield return LoadField();
            var holder = player.GetComponent<WeaponHolder>();
            holder.Select(0);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual("Weapon_Pistol", holder.CurrentWeapon.name);
            Assert.AreEqual(48, shooter.ReserveAmmo, "拳銃の予備弾が 48 発ではない");
            inventory.Add(Item("PistolAmmo"), 1);
            Assert.IsTrue(inventory.Use(Item("PistolAmmo")));
            Assert.AreEqual(72, shooter.ReserveAmmo, "拳銃の弾が補充されない");
        }

        [UnityTest]
        public IEnumerator RefillAll_NeverLowersTheReserve()
        {
            yield return LoadField();
            var holder = player.GetComponent<WeaponHolder>();
            holder.Select(0);
            yield return new WaitForSeconds(0.2f);
            shooter.AddReserveAmmo(holder.CurrentWeapon, 100);
            shooter.RefillAll();
            Assert.AreEqual(148, shooter.ReserveAmmo, "補充で予備の弾が減った");
        }

        // ---------- 敵 ----------
        [UnityTest]
        public IEnumerator Enemies_DoNotFollowThePlayerIntoTheBase()
        {
            yield return LoadField(hideEnemies: false);
            var enemy = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).OrderBy(x => x.name).First();
            foreach (var other in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                if (other != enemy) other.gameObject.SetActive(false);

            Vector3 center = BaseZone.Instance.transform.position;
            Assert.IsTrue(NavMesh.SamplePosition(center + Vector3.left * (BaseZone.Instance.Radius + 3f), out var outside, 4f, NavMesh.AllAreas));
            enemy.PlaceAt(outside.position + Vector3.up * 0.1f, 90f);
            yield return StandAt(StationOf(BaseStation.Kind.Workbench));
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(enemy.IsHuntingPlayer, "拠点の中のプレイヤーを追いかけている");
            Assert.IsFalse(BaseZone.Contains(enemy.transform.position), "敵が拠点に入った");

            // 拠点の外に出れば、今までどおり見つかる
            motor.Teleport(outside.position + Vector3.left * 2f + Vector3.up * 0.1f, 90f);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(enemy.IsHuntingPlayer, "拠点の外でも敵が反応しない");
        }

        [UnityTest]
        public IEnumerator Enemies_CanDropLoot()
        {
            yield return LoadField(hideEnemies: false);
            var definition = EnemyBrain.All.OrderBy(x => x.name).First().Definition;
            Assert.IsNotNull(definition.drops, "敵に落とす物が設定されていない");
            Assert.IsTrue(definition.drops.entries.Any(e => e.item == Item("Cloth")), "布切れを落とさない");
            Assert.IsTrue(definition.drops.entries.Any(e => e.item == Item("Parts")), "機械部品を落とさない");
        }

        // ---------- 寝床とセーブ ----------
        [UnityTest]
        public IEnumerator Bed_HealsSetsRespawnAndSaves()
        {
            yield return LoadField();
            var health = player.GetComponent<Health>();
            health.TakeDamage(new DamageInfo { amount = 50f, point = player.transform.position, direction = Vector3.forward });
            yield return new WaitForSeconds(0.5f);
            yield return StandAt(StationOf(BaseStation.Kind.Bed));
            Vector3 stood = player.transform.position;
            yield return PressInteract();
            Assert.AreEqual(health.Max, health.Current, 0.01f, "寝床で回復しない");
            Assert.IsTrue(File.Exists(SaveManager.SavePath), "寝床で記録されていない");
            Assert.Less(Vector3.Distance(stood, player.GetComponent<PlayerLife>().RespawnPosition), 0.5f, "復活地点が寝床になっていない");
        }

        [UnityTest]
        public IEnumerator SaveAndReload_RestoresTheWholeGame()
        {
            yield return LoadField();
            var holder = player.GetComponent<WeaponHolder>();
            holder.Select(0);
            yield return new WaitForSeconds(0.2f);

            inventory.Add(Item("Bandage"), 3);
            storage.Add(Item("Scrap"), 40);
            storage.Add(Item("Wood"), 6);
            Assert.IsTrue(crafting.TryUpgrade(Pistol, Catalog.UpgradeOf(UpgradeKind.Damage)));
            shooter.SetAmmo(Pistol, 5, 17);

            var point = ExplorationPoint.All.First(p => p.DisplayName == "錆びたバス");
            point.Discovered = true;
            player.GetComponent<ExplorationTracker>().RecountDiscovered();

            var box = Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None).First(c => c.Label == "作業員のロッカー");
            string boxId = box.SaveId;
            box.Searched = true;
            var node = Object.FindObjectsByType<GatherNode>(FindObjectsSortMode.None).First();
            string nodeId = node.SaveId;
            node.RemainingRespawn = 120f;
            var pickup = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).First(p => p.Item == Item("Medkit"));
            string pickupId = pickup.SaveId;
            Object.Destroy(pickup.gameObject);
            WorldPickup.Spawn(Item("Charm"), 2, new Vector3(3f, 0.5f, -14f));
            yield return null;

            var health = player.GetComponent<Health>();
            health.TakeDamage(new DamageInfo { amount = 30f, point = player.transform.position, direction = Vector3.forward });
            yield return new WaitForSeconds(0.5f);
            Vector3 respawn = new Vector3(24f, 0.3f, -14f);
            player.GetComponent<PlayerLife>().SetRespawnPoint(respawn, 45f);
            motor.Teleport(new Vector3(3f, 0.2f, -12f), 30f);
            yield return new WaitForSeconds(0.5f);
            Vector3 position = player.transform.position;
            Assert.IsTrue(player.GetComponent<SaveManager>().Save(), "記録に失敗した");

            yield return LoadField();
            holder = player.GetComponent<WeaponHolder>();
            holder.Select(0);
            yield return new WaitForSeconds(0.2f);

            Assert.AreEqual(3, inventory.CountOf(Item("Bandage")), "持ち物が戻っていない");
            Assert.AreEqual(36, storage.CountOf(Item("Scrap")), "保管箱が戻っていない");
            Assert.AreEqual(1, crafting.Level(Pistol, UpgradeKind.Damage), "強化が戻っていない");
            Assert.AreEqual(5, shooter.AmmoInMagazine, "弾倉の弾が戻っていない");
            Assert.AreEqual(17, shooter.ReserveAmmo, "予備の弾が戻っていない");
            Assert.AreEqual(70f, player.GetComponent<Health>().Current, 0.01f, "体力が戻っていない");
            Assert.Less(Vector3.Distance(position, player.transform.position), 0.5f, "現在地が戻っていない");
            Assert.Less(Vector3.Distance(respawn, player.GetComponent<PlayerLife>().RespawnPosition), 0.01f, "復活地点が戻っていない");

            Assert.IsTrue(ExplorationPoint.All.First(p => p.DisplayName == "錆びたバス").Discovered, "発見した場所が戻っていない");
            Assert.AreEqual(1, player.GetComponent<ExplorationTracker>().DiscoveredCount);
            Assert.IsTrue(Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None).First(c => c.SaveId == boxId).Searched, "物色済みが戻っていない");
            Assert.IsTrue(Object.FindObjectsByType<GatherNode>(FindObjectsSortMode.None).First(n => n.SaveId == nodeId).IsDepleted, "採集場所の状態が戻っていない");
            Assert.IsFalse(Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).Any(p => p.SaveId == pickupId), "拾った物が元に戻っている");
            var dropped = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).FirstOrDefault(p => p.Item == Item("Charm") && string.IsNullOrEmpty(p.SaveId));
            Assert.IsNotNull(dropped, "地面に置いた物が戻っていない");
            Assert.AreEqual(2, dropped.Count);
        }

        [UnityTest]
        public IEnumerator EveryPlacedThing_HasAUniqueSaveId()
        {
            yield return LoadField();
            var ids = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None)
                .Where(t => !(t is BaseStation)).Select(t => t.SaveId).ToArray();
            Assert.IsTrue(ids.All(id => !string.IsNullOrEmpty(id)), "記録用の名前が無い物がある");
            Assert.AreEqual(ids.Length, ids.Distinct().Count(), "記録用の名前が重複している");
        }

        [UnityTest]
        public IEnumerator BrokenSave_FallsBackToTheBackup()
        {
            yield return LoadField();
            inventory.Add(Item("Can"), 2);
            var save = player.GetComponent<SaveManager>();
            Assert.IsTrue(save.Save());
            inventory.Add(Item("Can"), 1);
            Assert.IsTrue(save.Save());
            Assert.IsTrue(File.Exists(SaveManager.SavePath + ".bak"), "ひとつ前の記録が残っていない");
            File.WriteAllText(SaveManager.SavePath, "{ 壊れた記録");

            LogAssert.ignoreFailingMessages = true;
            yield return LoadField();
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(2, inventory.CountOf(Item("Can")), "壊れた時にひとつ前の記録を使っていない");
        }

        [UnityTest]
        public IEnumerator ResetGame_DeletesTheSave_AfterConfirmation()
        {
            yield return LoadField();
            inventory.Add(Item("Can"), 2);
            Assert.IsTrue(player.GetComponent<SaveManager>().Save());
            var screen = player.GetComponent<InventoryScreen>();
            screen.Open();
            yield return null;
            screen.RequestReset();
            Assert.IsTrue(File.Exists(SaveManager.SavePath), "確認の前に記録を消した");
            Assert.IsTrue(screen.IsOpen, "確認の前に画面を閉じた");
            screen.RequestReset();
            yield return null;
            yield return LoadField();
            Assert.IsFalse(File.Exists(SaveManager.SavePath), "記録が消えていない");
            Assert.AreEqual(0, inventory.Stacks.Count, "持ち物が残っている");
            Assert.AreEqual(1f, Time.timeScale, "ゲームが止まったまま");
        }

        [UnityTest]
        public IEnumerator BaseShots()
        {
            yield return LoadField();
            var barn = GameObject.Find("House_25_-14").transform;
            var entrance = barn.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Entrance"));
            Vector3 front = Vector3.ProjectOnPlane(entrance.position - barn.position, Vector3.up).normalized;
            Shot("BASE_inside", barn.position + front * 3.6f + Vector3.up * 2.3f, barn.position + Vector3.up * 0.5f);
            Shot("BASE_outside", barn.position + front * 11f + Vector3.up * 5f + Vector3.Cross(Vector3.up, front) * 4f, barn.position + front * 2f);
        }

        static void Shot(string name, Vector3 eye, Vector3 target)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
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
