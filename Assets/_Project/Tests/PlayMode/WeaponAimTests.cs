// M5 動作確認：武器を持つ・しまう・構える（右スティック）・構えたまま移動・後ずさり。
using System.Collections;
using System.IO;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class WeaponAimTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;
        WeaponHolder weapons;
        PlayerController controller;
        Animator animator;

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
            weapons = player.GetComponent<WeaponHolder>();
            controller = player.GetComponent<PlayerController>();
            animator = player.GetComponentInChildren<Animator>();
            yield return new WaitForSeconds(0.5f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Angle(a, b); }

        Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

        [UnityTest]
        public IEnumerator Draw_PutsPistolInHand_AndHolsterPutsItOnThigh()
        {
            yield return LoadField();
            Assert.IsNotNull(weapons.WeaponInstance, "武器が生成されていない");
            Assert.Greater(weapons.WeaponInstance.Muzzle.localPosition.z + weapons.WeaponInstance.Muzzle.parent.localPosition.z, -1f);
            float holsterDist = Vector3.Distance(weapons.WeaponInstance.transform.position, Bone(HumanBodyBones.RightUpperLeg).position);

            Press(pad.buttonNorth); yield return null; Release(pad.buttonNorth);
            yield return new WaitForSeconds(0.6f);
            float handDist = Vector3.Distance(weapons.WeaponInstance.transform.position, Bone(HumanBodyBones.RightHand).position);
            float layer = animator.GetLayerWeight(animator.GetLayerIndex("UpperBody"));
            CloseUp("M5_drawn");

            Press(pad.buttonNorth); yield return null; Release(pad.buttonNorth);
            yield return new WaitForSeconds(0.6f);
            float holsterDist2 = Vector3.Distance(weapons.WeaponInstance.transform.position, Bone(HumanBodyBones.RightUpperLeg).position);
            Debug.Log($"[MatoiTest] draw: holsterDist={holsterDist:0.00} handDist={handDist:0.00} upperLayer={layer:0.00} holsterAgain={holsterDist2:0.00}");
            Assert.Less(holsterDist, 0.35f, "しまった時に太ももに無い");
            Assert.Less(handDist, 0.15f, "抜いた時に手に無い");
            Assert.Greater(layer, 0.95f, "上半身の構えアニメーションが有効になっていない");
            Assert.Less(holsterDist2, 0.35f);
            Assert.IsFalse(weapons.IsDrawn);
        }

        [UnityTest]
        public IEnumerator AimStick_TurnsAndPointsPistol()
        {
            yield return LoadField();
            Vector3 east = Vector3.right;
            Set(pad.rightStick, StickFor(east));
            yield return new WaitForSeconds(0.8f);
            float gunErr = Flat(weapons.WeaponInstance.transform.forward, east);
            float bodyErr = Flat(motor.transform.forward, east);
            CloseUp("M5_aim_idle");
            Shot("M5_aim_game");
            Debug.Log($"[MatoiTest] aim: drawn={weapons.IsDrawn} aiming={weapons.IsAiming} gunErr={gunErr:0.0} bodyErr={bodyErr:0.0} aimState={animator.GetCurrentAnimatorStateInfo(1).IsName("PistolAim")}");
            Assert.IsTrue(weapons.IsDrawn && weapons.IsAiming, "右スティックで構えになっていない");
            Assert.Less(gunErr, 3f, "銃が狙う方向を向いていない");
            Assert.Less(bodyErr, 10f, "止まって構えた時に体が狙う方向を向いていない");
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(1).IsName("PistolAim"));
        }

        [UnityTest]
        public IEnumerator AimWhileMoving_LegsFollowMovement_TorsoAims()
        {
            yield return LoadField();
            Vector3 aim = Vector3.right, move = Vector3.forward;
            Set(pad.rightStick, StickFor(aim));
            Set(pad.leftStick, StickFor(move));
            yield return new WaitForSeconds(1.2f);
            float gunErr = Flat(weapons.WeaponInstance.transform.forward, aim);
            float legsErr = Flat(motor.transform.forward, move);
            Vector3 chestFwd = Vector3.ProjectOnPlane(Bone(HumanBodyBones.UpperChest).position - Bone(HumanBodyBones.Hips).position, Vector3.up);
            float expected = motor.Profile.jogSpeed * motor.Profile.aimMoveSpeedFactor;
            CloseUp("M5_aim_strafe");
            Shot("M5_aim_strafe_game");
            Debug.Log($"[MatoiTest] strafe: speed={motor.Speed:0.00} expected={expected:0.00} gunErr={gunErr:0.0} legsErr={legsErr:0.0} twist={weapons.Twist:0.0} sprinting={motor.IsSprinting}");
            Assert.Less(gunErr, 3f);
            Assert.Less(legsErr, 12f, "構え移動中、脚が移動方向を向いていない");
            Assert.Greater(Mathf.Abs(weapons.Twist), 60f, "上半身がひねられていない");
            Assert.AreEqual(expected, motor.Speed, 0.35f, "構え中の移動速度が想定外");
        }

        [UnityTest]
        public IEnumerator AimBackward_Backpedals()
        {
            yield return LoadField();
            Vector3 aim = Vector3.forward, move = Vector3.back;
            Set(pad.rightStick, StickFor(aim));
            yield return new WaitForSeconds(0.4f);
            Set(pad.leftStick, StickFor(move));
            yield return new WaitForSeconds(1.0f);
            float bodyErr = Flat(motor.transform.forward, aim);
            Vector3 v = motor.Velocity; v.y = 0;
            float moveErr = Vector3.Angle(v, move);
            float speedParam = animator.GetFloat("Speed");
            CloseUp("M5_backpedal");
            Debug.Log($"[MatoiTest] backpedal: backpedal={controller.IsBackpedaling} bodyErr={bodyErr:0.0} moveErr={moveErr:0.0} fwdSpeed={motor.ForwardSpeed:0.00} animSpeedParam={speedParam:0.00}");
            Assert.IsTrue(controller.IsBackpedaling);
            Assert.Less(bodyErr, 12f, "後ずさり中、体が狙う方向を向いていない");
            Assert.Less(moveErr, 10f, "後ろへ進んでいない");
            Assert.Less(speedParam, -0.5f, "後退アニメーション（逆再生）になっていない");
        }

        [UnityTest]
        public IEnumerator Sprint_IsDisabledWhileAiming()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.forward));
            Press(pad.leftStickButton); yield return null; Release(pad.leftStickButton);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(motor.IsSprinting, "前提：ダッシュ中");
            Set(pad.rightStick, StickFor(Vector3.forward));
            yield return new WaitForSeconds(0.8f);
            Debug.Log($"[MatoiTest] sprint+aim: sprinting={motor.IsSprinting} speed={motor.Speed:0.00}");
            Assert.IsFalse(motor.IsSprinting, "構え中にダッシュできてしまう");
        }

        // ---------- 撮影 ----------
        void CloseUp(string name)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 28f;
            var p = motor.transform.position;
            Vector3 view = weapons.AimDirection;
            go.transform.position = p + Quaternion.Euler(0, 40, 0) * view * 3.6f + Vector3.up * 1.9f;
            go.transform.LookAt(p + Vector3.up * 1.1f);
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
