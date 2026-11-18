// パワーアーマーの動作確認：乗り降り・部品の取り付け位置・ダメージの肩代わり・足音・重いパンチ・体当たり・破損。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Equipment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class PowerArmorTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Health health;
        PowerArmorUser armor;
        PowerArmorStation station;
        Animator animator;

        IEnumerator LoadAndBoard()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            // このテストは見下ろし視点を前提にしている（三人称視点は ThirdPersonCameraTests で確認）
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            health = player.GetComponent<Health>();
            armor = player.GetComponent<PowerArmorUser>();
            animator = player.GetComponentInChildren<Animator>();
            station = Object.FindAnyObjectByType<PowerArmorStation>();
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.3f);
            motor.Teleport(station.transform.position - station.transform.forward * 1.6f, station.transform.eulerAngles.y);
            yield return new WaitForSeconds(0.2f);
            Assert.IsNotNull(armor.NearestAvailable(), "アーマーの近くなのに乗り込めない");
            yield return Tap(pad.dpad.up, 0.4f);
        }

        IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl b, float after = 0.3f)
        {
            Press(b); yield return null; yield return null; Release(b);
            yield return new WaitForSeconds(after);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            worldDir.y = 0;
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        float PieceToBone(string piece, HumanBodyBones bone)
        {
            var p = player.GetComponentsInChildren<Transform>(true).First(t => t.name == piece);
            return Vector3.Distance(p.position, animator.GetBoneTransform(bone).position);
        }

        [UnityTest]
        public IEnumerator Board_ChangesBody_AndPiecesSitOnBones()
        {
            yield return LoadAndBoard();
            var cc = player.GetComponent<CharacterController>();
            var pieces = player.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("PA_")).ToArray();
            float helmet = PieceToBone("PA_Helmet", HumanBodyBones.Head);
            float chest = PieceToBone("PA_Chest", HumanBodyBones.UpperChest);
            float footL = PieceToBone("PA_Foot_L", HumanBodyBones.LeftFoot);
            float handR = PieceToBone("PA_Hand_R", HumanBodyBones.RightHand);
            // ヘルメットの目（Front）が体の正面側を向いているか
            var helm = pieces.First(t => t.name == "PA_Helmet");
            Transform front = null;
            foreach (Transform c in helm) if (c.name.StartsWith("Front")) front = c;
            float facing = Vector3.Dot(Flat(front.position - helm.position).normalized, Flat(player.transform.forward));
            Debug.Log($"[MatoiTest] armor board: armored={armor.IsArmored} pieces={pieces.Length} active={pieces.Count(t => t.gameObject.activeInHierarchy)} " +
                      $"profile={motor.Profile.name} height={cc.height} helmet={helmet:0.00} chest={chest:0.00} footL={footL:0.00} handR={handR:0.00} helmetFacing={facing:0.00}");
            CloseUp("PA_front", 0f);
            CloseUp("PA_side", 90f);
            Shot("PA_game");
            var visible = player.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !r.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith("PA_")));
            Debug.Log("[MatoiTest] armor visible non-armor renderers: " + string.Join(", ", visible.Select(r => r.name)));
            Assert.AreEqual(0, player.GetComponentsInChildren<Weapon>().Length, "しまった武器が装甲からはみ出している");
            Assert.IsTrue(armor.IsArmored);
            Assert.AreEqual("Move_PowerArmor", motor.Profile.name);
            Assert.AreEqual(16, pieces.Count(t => t.gameObject.activeInHierarchy), "装甲部品が表示されていない");
            Assert.IsTrue(station.Occupied);
            Assert.Greater(cc.height, 2f, "体が大きくなっていない");
            Assert.Less(helmet, 0.3f); Assert.Less(chest, 0.3f); Assert.Less(footL, 0.25f); Assert.Less(handR, 0.2f);
            Assert.Greater(facing, 0.7f, "ヘルメットが前を向いていない");
        }

        [UnityTest]
        public IEnumerator Armor_AbsorbsDamage_AndDoesNotStagger()
        {
            yield return LoadAndBoard();
            float integrity0 = armor.Integrity;
            health.TakeDamage(new DamageInfo { amount = 50f, direction = Vector3.forward, knockback = 5f });
            yield return null;
            var reaction = player.GetComponent<HitReaction>();
            Debug.Log($"[MatoiTest] armor absorb: hp={health.Current} integrity {integrity0}->{armor.Integrity} staggered={reaction.IsStaggered}");
            Assert.AreEqual(health.Max - 10f, health.Current, 0.01f, "本人へのダメージが 20% になっていない");
            Assert.AreEqual(integrity0 - 40f, armor.Integrity, 0.01f, "装甲がダメージを肩代わりしていない");
            Assert.IsFalse(reaction.IsStaggered, "アーマー中にひるんでいる");
        }

        [UnityTest]
        public IEnumerator Walking_MakesHeavyFootsteps()
        {
            yield return LoadAndBoard();
            var steps = player.GetComponent<PowerArmorFootsteps>();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            Set(pad.leftStick, StickFor(Vector3.forward));
            yield return new WaitForSeconds(2.5f);
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] armor steps: {steps.Steps} in 2.5s, speed={motor.Speed:0.00}");
            Assert.GreaterOrEqual(steps.Steps, 3, "足音（着地）が検出されていない");
        }

        [UnityTest]
        public IEnumerator HeavyPunch_And_Ram_ThrowEnemies()
        {
            yield return LoadAndBoard();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            yield return null;
            var e = EnemyBrain.All.OrderBy(x => x.name).First();
            e.PlaceAt(player.transform.position + Vector3.forward * 1.7f, 180f);
            yield return null;
            Vector3 before = e.transform.position;
            yield return Tap(pad.buttonEast, 0.7f);
            float punchMoved = Vector3.Distance(Flat(before), Flat(e.transform.position));
            float punchDamage = e.Health.Max - e.Health.Current;
            CloseUp("PA_punch", 60f);

            // 走って体当たり
            var e2 = EnemyBrain.All.OrderBy(x => x.name).ElementAt(1);
            e2.PlaceAt(player.transform.position + Vector3.forward * 9f, 180f);
            var ram = player.GetComponent<PowerArmorRam>();
            Set(pad.leftStick, StickFor(Vector3.forward));
            yield return null;
            Press(pad.leftStickButton); yield return null; Release(pad.leftStickButton);
            float t = 0f;
            while (t < 3f && ram.RamHits == 0) { t += Time.deltaTime; yield return null; }
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] armor punch: dmg={punchDamage} moved={punchMoved:0.00}  ram hits={ram.RamHits} at {t:0.00}s e2hp={e2.Health.Current}");
            Assert.AreEqual(70f, punchDamage, 0.01f, "重いパンチのダメージが違う");
            Assert.Greater(punchMoved, 1.2f, "重いパンチで敵が吹き飛んでいない");
            Assert.GreaterOrEqual(ram.RamHits, 1, "ダッシュの体当たりが当たっていない");
        }

        [UnityTest]
        public IEnumerator Exit_LeavesArmorParked_AndRestoresBody()
        {
            yield return LoadAndBoard();
            Vector3 boardPos = player.transform.position;
            yield return Tap(pad.dpad.up, 0.3f);
            var cc = player.GetComponent<CharacterController>();
            float stationToPlayer = Vector3.Distance(Flat(station.transform.position), Flat(player.transform.position));
            Debug.Log($"[MatoiTest] armor exit: armored={armor.IsArmored} occupied={station.Occupied} profile={motor.Profile.name} height={cc.height} dist={stationToPlayer:0.00}");
            CloseUp("PA_parked", 30f);
            Assert.IsFalse(armor.IsArmored);
            Assert.IsFalse(station.Occupied);
            Assert.AreEqual("Move_Human", motor.Profile.name);
            Assert.Less(cc.height, 1.9f);
            Assert.That(stationToPlayer, Is.InRange(0.9f, 2.0f), "降りた位置がおかしい");
            Assert.Less(Vector3.Distance(Flat(station.transform.position), Flat(boardPos)), 0.3f, "アーマーが降りた場所に置かれていない");
        }

        [UnityTest]
        public IEnumerator ArmorBreak_EjectsPlayer()
        {
            yield return LoadAndBoard();
            health.TakeDamage(new DamageInfo { amount = 500f, direction = Vector3.forward });
            yield return null; yield return null;
            Debug.Log($"[MatoiTest] armor break: armored={armor.IsArmored} breaks={armor.BreakCount} integrity={station.Integrity:0} hp={health.Current} canReenter={station.CanEnter(player.transform.position)}");
            Assert.IsFalse(armor.IsArmored, "壊れても降ろされていない");
            Assert.AreEqual(1, armor.BreakCount);
            Assert.IsFalse(station.CanEnter(player.transform.position), "壊れたアーマーにすぐ乗れてしまう");
            Assert.IsFalse(health.IsDead, "装甲が壊れた瞬間に本人が死んでいる");
        }

        // ---------- 撮影 ----------
        void CloseUp(string name, float angle)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 30f;
            var p = player.transform.position;
            Vector3 dir = Quaternion.Euler(0, angle, 0) * player.transform.forward;
            go.transform.position = p + dir * 4.2f + Vector3.up * 2.0f;
            go.transform.LookAt(p + Vector3.up * 1.15f);
            Render(cam, name);
            Object.Destroy(go);
        }

        static void Shot(string name) => Render(Camera.main, name);

        static void Render(Camera cam, string name)
        {
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
