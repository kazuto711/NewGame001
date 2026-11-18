// 歩きの安定（腰のぶれ・上半身の揺れ）、ライフルの握り、主人公の選択を確認する。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Equipment;
using Matoi.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class PlayerPresentationTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Animator animator;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            animator = player.GetComponentInChildren<Animator>();
            yield return new WaitForSeconds(0.3f);
        }

        [TearDown]
        public void ClearChoice()
        {
            PlayerPrefs.DeleteKey(PlayerModelSelector.PrefKey);
            Time.timeScale = 1f;
        }

        static Vector2 StickFor(Vector3 worldDir, float amount)
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized * amount;
            return new Vector2(l.x, l.z);
        }

        struct Sway { public float hip, roll, yaw, tilt, foot; }

        /// <summary>まっすぐ歩いている間の、体のぶれの大きさ（最大−最小）を測る。</summary>
        IEnumerator MeasureWalk(Vector3 dir, System.Action<Sway> done)
        {
            Set(pad.leftStick, StickFor(dir, 0.4f));
            yield return new WaitForSeconds(0.8f);

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Transform l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            float[] min = { 9f, 999f, 999f, 999f, 9f }, max = { -9f, -999f, -999f, -999f, -9f };
            float end = Time.time + 2.4f;
            while (Time.time < end)
            {
                yield return null;   // 次のフレームの先頭＝前のフレームの最終姿勢を読む
                Transform t = motor.transform;
                float[] v =
                {
                    Vector3.Dot(hips.position - t.position, t.right),
                    Vector3.SignedAngle(t.up, Vector3.ProjectOnPlane(neck.position - hips.position, t.forward), t.forward),
                    Vector3.SignedAngle(t.right, Vector3.ProjectOnPlane(r.position - l.position, t.up), t.up),
                    Vector3.SignedAngle(t.right, Vector3.ProjectOnPlane(r.position - l.position, t.forward), t.forward),
                    Vector3.Dot(foot.position - t.position, t.right),
                };
                for (int i = 0; i < v.Length; i++) { min[i] = Mathf.Min(min[i], v[i]); max[i] = Mathf.Max(max[i], v[i]); }
            }
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.5f);
            done(new Sway { hip = max[0] - min[0], roll = max[1] - min[1], yaw = max[2] - min[2], tilt = max[3] - min[3], foot = max[4] - min[4] });
        }

        [UnityTest]
        public IEnumerator Walk_KeepsHipsAndUpperBodySteady_WithoutMovingTheFeet()
        {
            yield return LoadField();
            var stabilizer = player.GetComponent<LocomotionStabilizer>();
            Assert.IsNotNull(stabilizer, "歩きの安定の部品が付いていない");

            motor.Teleport(new Vector3(1.5f, 0.1f, -8f), 0f);
            yield return new WaitForSeconds(0.2f);
            stabilizer.enabled = false;
            Sway raw = default, steady = default;
            yield return MeasureWalk(Vector3.forward, s => raw = s);

            motor.Teleport(new Vector3(1.5f, 0.1f, -8f), 0f);
            yield return new WaitForSeconds(0.2f);
            stabilizer.enabled = true;
            yield return MeasureWalk(Vector3.forward, s => steady = s);

            Debug.Log($"[MatoiTest] walk sway raw hip={raw.hip:0.000} roll={raw.roll:0.0} yaw={raw.yaw:0.0} tilt={raw.tilt:0.0} foot={raw.foot:0.000} / " +
                      $"steady hip={steady.hip:0.000} roll={steady.roll:0.0} yaw={steady.yaw:0.0} tilt={steady.tilt:0.0} foot={steady.foot:0.000}");
            Assert.Less(steady.hip, raw.hip * 0.6f, "腰の左右ぶれが減っていない");
            Assert.Less(steady.roll, raw.roll * 0.6f, "上半身の左右の傾きが減っていない");
            Assert.Less(steady.yaw, raw.yaw * 0.6f, "肩のひねりが減っていない");
            Assert.Less(steady.tilt, 1.5f, "肩の高さが揺れている");   // 元から小さい揺れなので、割合ではなく角度で見る
            Assert.Less(Mathf.Abs(steady.foot - raw.foot), 0.03f, "足の運びが変わってしまった");
        }

        [UnityTest]
        public IEnumerator RifleGrip_BothHandsCloseAroundTheGun()
        {
            yield return LoadField();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 270f);
            var holder = player.GetComponent<WeaponHolder>();
            Press(pad.rightShoulder); yield return null; yield return null; Release(pad.rightShoulder);
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(WeaponStance.Rifle, holder.CurrentWeapon.stance);
            var l = Quaternion.Euler(0, -CamYaw, 0) * Vector3.left;
            Set(pad.rightStick, new Vector2(l.x, l.z));
            yield return new WaitForSeconds(1f);

            yield return null;   // 次のフレームの先頭＝前のフレームの最終姿勢を読む
            var w = holder.WeaponInstance.transform;
            Transform rTip = animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
            Transform rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lTip = animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal);
            float rGrip = Vector3.Distance(rTip.position, w.position);
            float lGrip = Vector3.Distance(lTip.position, holder.WeaponInstance.SupportHand.position);
            Debug.Log($"[MatoiTest] rifle grip: rightFingerToGrip={rGrip:0.000} leftFingerToSupport={lGrip:0.000} wrist={Vector3.Distance(rHand.position, w.position):0.000}");

            Vector3 c = w.position;
            Shot("Grip_right_side", c + w.right * 0.55f + w.up * 0.12f, c);
            Shot("Grip_left_side", c - w.right * 0.5f + w.forward * 0.25f + w.up * 0.05f, c + w.forward * 0.25f);
            Shot("Grip_under", c - w.up * 0.5f + w.forward * 0.15f + w.right * 0.15f, c + w.forward * 0.15f);
            Shot("Grip_body", c + w.right * 1.5f + w.forward * 0.7f + w.up * 0.35f, c + Vector3.up * 0.1f, 40f);
            Assert.Less(rGrip, 0.1f, "右手の指がグリップを握っていない");
            Assert.Less(lGrip, 0.14f, "左手の指がハンドガードに届いていない");
        }

        [UnityTest]
        public IEnumerator SavedChoice_LoadsThatModelOnly_AndItWorks()
        {
            PlayerPrefs.SetString(PlayerModelSelector.PrefKey, "survival");
            yield return LoadField();
            var selector = player.GetComponent<PlayerModelSelector>();
            Assert.AreEqual("survival", selector.Current.id, "保存した主人公が選ばれていない");
            yield return null;

            var animators = player.GetComponentsInChildren<Animator>(true);
            Assert.AreEqual(1, animators.Length, "使わないモデルが残っている");
            Assert.IsTrue(animators[0].isHuman);
            var armor = player.GetComponent<PowerArmorUser>();
            Assert.IsNotNull(armor);
            Assert.AreEqual(16, player.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("PA_") && t.parent.GetComponent<Renderer>() == null && IsPiece(t)), "装甲部品の数が違う");

            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            Set(pad.leftStick, StickFor(Vector3.forward, 1f));
            yield return new WaitForSeconds(0.8f);
            Set(pad.leftStick, Vector2.zero);
            Assert.Greater(animators[0].GetFloat("Speed") + motor.Speed, 0.5f, "選んだモデルが動いていない");
        }

        static bool IsPiece(Transform t) => !t.parent.name.StartsWith("PA_");

        [UnityTest]
        public IEnumerator SelectionScreen_PausesAndClosesCleanly()
        {
            yield return LoadField();
            var selector = player.GetComponent<PlayerModelSelector>();
            Assert.GreaterOrEqual(selector.variants.Length, 2, "選べる主人公が 2 体以上ない");
            selector.Open();
            Assert.IsTrue(selector.IsOpen);
            Assert.AreEqual(0f, Time.timeScale, "選択中にゲームが止まっていない");
            selector.Close();
            Assert.IsFalse(selector.IsOpen);
            Assert.AreEqual(1f, Time.timeScale, "閉じた後にゲームが動き出さない");
        }

        void Shot(string name, Vector3 eye, Vector3 target, float fov = 30f)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.03f;
            go.transform.position = eye;
            go.transform.LookAt(target);
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(go);
        }
    }
}
