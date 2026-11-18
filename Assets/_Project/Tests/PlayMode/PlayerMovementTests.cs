// M2 動作確認：仮想ゲームパッドで操作し、移動の手触りが狙いどおりかを数値で確認する。
// Unity メニュー Window > General > Test Runner > PlayMode から実行できる。
using System.Collections;
using System.IO;
using Matoi.Characters;
using Matoi.DebugTools;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class PlayerMovementTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;

        IEnumerator LoadField()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            // このテストは見下ろし視点を前提にしている（三人称視点は ThirdPersonCameraTests で確認）
            Object.FindAnyObjectByType<Matoi.CameraSystem.CameraModeSwitcher>()?.SetMode(Matoi.CameraSystem.CameraMode.TopDown);
            var player = GameObject.FindWithTag("Player");
            Assert.IsNotNull(player, "Player not found in TestField");
            motor = player.GetComponent<CharacterMotor>();
            yield return new WaitForSeconds(0.6f); // 着地と、視点切り替えのカメラ移行（0.35 秒）を待つ
        }

        static Vector3 CamForward => Quaternion.Euler(0, CamYaw, 0) * Vector3.forward;

        /// <summary>ワールドの方向に進むためのスティック値。</summary>
        static Vector2 StickFor(Vector3 worldDir)
        {
            var local = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(local.x, local.z);
        }

        IEnumerator MeasureTimeTo(float targetSpeed, float timeout, System.Action<float> result)
        {
            float t = 0f;
            while (motor.Speed < targetSpeed && t < timeout) { t += Time.deltaTime; yield return null; }
            result(t);
        }

        IEnumerator StopAndMeasure(System.Action<float, float> result)
        {
            Set(pad.leftStick, Vector2.zero);
            Vector3 start = motor.transform.position;
            float t = 0f;
            while (motor.Speed > 0.01f && t < 3f) { t += Time.deltaTime; yield return null; }
            var d = motor.transform.position - start; d.y = 0;
            result(t, d.magnitude);
        }

        [UnityTest]
        public IEnumerator Jog_AcceleratesGradually_AndMovesTowardScreenUp()
        {
            yield return LoadField();
            Vector3 start = motor.transform.position;
            var cam = Camera.main.transform.position;
            Set(pad.leftStick, new Vector2(0, 1));

            float t90 = 0;
            yield return MeasureTimeTo(motor.Profile.jogSpeed * 0.9f, 3f, v => t90 = v);
            yield return new WaitForSeconds(0.8f);

            var d = motor.transform.position - start; d.y = 0;
            float angle = Vector3.Angle(d, CamForward);
            Debug.Log($"[MatoiTest] jog: t90={t90:0.000}s speed={motor.Speed:0.00} dirError={angle:0.0}° camMoved={(Camera.main.transform.position - cam).magnitude:0.0}m");
            Shot("M2_jog");

            Assert.Greater(t90, 0.1f, "加速が速すぎる（瞬間的に最高速）");
            Assert.Less(t90, 0.7f, "加速が遅すぎる");
            Assert.AreEqual(motor.Profile.jogSpeed, motor.Speed, 0.15f);
            Assert.Less(angle, 8f, "スティック上が画面の奥方向になっていない");

            float stopT = 0, stopD = 0;
            yield return StopAndMeasure((tt, dd) => { stopT = tt; stopD = dd; });
            Debug.Log($"[MatoiTest] stop: time={stopT:0.000}s distance={stopD:0.00}m");

            // 止まった後、カメラがプレイヤーに追いついているか（なめらかさのため少し遅れて追従する）
            yield return new WaitForSeconds(1.5f);
            var playerMoved = motor.transform.position - start; playerMoved.y = 0;
            var camMoved = Camera.main.transform.position - cam; camMoved.y = 0;
            Debug.Log($"[MatoiTest] camera: playerMoved={playerMoved.magnitude:0.00} camMoved={camMoved.magnitude:0.00}");
            Assert.Less((playerMoved - camMoved).magnitude, 0.3f, "カメラが追従していない");
            Assert.Greater(stopT, 0.05f, "瞬間停止している（慣性がない）");
            Assert.Less(stopT, 0.6f, "止まるのが遅すぎる");
            Assert.That(stopD, Is.InRange(0.1f, 1.2f));
        }

        [UnityTest]
        public IEnumerator LightTilt_Walks()
        {
            yield return LoadField();
            Set(pad.leftStick, new Vector2(0, 0.3f));
            yield return new WaitForSeconds(1.0f);
            // スティックにはデッドゾーン補正がかかるので、補正後の値から期待速度を求める
            float processed = pad.leftStick.ReadValue().magnitude;
            float expected = motor.Profile.walkSpeed * processed / motor.Profile.walkInputThreshold;
            Debug.Log($"[MatoiTest] walk: speed={motor.Speed:0.00} expected={expected:0.00}");
            Assert.AreEqual(expected, motor.Speed, 0.2f);
        }

        [UnityTest]
        public IEnumerator Sprint_LatchesUntilStickReleased()
        {
            yield return LoadField();
            Set(pad.leftStick, new Vector2(0, 1));
            yield return new WaitForSeconds(0.3f);
            Press(pad.leftStickButton);
            yield return null;
            Release(pad.leftStickButton);
            yield return new WaitForSeconds(1.5f);
            float sprintSpeed = motor.Speed;
            Shot("M2_sprint");

            Set(pad.leftStick, Vector2.zero);
            yield return new WaitForSeconds(0.6f);
            Set(pad.leftStick, new Vector2(0, 1));
            yield return new WaitForSeconds(1.2f);
            Debug.Log($"[MatoiTest] sprint: {sprintSpeed:0.00} -> after release & re-push {motor.Speed:0.00}");
            Assert.AreEqual(motor.Profile.sprintSpeed, sprintSpeed, 0.2f);
            Assert.AreEqual(motor.Profile.jogSpeed, motor.Speed, 0.2f, "スティックを離してもダッシュが解除されていない");
        }

        [UnityTest]
        public IEnumerator Reverse_PlantsThenTurns()
        {
            yield return LoadField();
            Set(pad.leftStick, new Vector2(0, 1));
            yield return new WaitForSeconds(1.0f);
            Vector3 fwdBefore = motor.transform.forward;

            Set(pad.leftStick, new Vector2(0, -1));
            bool pivoted = false;
            float t = 0f, turnedAt = -1f;
            while (t < 1.2f)
            {
                pivoted |= motor.IsPivoting;
                if (turnedAt < 0 && Vector3.Angle(motor.transform.forward, -fwdBefore) < 15f) turnedAt = t;
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[MatoiTest] reverse: pivoted={pivoted} turnedAt={turnedAt:0.000}s speed={motor.Speed:0.00}");
            Assert.IsTrue(pivoted, "切り返し（踏ん張り）が発生していない");
            Assert.That(turnedAt, Is.InRange(0.05f, 0.6f), "反転にかかる時間が想定外");
            Assert.Greater(motor.Speed, 2f);
        }

        [UnityTest]
        public IEnumerator Turn90_SlowsDownInsteadOfDrifting()
        {
            yield return LoadField();
            Set(pad.leftStick, new Vector2(0, 1));
            yield return new WaitForSeconds(1.0f);
            Set(pad.leftStick, new Vector2(1, 0));
            float minSpeed = 99f, t = 0f, turnedAt = -1f;
            Vector3 right = Quaternion.Euler(0, CamYaw, 0) * Vector3.right;
            while (t < 0.8f)
            {
                minSpeed = Mathf.Min(minSpeed, motor.Speed);
                if (turnedAt < 0 && Vector3.Angle(motor.transform.forward, right) < 5f) turnedAt = t;
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[MatoiTest] turn90: minSpeed={minSpeed:0.00} turnedAt={turnedAt:0.000}s");
            Assert.Less(minSpeed, motor.Profile.jogSpeed * 0.95f, "曲がるときに減速していない");
            Assert.Greater(minSpeed, motor.Profile.jogSpeed * 0.3f, "曲がるときに減速しすぎ");
            Assert.That(turnedAt, Is.InRange(0.05f, 0.4f));
        }

        [UnityTest]
        public IEnumerator PowerArmor_IsHeavierThanHuman()
        {
            yield return LoadField();
            float humanT = 0, humanStop = 0;
            Set(pad.leftStick, new Vector2(0, 1));
            yield return MeasureTimeTo(motor.Profile.jogSpeed * 0.9f, 3f, v => humanT = v);
            yield return new WaitForSeconds(0.5f);
            yield return StopAndMeasure((tt, dd) => humanStop = dd);

            Press(pad.selectButton);
            yield return null;
            Release(pad.selectButton);
            yield return null;
            Assert.AreEqual("Move_PowerArmor", motor.Profile.name);

            float armorT = 0, armorStop = 0;
            Set(pad.leftStick, new Vector2(0, 1));
            yield return MeasureTimeTo(motor.Profile.jogSpeed * 0.9f, 5f, v => armorT = v);
            yield return new WaitForSeconds(0.5f);
            yield return StopAndMeasure((tt, dd) => armorStop = dd);
            Debug.Log($"[MatoiTest] armor vs human: t90 {armorT:0.00}s vs {humanT:0.00}s, stop {armorStop:0.00}m vs {humanStop:0.00}m");
            Assert.Greater(armorT, humanT * 1.5f);
            Assert.Greater(armorStop, humanStop * 1.5f);
        }

        [UnityTest]
        public IEnumerator ClimbsStairsAndRamp()
        {
            yield return LoadField();
            // 階段（移動テスト区画）
            motor.Teleport(new Vector3(16f, 0.1f, -40.5f), 0f);
            // 階段の上には踊り場がないので、上り切ると反対側へ降りる。到達した最高点を記録する。
            Set(pad.leftStick, StickFor(Vector3.forward));
            float stairsY = 0f, tt = 0f;
            while (tt < 2.5f)
            {
                stairsY = Mathf.Max(stairsY, motor.transform.position.y);
                if (tt < 0.9f && tt + Time.deltaTime >= 0.9f) Shot("M2_stairs");
                tt += Time.deltaTime;
                yield return null;
            }
            Shot("M2_stairs");

            // 20度の坂
            Set(pad.leftStick, Vector2.zero);
            motor.Teleport(new Vector3(11f, 0.1f, -40.5f), 0f);
            yield return null;
            Set(pad.leftStick, StickFor(Vector3.forward));
            yield return new WaitForSeconds(2.5f);
            float rampY = motor.transform.position.y;
            Debug.Log($"[MatoiTest] climb: stairsMaxY={stairsY:0.00} rampTopY={rampY:0.00}");
            Assert.Greater(stairsY, 1.2f, "階段を上れていない");
            Assert.Greater(rampY, 1.4f, "坂を上れていない");
        }

        static void Shot(string name)
        {
            var cam = Camera.main;
            if (cam == null) return;
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
