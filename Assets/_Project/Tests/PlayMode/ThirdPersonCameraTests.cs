// 三人称視点の動作確認：背後から追従・右スティックで回転・カメラ基準の移動・肩の切り替え・壁よけ・構え時の肩越し・視点の切り替え。
using System.Collections;
using System.IO;
using System.Linq;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class ThirdPersonCameraTests : InputTestFixture
    {
        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        ThirdPersonCameraController tp;
        CameraModeSwitcher switcher;
        Transform cam;

        IEnumerator Load()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            switcher = Object.FindAnyObjectByType<CameraModeSwitcher>();
            switcher.SetMode(CameraMode.ThirdPerson);
            tp = ThirdPersonCameraController.Instance;
            cam = Camera.main.transform;
            Time.timeScale = 1f;
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f); // 開けた道路の上、北向き
            tp.SetActive(true);
            yield return new WaitForSeconds(1.0f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        Vector3 Pivot => tp.transform.position;

        [UnityTest]
        public IEnumerator Camera_StartsBehindPlayer_ShowingWholeBody()
        {
            yield return Load();
            Vector3 toCam = cam.position - player.transform.position;
            float behind = Vector3.Dot(Flat(toCam), player.transform.forward);
            float dist = Vector3.Distance(cam.position, Pivot);
            float height = cam.position.y - player.transform.position.y;
            // 全身（足元〜頭）が画面内に入っているか
            var vp = Camera.main;
            Vector3 feet = vp.WorldToViewportPoint(player.transform.position);
            Vector3 head = vp.WorldToViewportPoint(player.transform.position + Vector3.up * 1.85f);
            Shot("TP_explore");
            Debug.Log($"[MatoiTest] tp start: behind={behind:0.00} dist={dist:0.00} height={height:0.00} feetY={feet.y:0.00} headY={head.y:0.00} mode={CameraModeSwitcher.Mode}");
            Assert.AreEqual(CameraMode.ThirdPerson, CameraModeSwitcher.Mode);
            Assert.Less(behind, -2.5f, "カメラが主人公の背後にない");
            Assert.That(dist, Is.InRange(3.0f, 4.8f));
            Assert.That(height, Is.InRange(1.4f, 3.2f), "カメラの高さが想定外");
            Assert.That(feet.y, Is.InRange(0.02f, 0.98f), "足元が画面に入っていない");
            Assert.That(head.y, Is.InRange(0.02f, 0.98f), "頭が画面に入っていない");
        }

        [UnityTest]
        public IEnumerator RightStick_RotatesCamera_AndMovementFollowsCameraDirection()
        {
            // 右スティックを左へ倒して約 90° 回し、西（開けた田んぼ側）を向く。東は電柱があるので避ける
            yield return LoadAndRotate(-90f);
            float yawAfter = tp.Yaw;
            Vector3 start = player.transform.position;
            Set(pad.leftStick, new Vector2(0, 1)); // 前へ
            yield return new WaitForSeconds(0.6f);  // 最初は北向きから 90° 振り向く（曲がりながら進む）
            start = player.transform.position;     // 振り向いた後の進み方を測る
            yield return new WaitForSeconds(0.6f);
            Vector3 moved = Flat(player.transform.position - start);
            float moveErr = Vector3.Angle(moved, tp.FlatForward);
            float faceErr = Vector3.Angle(Flat(player.transform.forward), tp.FlatForward);
            float camBehind = Vector3.Dot(Flat(cam.position - player.transform.position).normalized, player.transform.forward);
            Shot("TP_after_turn");
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] tp rotate: yaw 0->{yawAfter:0} moved={moved.magnitude:0.0}m moveErr={moveErr:0.0} faceErr={faceErr:0.0} camBehind={camBehind:0.00}");
            Assert.That(Mathf.DeltaAngle(0f, yawAfter), Is.InRange(-120f, -60f), "右スティックでカメラが回っていない");
            Assert.Less(moveErr, 8f, "前に倒した時にカメラの向きへ進んでいない");
            Assert.Less(faceErr, 10f, "体が移動方向を向いていない");
            Assert.Less(camBehind, -0.8f, "移動中にカメラが背後に回っていない");
        }

        IEnumerator LoadAndRotate(float targetYaw)
        {
            yield return Load();
            tp.Yaw = 0f;
            float sign = Mathf.Sign(targetYaw);
            Set(pad.rightStick, new Vector2(sign, 0));
            float t = 0f;
            while (Mathf.Abs(Mathf.DeltaAngle(0f, tp.Yaw)) < Mathf.Abs(targetYaw) && t < 3f) { t += Time.deltaTime; yield return null; }
            Set(pad.rightStick, Vector2.zero);
            yield return new WaitForSeconds(0.3f);
        }

        [UnityTest]
        public IEnumerator SideStrafe_BodyTurnsToMovementDirection()
        {
            yield return Load();
            Set(pad.leftStick, new Vector2(1, 0)); // 右へ
            yield return new WaitForSeconds(1.0f);
            Vector3 right = Quaternion.Euler(0, tp.Yaw, 0) * Vector3.right;
            float faceErr = Vector3.Angle(Flat(player.transform.forward), right);
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] tp right: faceErr={faceErr:0.0} speed={motor.Speed:0.00}");
            Assert.Less(faceErr, 12f, "右に動いた時に体が右を向いていない");
        }

        [UnityTest]
        public IEnumerator ShoulderSwitch_MovesCameraToOtherSide()
        {
            yield return Load();
            float sideBefore = Vector3.Dot(cam.position - Pivot, cam.right);
            Press(pad.rightStickButton); yield return null; Release(pad.rightStickButton);
            yield return new WaitForSeconds(0.1f);
            float midSide = tp.CurrentSide;
            yield return new WaitForSeconds(0.8f);
            float sideAfter = Vector3.Dot(cam.position - Pivot, cam.right);
            Shot("TP_left_shoulder");
            Debug.Log($"[MatoiTest] tp shoulder: before={sideBefore:0.00} mid={midSide:0.00} after={sideAfter:0.00} right={tp.RightShoulder}");
            Assert.Greater(sideBefore, 0.2f, "最初は右肩側にない");
            Assert.Less(sideAfter, -0.2f, "左肩側に移っていない");
            Assert.That(midSide, Is.InRange(0.05f, 0.95f), "切り替えがなめらかでない（一瞬で移っている）");
        }

        [UnityTest]
        public IEnumerator Camera_DoesNotClipIntoWalls()
        {
            yield return Load();
            // 蔵の西側の壁を背にして立ち、カメラが蔵の中に入る向き（東）にする
            var kura = CameraOccluder.All.First(o => o.name.StartsWith("House_27_5"));
            var b = kura.Bounds;
            motor.Teleport(new Vector3(b.min.x - 0.9f, 0.1f, b.center.z), 270f);
            tp.Yaw = 270f; // カメラは西を向く → 本来カメラが来る位置（東）は蔵の中
            tp.Pitch = 5f;
            yield return new WaitForSeconds(1.2f);
            var hits = Physics.OverlapSphere(cam.position, 0.12f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.CompareTag("Player")).ToArray();
            float dist = Vector3.Distance(cam.position, Pivot);
            bool inside = b.Contains(cam.position);
            Shot("TP_wall");
            Debug.Log($"[MatoiTest] tp wall: dist={dist:0.00} overlaps={hits.Length} insideKuraBounds={inside}");
            Assert.AreEqual(0, hits.Length, "カメラが物の中にめり込んでいる: " + string.Join(",", hits.Select(h => h.name)));
            Assert.Less(dist, 2.2f, "壁の前でカメラが手前に寄っていない");
        }

        [UnityTest]
        public IEnumerator Aiming_SwitchesToShoulderCamera_AndBack()
        {
            yield return Load();
            float exploreDist = Vector3.Distance(cam.position, Pivot);
            Press(pad.leftTrigger);
            yield return new WaitForSeconds(0.7f);
            var brain = Camera.main.GetComponent<CinemachineBrain>();
            string active = brain.ActiveVirtualCamera?.Name;
            float aimDist = Vector3.Distance(cam.position, Pivot);
            var holder = player.GetComponent<WeaponHolder>();
            float aimErr = Vector3.Angle(holder.AimDirection, tp.FlatForward);
            Shot("TP_aim");
            Release(pad.leftTrigger);
            yield return new WaitForSeconds(0.8f);
            float backDist = Vector3.Distance(cam.position, Pivot);
            Debug.Log($"[MatoiTest] tp aim: explore={exploreDist:0.00} aim={aimDist:0.00} active={active} back={backDist:0.00} aimErr={aimErr:0.0} aiming={holder.IsAiming}");
            Assert.AreEqual("CM Aim", active, "構えた時に肩越しカメラになっていない");
            Assert.Less(aimDist, exploreDist - 1.2f, "構えた時にカメラが寄っていない");
            Assert.Less(aimErr, 3f, "（仮）狙う方向がカメラの正面になっていない");
            Assert.Greater(backDist, exploreDist - 0.4f, "構えを解いても元に戻らない");
        }

        [UnityTest]
        public IEnumerator ToggleMode_SwitchesBetweenTopDownAndThirdPerson()
        {
            yield return Load();
            Press(pad.dpad.down); yield return null; Release(pad.dpad.down);
            yield return new WaitForSeconds(1.0f);
            var mode1 = CameraModeSwitcher.Mode;
            float topDist = Vector3.Distance(cam.position, player.transform.position);
            Shot("TP_toggle_topdown");
            Press(pad.dpad.down); yield return null; Release(pad.dpad.down);
            yield return new WaitForSeconds(1.0f);
            var mode2 = CameraModeSwitcher.Mode;
            float tpDist = Vector3.Distance(cam.position, player.transform.position);
            Debug.Log($"[MatoiTest] tp toggle: {mode1} dist={topDist:0.0} -> {mode2} dist={tpDist:0.0}");
            Assert.AreEqual(CameraMode.TopDown, mode1);
            Assert.Greater(topDist, 12f, "見下ろし視点のカメラになっていない");
            Assert.AreEqual(CameraMode.ThirdPerson, mode2);
            Assert.Less(tpDist, 6f, "三人称視点に戻っていない");
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
