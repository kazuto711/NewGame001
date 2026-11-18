// 採集・探索の確認：拾う、重さの上限、使う、長押しの採集、物色、発見、持ち物画面、建物に入れること。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Items;
using Matoi.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class FieldContentTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Inventory inventory;
        PlayerInteractor interactor;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            foreach (var enemy in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)) enemy.gameObject.SetActive(false);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            inventory = player.GetComponent<Inventory>();
            interactor = player.GetComponent<PlayerInteractor>();
            yield return new WaitForSeconds(0.2f);
        }

        [TearDown]
        public void ResetTime() { Time.timeScale = 1f; MenuState.IsOpen = false; }

        IEnumerator StandBeside(Interactable target)
        {
            Vector3 d = Vector3.ProjectOnPlane(motor.transform.position - target.Point, Vector3.up).normalized;
            if (NavMesh.SamplePosition(target.Point + d * 1.3f, out var hit, 2f, NavMesh.AllAreas))
            {
                Vector3 to = target.Point - hit.position;
                motor.Teleport(hit.position + Vector3.up * 0.1f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            }
            yield return new WaitForSeconds(0.3f);
        }

        ItemDefinition FindItem(string assetName) =>
            Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).Select(p => p.Item).First(i => i.name == assetName);

        [UnityTest]
        public IEnumerator Field_HasEnoughThingsToFind()
        {
            yield return LoadField();
            int pickups = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).Length;
            int nodes = Object.FindObjectsByType<GatherNode>(FindObjectsSortMode.None).Length;
            int containers = Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None).Length;
            Debug.Log($"[MatoiTest] field content: pickups={pickups} nodes={nodes} containers={containers} points={ExplorationPoint.All.Count}");
            Assert.GreaterOrEqual(pickups, 12);
            Assert.GreaterOrEqual(nodes, 12);
            Assert.GreaterOrEqual(containers, 15);
            Assert.GreaterOrEqual(ExplorationPoint.All.Count, 8);
        }

        [UnityTest]
        public IEnumerator Press_PicksUpTheItemOnTheGround()
        {
            yield return LoadField();
            var pickup = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None).First(p => p.Item.name == "Item_Medkit");
            var item = pickup.Item;
            yield return StandBeside(pickup);
            Assert.AreEqual(pickup, interactor.Current, "近くの物が調べる対象になっていない");
            StringAssert.Contains(item.displayName, player.GetComponent<FieldHud>().Prompt, "操作の案内が出ていない");
            Press(pad.dpad.up); yield return null; yield return null; Release(pad.dpad.up);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, inventory.CountOf(item), "持ち物に入っていない");
            Assert.IsTrue(pickup == null, "拾った物が地面に残っている");
        }

        [UnityTest]
        public IEnumerator Inventory_RefusesWhatIsTooHeavy()
        {
            yield return LoadField();
            var wood = ScriptableObject.CreateInstance<ItemDefinition>();
            wood.weight = 1f;
            Assert.AreEqual(30, inventory.Add(wood, 100), "重さの上限まで持てていない");
            Assert.AreEqual(0, inventory.Add(wood, 1), "上限を超えて持てている");
            Assert.AreEqual(5, inventory.Remove(wood, 5));
            Assert.AreEqual(25f, inventory.CurrentWeight, 0.01f);
        }

        [UnityTest]
        public IEnumerator UsingItems_HealsAndRefillsAmmo_WithoutWasting()
        {
            yield return LoadField();
            var health = player.GetComponent<Health>();
            var bandage = FindItem("Item_Bandage");
            inventory.Add(bandage, 2);
            Assert.IsFalse(inventory.Use(bandage), "体力が満タンなのに消費した");
            Assert.AreEqual(2, inventory.CountOf(bandage));
            health.TakeDamage(new DamageInfo { amount = 40f, point = player.transform.position, direction = Vector3.forward });
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(inventory.Use(bandage));
            Assert.AreEqual(85f, health.Current, 0.01f, "包帯で回復していない");
            Assert.AreEqual(1, inventory.CountOf(bandage));

            Press(pad.rightShoulder); yield return null; yield return null; Release(pad.rightShoulder);
            yield return new WaitForSeconds(0.3f);
            var shooter = player.GetComponent<WeaponShooter>();
            var ammo = FindItem("Item_RifleAmmo");
            inventory.Add(ammo, 1);
            int before = shooter.ReserveAmmo;
            Assert.IsTrue(inventory.Use(ammo));
            Assert.AreEqual(before + 30, shooter.ReserveAmmo, "予備の弾が増えていない");
        }

        [UnityTest]
        public IEnumerator Gathering_NeedsHolding_ThenDepletesTheSpot()
        {
            yield return LoadField();
            var node = Object.FindObjectsByType<GatherNode>(FindObjectsSortMode.None).First(n => n.Label == "薬草");
            yield return StandBeside(node);
            Assert.AreEqual(node, interactor.Current);

            Press(pad.dpad.up);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(interactor.IsHolding, "長押しが始まっていない");
            Release(pad.dpad.up);
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(interactor.IsHolding, "離しても長押しが続いている");
            Assert.AreEqual(0, inventory.Stacks.Count, "途中で離したのに手に入った");
            yield return new WaitForSeconds(0.4f);

            Vector3 start = motor.transform.position;
            Press(pad.dpad.up);
            Set(pad.leftStick, Vector2.up);
            yield return new WaitForSeconds(1.1f);
            float moved = Vector3.Distance(start, motor.transform.position);
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.6f);
            Release(pad.dpad.up);
            yield return null;
            Assert.Less(moved, 0.15f, "採集中に歩いてしまった");
            Assert.AreEqual(1, inventory.Stacks.Count, "採集で手に入っていない");
            Assert.IsTrue(node.IsDepleted, "採集した場所が残っている");
        }

        [UnityTest]
        public IEnumerator Searching_GivesLootOnce()
        {
            yield return LoadField();
            var box = Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None).First(c => c.Label == "作業員のロッカー");
            yield return StandBeside(box);
            Assert.AreEqual(box, interactor.Current, "入れ物が調べる対象になっていない");
            Press(pad.dpad.up);
            yield return new WaitForSeconds(1.1f);
            Release(pad.dpad.up);
            yield return null;
            Assert.IsTrue(box.Searched, "物色が終わっていない");
            Assert.Greater(inventory.Stacks.Count, 0, "必ず中身がある入れ物が空だった");
            Assert.IsFalse(box.CanInteract, "同じ入れ物を何度も調べられる");
        }

        [UnityTest]
        public IEnumerator WalkingNear_DiscoversThePlace()
        {
            yield return LoadField();
            var tracker = player.GetComponent<ExplorationTracker>();
            var hud = player.GetComponent<FieldHud>();
            var point = ExplorationPoint.All.First(p => p.DisplayName == "錆びたバス");
            Assert.IsFalse(point.Discovered);
            NavMesh.SamplePosition(point.transform.position + Vector3.right * 4f, out var hit, 5f, NavMesh.AllAreas);
            motor.Teleport(hit.position + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(point.Discovered, "近づいても発見にならない");
            Assert.AreEqual(1, tracker.DiscoveredCount);
            StringAssert.Contains(point.DisplayName, hud.Banner);
        }

        [UnityTest]
        public IEnumerator InventoryScreen_PausesAndUsesTheSelectedItem()
        {
            yield return LoadField();
            var screen = player.GetComponent<InventoryScreen>();
            var health = player.GetComponent<Health>();
            health.TakeDamage(new DamageInfo { amount = 30f, point = player.transform.position, direction = Vector3.forward });
            yield return new WaitForSeconds(0.5f);
            var can = FindItem("Item_Can");
            inventory.Add(can, 1);

            Press(pad.dpad.right); yield return null; Release(pad.dpad.right); yield return null;
            Assert.IsTrue(screen.IsOpen, "十字キー右で持ち物画面が開かない");
            Assert.AreEqual(0f, Time.timeScale, "持ち物画面でゲームが止まっていない");
            Press(pad.buttonSouth); yield return null; Release(pad.buttonSouth); yield return null;
            Assert.AreEqual(85f, health.Current, 0.01f, "A ボタンで使えていない");
            Assert.AreEqual(0, inventory.CountOf(can));
            Press(pad.buttonEast); yield return null; Release(pad.buttonEast); yield return null;
            Assert.IsFalse(screen.IsOpen, "B ボタンで閉じない");
            Assert.AreEqual(1f, Time.timeScale, "閉じた後にゲームが動かない");
        }

        [UnityTest]
        public IEnumerator Kura_CanBeEnteredOnFoot() => WalkInto("House_27_5", "FC_kura");

        [UnityTest]
        public IEnumerator RuinedHouse_CanBeEnteredOnFoot() => WalkInto("House_14_-2", "FC_ruin");

        IEnumerator WalkInto(string houseName, string shot)
        {
            yield return LoadField();
            var house = GameObject.Find(houseName).transform;
            var entrance = house.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Entrance"));
            Vector3 front = Vector3.ProjectOnPlane(entrance.position - house.position, Vector3.up).normalized;
            Assert.IsTrue(NavMesh.SamplePosition(entrance.position + front * 2f, out var outside, 3f, NavMesh.AllAreas), "入口の前に立てない");
            Assert.IsTrue(NavMesh.SamplePosition(house.position + Vector3.up * 0.5f, out var inside, 1.5f, NavMesh.AllAreas), "建物の中に歩ける床が無い");

            var path = new NavMeshPath();
            NavMesh.CalculatePath(outside.position, inside.position, NavMesh.AllAreas, path);
            Debug.Log($"[MatoiTest] {houseName}: outside={outside.position} inside={inside.position} status={path.status} corners={string.Join(" ", path.corners.Select(c => c.ToString("0.0")))}");
            CloseShot(shot + "_above", house.position + Vector3.up * 16f - front * 7f, house.position - front * 3f);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "入口から中までの道がつながっていない");

            motor.Teleport(outside.position + Vector3.up * 0.1f, 0f);
            yield return new WaitForSeconds(0.2f);
            foreach (var corner in path.corners.Skip(1))
            {
                float limit = Time.time + 6f;
                while (Time.time < limit)
                {
                    Vector3 d = Vector3.ProjectOnPlane(corner - motor.transform.position, Vector3.up);
                    if (d.magnitude < 0.35f) break;
                    var l = Quaternion.Euler(0, -CamYaw, 0) * d.normalized * 0.7f;
                    Set(pad.leftStick, new Vector2(l.x, l.z));
                    yield return null;
                }
            }
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.3f);

            float distance = Vector3.Distance(Vector3.ProjectOnPlane(motor.transform.position, Vector3.up), Vector3.ProjectOnPlane(inside.position, Vector3.up));
            Debug.Log($"[MatoiTest] {houseName}: reached inside, distance={distance:0.00} height={motor.transform.position.y:0.00}");
            CloseShot(shot, motor.transform.position + Vector3.up * 1.6f - motor.transform.forward * 1.6f, motor.transform.position + Vector3.up * 1f + motor.transform.forward);
            Assert.Less(distance, 1f, "建物の中まで歩いて入れない");
        }

        void CloseShot(string name, Vector3 eye, Vector3 target)
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
