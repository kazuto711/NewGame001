// M3（カメラ）と主人公アニメーションの動作確認。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.CameraSystem;
using Matoi.Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class CameraAndAnimationTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;
        Animator animator;
        PlayerCameraController camCtrl;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            // このテストは見下ろし視点を前提にしている（三人称視点は ThirdPersonCameraTests で確認）
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            var player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            animator = player.GetComponentInChildren<Animator>();
            camCtrl = Object.FindAnyObjectByType<PlayerCameraController>();
            yield return new WaitForSeconds(0.5f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        float HandHeight(HumanBodyBones b) =>
            animator.GetBoneTransform(b).position.y - motor.transform.position.y;

        [UnityTest]
        public IEnumerator Hero_AnimatesWithSpeed()
        {
            yield return LoadField();
            Assert.IsTrue(animator.avatar.isValid && animator.avatar.isHuman, "Humanoid アバターが無効");
            yield return new WaitForSeconds(0.5f);
            float idleHand = HandHeight(HumanBodyBones.LeftHand);
            CloseUp("M3_hero_idle");

            Set(pad.leftStick, StickFor(Vector3.right));
            yield return new WaitForSeconds(1.2f);
            float animSpeed = animator.GetFloat("Speed");
            CloseUp("M3_hero_jog");
            Debug.Log($"[MatoiTest] anim: idleHandY={idleHand:0.00} speedParam={animSpeed:0.00} motor={motor.Speed:0.00}");
            Assert.Less(idleHand, 1.25f, "待機中に T ポーズのまま（アニメーションが再生されていない）");
            Assert.AreEqual(motor.Speed, animSpeed, 0.4f, "アニメーションの速度パラメータが移動速度に追従していない");

            Press(pad.leftStickButton); yield return null; Release(pad.leftStickButton);
            yield return new WaitForSeconds(1.2f);
            CloseUp("M3_hero_sprint");
            Set(pad.leftStick, Vector2.zero);
        }

        [UnityTest]
        public IEnumerator Camera_LooksAheadInMoveDirection()
        {
            yield return LoadField();
            Vector3 dir = Vector3.right;
            Set(pad.leftStick, StickFor(dir));
            yield return new WaitForSeconds(1.5f);
            var cam = Camera.main.transform;
            // 画面中央が指している地面の点と、プレイヤーとのずれ
            var plane = new Plane(Vector3.up, motor.transform.position + Vector3.up);
            plane.Raycast(new Ray(cam.position, cam.forward), out float t);
            Vector3 center = cam.position + cam.forward * t;
            float ahead = Vector3.Dot(center - motor.transform.position, dir);
            Debug.Log($"[MatoiTest] lookahead: {ahead:0.00} m ahead");
            Shot("M3_lookahead");
            Assert.Greater(ahead, 0.8f, "進行方向の先が映っていない");
        }

        [UnityTest]
        public IEnumerator Camera_ZoomsOutWhileSprinting_AndForPowerArmor()
        {
            yield return LoadField();
            float baseD = camCtrl.CurrentDistance;
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(0.3f);
            Press(pad.leftStickButton); yield return null; Release(pad.leftStickButton);
            yield return new WaitForSeconds(2.0f);
            float sprintD = camCtrl.CurrentDistance;
            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(2.0f);
            float backD = camCtrl.CurrentDistance;

            Press(pad.selectButton); yield return null; Release(pad.selectButton);
            yield return new WaitForSeconds(2.5f);
            float armorD = camCtrl.CurrentDistance;
            Debug.Log($"[MatoiTest] zoom: base={baseD:0.0} sprint={sprintD:0.0} back={backD:0.0} armor={armorD:0.0}");
            Assert.Greater(sprintD, baseD + 1.5f, "ダッシュ中にカメラが引いていない");
            Assert.AreEqual(baseD, backD, 0.6f, "止まった後に元の距離へ戻っていない");
            Assert.Greater(armorD, baseD + 3f, "パワーアーマー時にカメラが引いていない");
        }

        [UnityTest]
        public IEnumerator Occlusion_HidesHouseBehindPlayer()
        {
            yield return LoadField();
            var house = CameraOccluder.All.First(o => o.name.StartsWith("House_14_-2"));
            // カメラから見て家の真裏（北東側）に立つ
            Vector3 flatFwd = Quaternion.Euler(0, CamYaw, 0) * Vector3.forward;
            // 家の大きさに合わせて、家のすぐ裏（カメラの反対側）に立つ
            var hb = house.Bounds;
            float reach = Mathf.Abs(hb.extents.x * flatFwd.x) + Mathf.Abs(hb.extents.z * flatFwd.z) + 1.2f;
            Vector3 spot = new Vector3(hb.center.x, 0.1f, hb.center.z) + flatFwd * reach;
            motor.Teleport(spot, 0f);
            yield return new WaitForSeconds(2.0f);
            bool hiddenBehind = house.IsHidden;
            Shot("M3_occlusion");

            motor.Teleport(new Vector3(1.5f, 0.1f, -16f), 0f);
            yield return new WaitForSeconds(2.0f);
            bool hiddenAway = house.IsHidden;
            Debug.Log($"[MatoiTest] occlusion: behind={hiddenBehind} away={hiddenAway} at {spot}");
            Assert.IsTrue(hiddenBehind, "プレイヤーを隠している家が透けていない");
            Assert.IsFalse(hiddenAway, "離れても家が透けたまま");
        }

        [UnityTest]
        public IEnumerator Shake_MovesCameraBriefly()
        {
            yield return LoadField();
            yield return new WaitForSeconds(1.0f);
            var cam = Camera.main.transform;
            Vector3 rest = cam.position;
            CameraShake.Shake(1f);
            float maxDev = 0f, tt = 0f;
            while (tt < 0.4f) { maxDev = Mathf.Max(maxDev, (cam.position - rest).magnitude); tt += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.0f);
            float after = (cam.position - rest).magnitude;
            Debug.Log($"[MatoiTest] shake: maxDev={maxDev:0.000} after={after:0.000}");
            Assert.Greater(maxDev, 0.05f, "画面が揺れていない");
            Assert.Less(after, 0.02f, "揺れが収まっていない");
        }

        // ---------- 撮影 ----------
        void CloseUp(string name)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 28f;
            var p = motor.transform.position;
            go.transform.position = p + motor.transform.rotation * new Vector3(3.2f, 1.7f, 2.4f);
            go.transform.LookAt(p + Vector3.up * 0.95f);
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
