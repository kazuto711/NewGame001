// M6 動作確認：射撃（単発・連射）・弾数・リロード・ライフルの両手持ち・近接攻撃。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class CombatTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;
        WeaponHolder holder;
        WeaponShooter shooter;
        MeleeAttack melee;
        Animator animator;
        TargetDummy target;

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
            holder = player.GetComponent<WeaponHolder>();
            shooter = player.GetComponent<WeaponShooter>();
            melee = player.GetComponent<MeleeAttack>();
            animator = player.GetComponentInChildren<Animator>();
            target = Object.FindObjectsByType<TargetDummy>(FindObjectsSortMode.None)
                .OrderBy(t => Vector3.Distance(t.transform.position, motor.transform.position)).First();
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.5f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        IEnumerator AimAtTarget()
        {
            Vector3 d = target.transform.position - motor.transform.position; d.y = 0;
            Set(pad.rightStick, StickFor(d));
            yield return new WaitForSeconds(0.6f);
        }

        IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl b, float after = 0.25f)
        {
            Press(b); yield return null; yield return null; Release(b);
            yield return new WaitForSeconds(after);
        }

        [UnityTest]
        public IEnumerator Pistol_SemiAuto_HitsTarget_AndUsesAmmo()
        {
            yield return LoadField();
            yield return AimAtTarget();
            var pw = holder.WeaponInstance;
            float pistolAhead = Vector3.Dot(pw.Muzzle.position - pw.transform.position, holder.AimDirection);
            Debug.Log($"[MatoiTest] pistol orientation: muzzleAhead={pistolAhead:0.00}");
            Assert.Greater(pistolAhead, 0.1f, "拳銃の銃口が前を向いていない");
            SideView("M6_pistol_side");
            Press(pad.rightTrigger);
            yield return null; yield return null;
            Shot("M6_pistol_fire");
            yield return new WaitForSeconds(0.6f);
            Release(pad.rightTrigger);
            int afterHold = shooter.ShotsFired;
            yield return new WaitForSeconds(0.25f);
            for (int i = 0; i < 3; i++) yield return Tap(pad.rightTrigger);
            Debug.Log($"[MatoiTest] pistol: afterHold={afterHold} total={shooter.ShotsFired} ammo={shooter.AmmoInMagazine} targetHP={target.Health.Current}");
            Assert.AreEqual(1, afterHold, "拳銃（単発）が押しっぱなしで連射されている");
            Assert.AreEqual(4, shooter.ShotsFired);
            Assert.AreEqual(holder.CurrentWeapon.magazineSize - 4, shooter.AmmoInMagazine, "弾数が減っていない");
            Assert.Less(target.Health.Current, 100f - 3 * holder.CurrentWeapon.damage + 1f, "的に当たっていない");
        }

        [UnityTest]
        public IEnumerator Reload_RefillsMagazine()
        {
            yield return LoadField();
            yield return AimAtTarget();
            for (int i = 0; i < 3; i++) yield return Tap(pad.rightTrigger);
            int before = shooter.AmmoInMagazine;
            yield return Tap(pad.buttonWest, 0.1f);
            bool reloading = shooter.IsReloading;
            yield return new WaitForSeconds(holder.CurrentWeapon.reloadTime + 0.3f);
            Debug.Log($"[MatoiTest] reload: before={before} reloading={reloading} after={shooter.AmmoInMagazine}");
            Assert.IsTrue(reloading, "リロードが始まっていない");
            Assert.AreEqual(holder.CurrentWeapon.magazineSize, shooter.AmmoInMagazine, "リロード後に弾が補充されていない");
        }

        [UnityTest]
        public IEnumerator Rifle_HeldWithBothHands_AndFiresAutomatically()
        {
            yield return LoadField();
            yield return Tap(pad.rightShoulder, 0.3f);
            Assert.AreEqual(WeaponStance.Rifle, holder.CurrentWeapon.stance, "ライフルに持ち替わっていない");
            yield return AimAtTarget();
            var w = holder.WeaponInstance;
            float rightHand = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightHand).position,
                w.transform.TransformPoint(holder.CurrentWeapon.rightWristOffset / holder.CurrentWeapon.visualScale));
            float leftHand = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                w.SupportHand.position + w.transform.rotation * holder.CurrentWeapon.leftWristOffset);
            Vector3 aimFlat = holder.AimDirection;
            float gunErr = Vector3.Angle(Vector3.ProjectOnPlane(w.transform.forward, Vector3.up), aimFlat);
            // 銃口がグリップより狙う方向の前にあるか（逆向きに持っていないか）
            float muzzleAhead = Vector3.Dot(w.Muzzle.position - w.transform.position, aimFlat);
            float shoulderBehind = Vector3.Dot(animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position - w.transform.position, aimFlat);
            Debug.Log($"[MatoiTest] rifle orientation: muzzleAhead={muzzleAhead:0.00} shoulderBehindGrip={shoulderBehind:0.00}");
            Assert.Greater(muzzleAhead, 0.5f, "銃口が前を向いていない");
            CloseUp("M6_rifle_aim");
            SideView("M6_rifle_side");

            Press(pad.rightTrigger);
            yield return new WaitForSeconds(0.25f);
            Shot("M6_rifle_fire");
            CloseUp("M6_rifle_fire_close");
            yield return new WaitForSeconds(0.75f);
            Release(pad.rightTrigger);
            int shots = shooter.ShotsFired;
            Debug.Log($"[MatoiTest] rifle: rightHandErr={rightHand:0.000} leftHandErr={leftHand:0.000} gunErr={gunErr:0.0} shots1s={shots} ammo={shooter.AmmoInMagazine} targetDead={target.Health.IsDead}");
            Assert.Less(rightHand, 0.06f, "右手が銃のグリップに届いていない");
            Assert.Less(leftHand, 0.06f, "左手が銃に添えられていない");
            Assert.Less(gunErr, 4f);
            float rate = holder.CurrentWeapon.fireRate;  // 1秒間押し続けた時の発射数（データの値から期待値を出す）
            Assert.That(shots, Is.InRange(Mathf.FloorToInt(rate * 0.7f), Mathf.CeilToInt(rate * 1.2f)), "連射速度が想定外");
            Assert.IsTrue(target.Health.IsDead, "連射で的を倒せていない");
        }

        [UnityTest]
        public IEnumerator Melee_HitsTargetInFront()
        {
            yield return LoadField();
            Vector3 tp = target.transform.position;
            motor.Teleport(tp + new Vector3(0, 0.05f, -1.5f), 0f);
            yield return new WaitForSeconds(0.3f);
            yield return Tap(pad.buttonEast, 0.0f);
            yield return new WaitForSeconds(0.2f);
            CloseUp("M6_melee");
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[MatoiTest] melee: hits={melee.HitCount} targetHP={target.Health.Current}");
            Assert.AreEqual(1, melee.HitCount, "近接攻撃が的に当たっていない");
            Assert.AreEqual(100f - melee.damage, target.Health.Current, 0.01f);
        }

        // ---------- 撮影 ----------
        void CloseUp(string name)
        {
            var go = new GameObject("TempCloseUp");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 28f;
            var p = motor.transform.position;
            Vector3 view = holder.IsAiming ? holder.AimDirection : motor.transform.forward;
            go.transform.position = p + Quaternion.Euler(0, 50, 0) * view * 3.4f + Vector3.up * 1.8f;
            go.transform.LookAt(p + Vector3.up * 1.2f);
            Render(cam, name);
            Object.Destroy(go);
        }

        /// <summary>狙う方向に対して真横から撮る（構えの形の確認用）。</summary>
        void SideView(string name)
        {
            var go = new GameObject("TempSide");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 26f;
            var p = motor.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, holder.AimDirection);
            go.transform.position = p + side * 3.6f + Vector3.up * 1.4f + holder.AimDirection * 0.3f;
            go.transform.LookAt(p + Vector3.up * 1.25f + holder.AimDirection * 0.3f);
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
