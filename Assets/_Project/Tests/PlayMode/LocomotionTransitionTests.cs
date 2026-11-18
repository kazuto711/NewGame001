// 第6マイルストーン：停止・切り返し・その場旋回のアニメーションが、操作に合わせて再生されるかを確認する。
using System.Collections;
using System.IO;
using Matoi.Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class LocomotionTransitionTests : InputTestFixture
    {
        const float CamYaw = 20f;
        Gamepad pad;
        CharacterMotor motor;
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
            animator = player.GetComponentInChildren<Animator>();
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 270f); // 障害物の無い道路の上
            yield return new WaitForSeconds(0.4f);
        }

        static Vector2 StickFor(Vector3 worldDir)
        {
            var l = Quaternion.Euler(0, -CamYaw, 0) * worldDir.normalized;
            return new Vector2(l.x, l.z);
        }

        bool InState(string name) => animator.GetCurrentAnimatorStateInfo(0).IsName(name)
                                     || animator.GetNextAnimatorStateInfo(0).IsName(name);

        /// <summary>指定の状態に入るまで最大 seconds 秒待ち、入れたら true。</summary>
        IEnumerator WaitForState(string name, float seconds, System.Action<bool> result)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (InState(name)) { result(true); yield break; }
                yield return null;
            }
            result(false);
        }

        [UnityTest]
        public IEnumerator ReleasingStick_WhileRunning_PlaysRunStop_ThenReturnsToLocomotion()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(1.0f);
            Assert.Greater(motor.Speed, 3f, "前提：走っている");

            Set(pad.leftStick, Vector2.zero);
            bool played = false;
            yield return WaitForState("StopRun", 0.4f, r => played = r);
            Shot("Loco_run_stop");
            Assert.IsTrue(played, "走りの停止アニメーションが再生されていない");

            bool back = false;
            yield return WaitForState("Locomotion", 2.5f, r => back = r);
            Assert.IsTrue(back, "停止の後、移動の状態に戻っていない");
        }

        [UnityTest]
        public IEnumerator MovingAgain_DuringStop_CancelsIntoLocomotion()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(1.0f);
            Set(pad.leftStick, Vector2.zero);
            bool played = false;
            yield return WaitForState("StopRun", 0.4f, r => played = r);
            Assert.IsTrue(played, "前提：停止アニメーション中");

            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "走り出しても停止アニメーションが続いている");
        }

        [UnityTest]
        public IEnumerator ReversingWhileRunning_PlaysPivot()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(1.0f);
            Set(pad.leftStick, StickFor(Vector3.right));
            bool played = false;
            yield return WaitForState("Pivot", 0.4f, r => played = r);
            Shot("Loco_pivot");
            Assert.IsTrue(played, "切り返しのアニメーションが再生されていない");
        }

        [UnityTest]
        public IEnumerator AimingSideways_FromStandstill_StepsInPlace()
        {
            yield return LoadField();
            Vector3 right = Quaternion.Euler(0, 90, 0) * motor.transform.forward;
            Set(pad.rightStick, StickFor(right));
            bool played = false;
            yield return WaitForState("TurnRight", 0.8f, r => played = r);
            Shot("Loco_turn_right");
            Assert.IsTrue(played, "右へ向いた時に足の踏みかえが再生されていない");
        }

        /// <summary>その状態を再生している間、体の向きのずれ・足の高さ・腰の高さを記録し、途中の姿勢を撮影する。</summary>
        IEnumerator MeasurePose(string state, string shot, float shotTime = 0.45f)
        {
            bool played = false;
            yield return WaitForState(state, 0.8f, r => played = r);
            Assert.IsTrue(played, state + " が再生されていない");

            float maxYaw = 0f, minFoot = 9f, minHips = 9f, maxHips = 0f;
            bool shotTaken = false;
            float end = Time.time + 2.5f;
            while (Time.time < end)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(state) && !animator.IsInTransition(0))
                {
                    // 腰の左右の骨から、体の正面を求める
                    Vector3 hipLine = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position
                                      - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                    Vector3 body = Vector3.Cross(hipLine, Vector3.up); body.y = 0f;
                    Vector3 fwd = motor.transform.forward; fwd.y = 0f;
                    maxYaw = Mathf.Max(maxYaw, Vector3.Angle(body, fwd));
                    float ground = motor.transform.position.y;
                    minFoot = Mathf.Min(minFoot,
                        animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y - ground,
                        animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y - ground);
                    float hips = animator.GetBoneTransform(HumanBodyBones.Hips).position.y - ground;
                    minHips = Mathf.Min(minHips, hips);
                    maxHips = Mathf.Max(maxHips, hips);
                    if (!shotTaken && info.normalizedTime >= shotTime) { Shot(shot); shotTaken = true; }
                }
                else if (shotTaken) break;
                yield return null;
            }
            Debug.Log($"[MatoiTest] pose {state}: maxYaw={maxYaw:0.0} minFoot={minFoot:0.00} hips={minHips:0.00}-{maxHips:0.00} shot={shotTaken}");
            Assert.IsTrue(shotTaken, state + " が途中で打ち切られた");
            Assert.Less(maxYaw, 50f, state + "：体がキャラクターの向きから大きくずれている");
            Assert.Greater(minFoot, 0f, state + "：足首が地面より下に沈んでいる"); // 足首の骨の高さで判定する
            Assert.Less(minFoot, 0.25f, state + "：足が地面から浮いている");
            Assert.Greater(minHips, 0.55f, state + "：腰が落ちすぎている");
            Assert.Less(maxHips, 1.15f, state + "：腰が上がりすぎている");
        }

        [UnityTest]
        public IEnumerator StopPose_StaysAlignedAndGrounded()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(1.0f);
            Set(pad.leftStick, Vector2.zero);
            yield return MeasurePose("StopRun", "Loco_mid_run_stop");
        }

        [UnityTest]
        public IEnumerator WalkStopPose_StaysAlignedAndGrounded()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left) * 0.35f);
            yield return new WaitForSeconds(1.0f);
            Assert.That(motor.Speed, Is.InRange(0.8f, 3f), "前提：歩いている");
            Set(pad.leftStick, Vector2.zero);
            yield return MeasurePose("StopWalk", "Loco_mid_walk_stop");
        }

        [UnityTest]
        public IEnumerator PivotPose_StaysGrounded()
        {
            yield return LoadField();
            Set(pad.leftStick, StickFor(Vector3.left));
            yield return new WaitForSeconds(1.0f);
            Set(pad.leftStick, StickFor(Vector3.right));
            yield return MeasurePose("Pivot", "Loco_mid_pivot", 0.2f);
        }

        [UnityTest]
        public IEnumerator TurnPose_StaysAlignedAndGrounded()
        {
            yield return LoadField();
            Vector3 right = Quaternion.Euler(0, 90, 0) * motor.transform.forward;
            Set(pad.rightStick, StickFor(right));
            yield return MeasurePose("TurnRight", "Loco_mid_turn_right");
        }

        void Shot(string name)
        {
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 34f;
            Vector3 p = motor.transform.position + Vector3.up * 1.0f;
            go.transform.position = p + Quaternion.Euler(0, 55, 0) * motor.transform.forward * 3.4f + Vector3.up * 0.5f;
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
