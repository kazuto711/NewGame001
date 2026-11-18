// 第6マイルストーン：武器ごとの構え・発砲・リロードと、攻撃中に腕が銃に固定されないことを確認する。
using System.Collections;
using System.IO;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class WeaponStanceTests : InputTestFixture
    {
        const int UpperLayer = 1;
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;
        WeaponHolder holder;
        WeaponShooter shooter;
        Animator animator;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            var player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            holder = player.GetComponent<WeaponHolder>();
            shooter = player.GetComponent<WeaponShooter>();
            animator = player.GetComponentInChildren<Animator>();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 270f);
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator Tap(ButtonControl button, float after = 0.15f)
        {
            Press(button);
            yield return null;
            yield return null;
            Release(button);
            yield return new WaitForSeconds(after);
        }

        IEnumerator DrawRifle()
        {
            yield return Tap(pad.rightShoulder, 0.6f);
            Assert.AreEqual(WeaponStance.Rifle, holder.CurrentWeapon.stance, "前提：ライフルを持っている");
        }

        void AimWest()
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * Vector3.left;
            Set(pad.rightStick, new Vector2(l.x, l.z));
        }

        bool Upper(string state) => animator.GetCurrentAnimatorStateInfo(UpperLayer).IsName(state)
                                    || animator.GetNextAnimatorStateInfo(UpperLayer).IsName(state);

        IEnumerator WaitUpper(string state, float seconds, System.Action<bool> result)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (Upper(state)) { result(true); yield break; }
                yield return null;
            }
            result(false);
        }

        [UnityTest]
        public IEnumerator Rifle_UsesItsOwnHoldAndAimStates()
        {
            yield return LoadField();
            yield return DrawRifle();
            Assert.IsTrue(Upper("RifleHold"), "ライフルの待機になっていない");
            Shot("Gun_rifle_hold");

            AimWest();
            bool aimed = false;
            yield return WaitUpper("RifleAim", 0.8f, r => aimed = r);
            yield return new WaitForSeconds(0.4f);
            Shot("Gun_rifle_aim");
            Assert.IsTrue(aimed, "ライフルの構えになっていない");
        }

        [UnityTest]
        public IEnumerator RifleAim_StockRestsOnShoulder_AndDoesNotPierceTheBack()
        {
            yield return LoadField();
            yield return DrawRifle();
            AimWest();
            yield return new WaitForSeconds(1f);

            // 銃身の方向で見て、銃のいちばん後ろ（銃床の端）が肩の関節よりどれだけ前にあるか
            var w = holder.WeaponInstance.transform;
            Vector3 shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            float rear = float.MaxValue;
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>())
            {
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                    rear = Mathf.Min(rear, Vector3.Dot(mf.transform.TransformPoint(corner) - shoulder, w.forward));
                }
            }
            Debug.Log($"[MatoiTest] rifle stock: rearFromShoulder={rear:0.000}");
            Assert.Greater(rear, -0.03f, "銃床が肩より後ろに抜けて、背中から飛び出している");
            Assert.Less(rear, 0.12f, "銃床が肩から離れすぎている");
        }

        [UnityTest]
        public IEnumerator RifleReload_KeepsGunInRightHand_SwapsMagazine_AndMatchesReloadTime()
        {
            yield return LoadField();
            yield return DrawRifle();
            AimWest();
            yield return new WaitForSeconds(0.6f);
            yield return Tap(pad.rightTrigger, 0.2f);
            int before = shooter.AmmoInMagazine;
            Set(pad.rightStick, Vector2.zero);
            yield return new WaitForSeconds(0.4f);

            Press(pad.buttonWest);
            yield return null;
            yield return null;
            Release(pad.buttonWest);
            float started = Time.time;
            var w = holder.WeaponInstance;
            Assert.IsNotNull(w.Magazine, "ライフルの弾倉が別部品になっていない");
            Vector3 restLocal = w.Magazine.localPosition;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            float maxRightErr = 0f, maxHeadTilt = 0f, maxMagTravel = 0f;
            bool wasOut = false;
            int shotIndex = 0;
            // 顔の向きは、リロード前の姿勢からどれだけ変わったかで見る（骨の軸はモデルごとに違う）
            Quaternion headBefore = Quaternion.Inverse(motor.transform.rotation) * head.rotation;
            float[] shotTimes = { 0.2f, 0.35f, 0.5f, 0.75f };

            yield return null;
            while (shooter.IsReloading && Time.time < started + 5f)
            {
                float rightErr = Vector3.Distance(rightHand.position,
                    w.transform.TransformPoint(holder.CurrentWeapon.rightWristOffset / holder.CurrentWeapon.visualScale));
                maxRightErr = Mathf.Max(maxRightErr, rightErr);
                maxHeadTilt = Mathf.Max(maxHeadTilt, Quaternion.Angle(headBefore, Quaternion.Inverse(motor.transform.rotation) * head.rotation));
                maxMagTravel = Mathf.Max(maxMagTravel, Vector3.Distance(w.Magazine.localPosition, restLocal));
                wasOut |= !holder.MagazineInWell;
                if (shotIndex < shotTimes.Length && shooter.ReloadProgress >= shotTimes[shotIndex])
                    Shot($"Gun_rifle_reload_{++shotIndex}");
                yield return null;
            }
            float took = Time.time - started;
            int dropped = 0;
            foreach (var go in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (go.name == "DroppedMagazine") dropped++;
            Debug.Log($"[MatoiTest] rifle reload: rightErr={maxRightErr:0.000} headTilt={maxHeadTilt:0.0} magTravel={maxMagTravel:0.00} wasOut={wasOut} dropped={dropped} took={took:0.00}s ammo={before}->{shooter.AmmoInMagazine}");
            Assert.Less(maxRightErr, 0.06f, "リロード中に右手が銃のグリップから離れている");
            Assert.Less(maxHeadTilt, 20f, "リロード中に顔の向きが大きく変わっている");
            Assert.IsTrue(wasOut, "弾倉が銃から外れていない");
            Assert.Greater(maxMagTravel, 0.15f, "弾倉が動いていない");
            Assert.AreEqual(1, dropped, "空の弾倉が 1 個落ちていない");
            Assert.AreEqual(holder.CurrentWeapon.reloadTime, took, 0.2f, "リロード時間が武器の設定と違う");
            Assert.AreEqual(holder.CurrentWeapon.magazineSize, shooter.AmmoInMagazine, "弾が補充されていない");

            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(holder.MagazineInWell, "リロードの後、弾倉が銃に戻っていない");
            Assert.Less(Vector3.Distance(w.Magazine.localPosition, restLocal), 0.001f, "弾倉が元の位置に戻っていない");
            float leftErrAfter = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                w.SupportHand.position + w.transform.rotation * holder.CurrentWeapon.leftWristOffset);
            Assert.Less(leftErrAfter, 0.06f, "リロードの後、左手が銃に戻っていない");
        }

        [UnityTest]
        public IEnumerator Melee_WithRifleDrawn_ShovesWithBothHands_ThenReturnsToGrip()
        {
            yield return LoadField();
            yield return DrawRifle();
            yield return new WaitForSeconds(0.3f);
            var w = holder.WeaponInstance;
            float holdForward = motor.transform.InverseTransformPoint(w.transform.position).z;

            Press(pad.buttonEast);
            yield return null;
            yield return null;
            Release(pad.buttonEast);

            float maxForward = holdForward, nearestToHead = 9f, maxLeftErr = 0f, maxSling = 0f;
            bool shot = false;
            float end = Time.time + 0.5f;
            while (Time.time < end)
            {
                maxForward = Mathf.Max(maxForward, motor.transform.InverseTransformPoint(w.transform.position).z);
                maxSling = Mathf.Max(maxSling, holder.SlingBlend);
                maxLeftErr = Mathf.Max(maxLeftErr, Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                    w.SupportHand.position + w.transform.rotation * holder.CurrentWeapon.leftWristOffset));
                // 突き出している間だけ頭との距離を見る（普段の構えは銃床がほおの近くに来るのが正しい）
                float progress = motor.GetComponent<MeleeAttack>().Progress;
                if (progress >= 0.2f && progress <= 0.7f)
                {
                    Vector3 head = animator.GetBoneTransform(HumanBodyBones.Head).position;
                    Vector3 grip = w.transform.position, muzzle = w.Muzzle.position;
                    for (float t = -0.3f; t <= 1f; t += 0.1f)
                        nearestToHead = Mathf.Min(nearestToHead, Vector3.Distance(Vector3.LerpUnclamped(grip, muzzle, t), head));
                }
                if (!shot && Time.time > end - 0.34f) { Shot("Gun_rifle_melee"); shot = true; }
                yield return null;
            }

            yield return new WaitForSeconds(0.7f);
            float leftErr = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                w.SupportHand.position + w.transform.rotation * holder.CurrentWeapon.leftWristOffset);
            Debug.Log($"[MatoiTest] rifle melee: thrust={maxForward - holdForward:0.00} nearestToHead={nearestToHead:0.00} maxLeftErr={maxLeftErr:0.000} sling={maxSling:0.00} leftErrAfter={leftErr:0.000}");
            Assert.Greater(maxForward - holdForward, 0.1f, "ライフルを前へ突き出していない");
            Assert.Greater(nearestToHead, 0.2f, "近接攻撃中にライフルが頭を貫通している");
            Assert.Less(maxLeftErr, 0.08f, "近接攻撃中に左手が銃から離れている");
            Assert.AreEqual(0f, maxSling, 0.01f, "近接攻撃でライフルが背中へ移動している");
            Assert.Less(leftErr, 0.06f, "近接攻撃の後、左手が銃に戻っていない");
        }

        [UnityTest]
        public IEnumerator Pistol_StaysInsideTheHand_WhileAiming_AndHolsterClearsTheHand()
        {
            yield return LoadField();
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var pistol = holder.WeaponInstance;
            Vector3 gripLocal = new Vector3(0f, -0.05f, -0.02f);
            float holsterGap = Vector3.Distance(rightHand.position, pistol.transform.TransformPoint(gripLocal));

            AimWest();
            yield return new WaitForSeconds(1.0f);
            float wristErr = Vector3.Distance(rightHand.position,
                pistol.transform.position + pistol.transform.rotation * holder.CurrentWeapon.pistolWristLocal);
            float palmToGrip = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position,
                pistol.transform.TransformPoint(gripLocal));
            float gunErr = Vector3.Angle(Vector3.ProjectOnPlane(pistol.transform.forward, Vector3.up), holder.AimDirection);
            Debug.Log($"[MatoiTest] pistol grip: scale={pistol.transform.localScale.x:0.00} holsterGap={holsterGap:0.00} wristErr={wristErr:0.000} palmToGrip={palmToGrip:0.00} gunErr={gunErr:0.0}");
            Assert.AreEqual(1f, pistol.transform.localScale.x, 0.001f, "拳銃が実寸ではない");
            Assert.Greater(holsterGap, 0.12f, "しまった拳銃が右手と重なっている");
            Assert.Less(wristErr, 0.03f, "右手首がグリップの位置にない");
            Assert.Less(palmToGrip, 0.09f, "グリップが手のひらの中にない");
            Assert.Less(gunErr, 3f, "拳銃が狙う方向を向いていない");
        }

        [UnityTest]
        public IEnumerator Pistol_PlaysFireAnimation_WhenShooting()
        {
            yield return LoadField();
            AimWest();
            yield return new WaitForSeconds(0.7f);
            Press(pad.rightTrigger);
            bool played = false;
            yield return WaitUpper("PistolFire", 0.4f, r => played = r);
            Release(pad.rightTrigger);
            Shot("Gun_pistol_fire");
            Assert.IsTrue(played, "拳銃の発砲アニメーションが再生されていない");
        }

        void Shot(string name)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 34f;
            Vector3 p = motor.transform.position + Vector3.up * 1.15f;
            go.transform.position = p + Quaternion.Euler(0, 60, 0) * motor.transform.forward * 2.8f + Vector3.up * 0.35f;
            go.transform.LookAt(p);
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
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
