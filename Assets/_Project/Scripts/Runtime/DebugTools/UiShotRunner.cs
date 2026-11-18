using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
using Matoi.Missions;
using Matoi.SaveSystem;
using Matoi.UI;
using UnityEngine;

namespace Matoi.DebugTools
{
    /// <summary>
    /// 画面表示の確認用。起動オプション -matoiuishots フォルダ を付けた時だけ動き、各画面を順番に撮影して終了する。
    /// 記録は一時フォルダに書き、本番のセーブデータには触らない。
    /// </summary>
    public class UiShotRunner : MonoBehaviour
    {
        static string outputFolder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int index = System.Array.IndexOf(args, "-matoiuishots");
            if (index < 0 || index + 1 >= args.Length) return;
            outputFolder = args[index + 1];
            Directory.CreateDirectory(outputFolder);
            SaveManager.DirectoryOverride = Path.Combine(outputFolder, "save");
            if (Directory.Exists(SaveManager.DirectoryOverride)) Directory.Delete(SaveManager.DirectoryOverride, true);
            Directory.CreateDirectory(SaveManager.DirectoryOverride);
            Application.runInBackground = true;
            var go = new GameObject("UiShotRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<UiShotRunner>();
        }

        static IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(outputFolder, name + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(3f);
            var player = GameObject.FindWithTag("Player");
            yield return Shot("01_title");

            player.GetComponent<TitleScreen>().Select(TitleScreen.Choice.NewGame);
            yield return new WaitForSecondsRealtime(4f);
            player = GameObject.FindWithTag("Player");
            player.GetComponent<Health>().InvulnerableUntil = float.MaxValue;
            foreach (var enemy in EnemyBrain.All.ToArray()) enemy.gameObject.SetActive(false);
            yield return Shot("02_hud_mission");

            var crafting = player.GetComponent<PlayerCrafting>();
            var inventory = player.GetComponent<Inventory>();
            var storage = player.GetComponent<ItemStorage>();
            var catalog = crafting.Catalog;
            foreach (string id in new[] { "Bandage", "Can", "Herb", "Cloth", "Scrap", "RadioPart" }) inventory.Add(catalog.FindItem("Item_" + id), 3);
            storage.Add(catalog.FindItem("Item_Wood"), 8);
            storage.Add(catalog.FindItem("Item_Parts"), 4);

            var inventoryScreen = player.GetComponent<InventoryScreen>();
            inventoryScreen.Open();
            yield return Shot("03_inventory");
            inventoryScreen.Close();

            var workbench = player.GetComponent<WorkbenchScreen>();
            workbench.Open();
            yield return Shot("04_workbench_craft");
            workbench.SelectTab(1);
            yield return Shot("05_workbench_upgrade");
            workbench.Close();

            var storageScreen = player.GetComponent<StorageScreen>();
            storageScreen.Open();
            yield return Shot("06_storage");
            storageScreen.Close();

            var pause = player.GetComponent<PauseMenu>();
            pause.Open();
            yield return Shot("07_pause");
            pause.Select(1);
            yield return Shot("08_controls");
            pause.Close();

            var tracker = player.GetComponent<MissionTracker>();
            int bossStep = System.Array.FindIndex(tracker.Mission.steps, s => s.signal == MissionSignals.BossDefeated);
            tracker.Restore(true, bossStep, 0, false);
            player.GetComponent<CharacterMotor>().Teleport(BossArena.Instance.transform.position + new Vector3(0f, 0.3f, -4f), 0f);
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot("09_boss");

            BossArena.Instance.Boss.Health.TakeDamage(new DamageInfo { amount = 99999f, direction = Vector3.forward, isMelee = true });
            yield return new WaitForSecondsRealtime(1f);
            var ending = player.GetComponent<EndingScreen>();
            ending.Open();
            yield return Shot("10_ending");
            Application.Quit();
        }
    }
}
