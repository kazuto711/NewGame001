// 三人称 段階2（構えと射撃）の動作確認：
// 照準マーク・画面中央の先へ撃つ（上下も）・照準で敵を倒す・構え中の感度・撃った時のカメラの跳ね上がり・
// 構えたままの移動・パワーアーマーの構えカメラ・見下ろし視点は従来どおり。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.AI;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Equipment;
using Matoi.Player;
using Matoi.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class ThirdPersonCombatTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        PlayerController controller;
        WeaponHolder holder;
        WeaponShooter shooter;
        Crosshair crosshair;
        ThirdPersonCameraController tp;
        Transform cam;

        IEnumerator Load()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            controller = player.GetComponent<PlayerController>();
            holder = player.GetComponent<WeaponHolder>();
            shooter = player.GetComponent<WeaponShooter>();
            crosshair = player.GetComponent<Crosshair>();
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.ThirdPerson);
            tp = ThirdPersonCameraController.Instance;
            cam = Camera.main.transform;
            Time.timeScale = 1f;
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f); // 開けた道路の上、北向き
            tp.SetActive(true);
            yield return new WaitForSeconds(0.8f);
        }

        IEnumerator Tap(UnityEngine.InputSystem.Controls.ButtonControl b, float after = 0.25f)
        {
            Press(b); yield return null; yield return null; Release(b);
            yield return new WaitForSeconds(after);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>カメラ（の回転の中心）を、ワールドの点 p に向ける。撃った時の跳ね上がりも押さえて狙い続ける。</summary>
        void LookAt(Vector3 p)
        {
            Vector3 d = p - cam.position;
            tp.Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - tp.RecoilYawAmount;
            tp.Pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg + tp.RecoilAmount;
        }

        [UnityTest]
        public IEnumerator Crosshair_ShowsWhenAiming_AndSpreadsWhenFiring()
        {
            yield return Load();
            bool visibleIdle = crosshair.IsVisible;
            yield return Tap(pad.rightShoulder, 0.2f); // ライフル
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.5f);
            bool visibleAim = crosshair.IsVisible;
            float gapStill = crosshair.GapPixels;
            Press(pad.rightTrigger);
            yield return new WaitForSeconds(0.6f);
            float gapFiring = crosshair.GapPixels;
            Shot("TP2_firing");
            Release(pad.rightTrigger);
            yield return new WaitForSeconds(0.8f);
            float gapRecovered = crosshair.GapPixels;
            Release(pad.leftTrigger);
            yield return null;
            Debug.Log($"[MatoiTest] crosshair: idle={visibleIdle} aim={visibleAim} gap still={gapStill:0.0} firing={gapFiring:0.0} recovered={gapRecovered:0.0}");
            Assert.IsFalse(visibleIdle, "構えていないのに照準マークが出ている");
            Assert.IsTrue(visibleAim, "構えた時に照準マークが出ていない");
            Assert.Greater(gapFiring, gapStill + 3f, "撃ち続けても照準が広がらない");
            Assert.Less(gapRecovered, gapFiring - 2f, "撃つのをやめても照準が戻らない");
        }

        [UnityTest]
        public IEnumerator Shots_GoWhereScreenCenterPoints_UpAndDown()
        {
            yield return Load();
            RaycastHit lastHit = default;
            bool gotHit = false;
            shooter.Hit += (h, d) => { lastHit = h; gotHit = true; };

            // 下を向いて地面を撃つ
            tp.Yaw = 0f;
            tp.Pitch = 28f;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.6f);
            Vector3 expected = controller.ScreenCenterAimPoint();
            yield return Tap(pad.rightTrigger, 0.1f);
            float groundErr = gotHit ? Vector3.Distance(lastHit.point, expected) : 99f;
            float downY = shooter.LastFireDirection.y;
            Shot("TP2_aim_down");

            // 上を向いて撃つ（上半身も反る）
            tp.Pitch = -18f;
            yield return new WaitForSeconds(0.5f);
            float elev = holder.Elevation;
            yield return Tap(pad.rightTrigger, 0.1f);
            float upY = shooter.LastFireDirection.y;
            Shot("TP2_aim_up");
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] tp shoot: groundErr={groundErr:0.00}m downY={downY:0.00} upY={upY:0.00} elevation={elev:0.0}");
            Assert.Less(groundErr, 0.8f, "画面中央の先に弾が当たっていない");
            Assert.Less(downY, -0.15f, "下を狙った時に弾が下へ飛んでいない");
            Assert.Greater(upY, 0.1f, "上を狙った時に弾が上へ飛んでいない");
            Assert.Greater(elev, 8f, "上を狙った時に上半身が反っていない");
        }

        [UnityTest]
        public IEnumerator AimingWithCrosshair_KillsEnemy_AndShowsHitMarker()
        {
            yield return Load();
            var e = EnemyBrain.All.OrderBy(x => x.name).First();
            e.PlaceAt(player.transform.position + Vector3.forward * 13f, 180f);
            yield return Tap(pad.rightShoulder, 0.2f); // ライフル
            Press(pad.leftTrigger);
            Press(pad.rightTrigger);
            float t = 0f;
            bool shot = false;
            while (t < 5f && !e.Health.IsDead)
            {
                LookAt(e.transform.position + Vector3.up * 1.2f); // プレイヤーが照準を敵に合わせ続ける
                if (!shot && e.Health.Current < e.Health.Max) { shot = true; Shot("TP2_hit_enemy"); }
                t += Time.deltaTime;
                yield return null;
            }
            Release(pad.rightTrigger);
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] tp kill: dead={e.Health.IsDead} t={t:0.00}s shots={shooter.ShotsFired} hitMarkers={crosshair.HitMarkers}");
            Assert.IsTrue(e.Health.IsDead, "照準を合わせて撃っても敵を倒せない");
            Assert.Greater(crosshair.HitMarkers, 0, "当たった時のヒットマークが出ていない");
        }

        [UnityTest]
        public IEnumerator AimSensitivity_IsLowerWhileAiming()
        {
            yield return Load();
            tp.Yaw = 0f;
            Set(pad.rightStick, new Vector2(1, 0));
            yield return new WaitForSeconds(0.4f);
            Set(pad.rightStick, Vector2.zero);
            float normal = Mathf.DeltaAngle(0f, tp.Yaw);
            yield return null;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.4f);
            float y0 = tp.Yaw;
            Set(pad.rightStick, new Vector2(1, 0));
            yield return new WaitForSeconds(0.4f);
            Set(pad.rightStick, Vector2.zero);
            float aimed = Mathf.DeltaAngle(y0, tp.Yaw);
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] tp sens: normal={normal:0.0}° aim={aimed:0.0}° ratio={aimed / normal:0.00}");
            Assert.That(aimed / normal, Is.InRange(0.4f, 0.7f), "構え中の感度が下がっていない");
        }

        [UnityTest]
        public IEnumerator Firing_KicksCameraUp()
        {
            yield return Load();
            yield return Tap(pad.rightShoulder, 0.2f); // ライフル
            tp.Pitch = 5f;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.5f);
            float before = tp.ViewPitch;
            Press(pad.rightTrigger);
            yield return new WaitForSeconds(0.6f);
            Release(pad.rightTrigger);
            Release(pad.leftTrigger);
            float kick = before - tp.ViewPitch;
            Debug.Log($"[MatoiTest] tp recoil: camera rose {kick:0.0}° over {shooter.ShotsFired} shots");
            Assert.Greater(kick, 1.5f, "撃ってもカメラが跳ね上がらない");
            Assert.Less(kick, 10f, "カメラの跳ね上がりが大きすぎる");
        }

        [UnityTest]
        public IEnumerator MovingWhileAiming_KeepsAimOnCameraForward()
        {
            yield return Load();
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.4f);
            Vector3 start = player.transform.position;
            Set(pad.leftStick, new Vector2(-1, 0)); // 左へ（西＝開けた側）
            yield return new WaitForSeconds(1.0f);
            Vector3 moved = Flat(player.transform.position - start);
            Vector3 left = Quaternion.Euler(0, tp.Yaw, 0) * Vector3.left;
            float moveErr = Vector3.Angle(moved, left);
            float aimErr = Vector3.Angle(Flat(holder.AimDirection), tp.FlatForward);
            float gunErr = Vector3.Angle(Flat(holder.WeaponInstance.transform.forward), tp.FlatForward);
            Shot("TP2_strafe");
            Set(pad.leftStick, Vector2.zero);
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] tp strafe: moved={moved.magnitude:0.0}m moveErr={moveErr:0.0} aimErr={aimErr:0.0} gunErr={gunErr:0.0} twist={holder.Twist:0}");
            Assert.Greater(moved.magnitude, 1.5f);
            Assert.Less(moveErr, 12f, "左に進んでいない");
            Assert.Less(aimErr, 3f, "移動中に狙いがカメラの正面からずれている");
            Assert.Less(gunErr, 8f, "銃がカメラの正面を向いていない");
        }

        [UnityTest]
        public IEnumerator PowerArmor_AimCameraIsFurther()
        {
            yield return Load();
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.8f);
            float human = Vector3.Distance(cam.position, tp.transform.position);
            Release(pad.leftTrigger);
            var station = Object.FindAnyObjectByType<PowerArmorStation>();
            motor.Teleport(station.transform.position - station.transform.forward * 1.6f, station.transform.eulerAngles.y);
            yield return new WaitForSeconds(0.3f);
            yield return Tap(pad.dpad.up, 0.5f);
            Assert.IsTrue(player.GetComponent<PowerArmorUser>().IsArmored);
            tp.Yaw = player.transform.eulerAngles.y;
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.8f);
            float armored = Vector3.Distance(cam.position, tp.transform.position);
            Shot("TP2_armor_aim");
            Release(pad.leftTrigger);
            Debug.Log($"[MatoiTest] tp armor aim: human={human:0.00} armored={armored:0.00}");
            Assert.Greater(armored, human + 0.4f, "パワーアーマーの構えカメラが引いていない");
        }

        [UnityTest]
        public IEnumerator TopDown_StillUsesStickAim_WithoutCrosshair()
        {
            yield return Load();
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.TopDown);
            yield return new WaitForSeconds(0.5f);
            Set(pad.rightStick, new Vector2(1, 0));
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[MatoiTest] topdown: aiming={holder.IsAiming} crosshair={crosshair.IsVisible} aimPoint={holder.AimPoint.HasValue}");
            Assert.IsTrue(holder.IsAiming, "見下ろしで右スティックの構えができない");
            Assert.IsFalse(crosshair.IsVisible, "見下ろしで照準マークが出ている");
            Assert.IsFalse(holder.AimPoint.HasValue, "見下ろしなのに照準点が使われている");
            Set(pad.rightStick, Vector2.zero);
        }

        static void Shot(string name)
        {
            var c = Camera.main;
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = c.targetTexture;
            c.targetTexture = rt;
            c.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            c.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }
    }
}
