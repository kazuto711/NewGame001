// ジャンプ中の姿勢の確認：上半身が後ろへ反らないこと（その場ジャンプ・走りジャンプ・武器を持った時）。
using System.Collections;
using System.IO;
using Matoi.CameraSystem;
using Matoi.Characters;
using Matoi.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Matoi.Tests
{
    public class JumpPoseTests : InputTestFixture
    {
        /// <summary>これより後ろへ反ると、変な姿勢に見える（度。マイナスが後ろ）。</summary>
        const float MaxBackLean = -6f;

        Gamepad pad;
        GameObject player;
        CharacterMotor motor;
        Animator animator;
        Transform hips, neck, head;

        IEnumerator Load()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            SceneManager.LoadScene("TestField");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<CameraModeSwitcher>().SetMode(CameraMode.TopDown);
            foreach (var enemy in Object.FindObjectsByType<Matoi.AI.EnemyBrain>(FindObjectsSortMode.None)) enemy.gameObject.SetActive(false);
            player = GameObject.FindWithTag("Player");
            motor = player.GetComponent<CharacterMotor>();
            animator = player.GetComponentInChildren<Animator>();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            motor.Teleport(new Vector3(1.5f, 0.1f, -2f), 0f);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>腰から首への線が、体の正面へ何度傾いているか（プラスが前、マイナスが後ろ）。</summary>
        float TorsoLean()
        {
            Transform root = player.transform;
            Vector3 torso = Vector3.ProjectOnPlane(neck.position - hips.position, root.right);
            return Vector3.SignedAngle(root.up, torso, root.right);
        }

        IEnumerator MeasureJump(string label, bool running)
        {
            if (running)
            {
                var l = Quaternion.Euler(0, -20f, 0) * Vector3.forward;
                Set(pad.leftStick, new Vector2(l.x, l.z));
                yield return new WaitForSeconds(0.8f);
            }
            Press(pad.buttonSouth);
            yield return null;
            Release(pad.buttonSouth);

            float worst = 180f, worstStart = 180f, worstLoop = 180f, worstLand = 180f;
            bool shot = false;
            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                yield return null;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                float lean = TorsoLean();
                if (state.IsName("JumpStart")) worstStart = Mathf.Min(worstStart, lean);
                if (state.IsName("JumpLoop")) worstLoop = Mathf.Min(worstLoop, lean);
                if (state.IsName("JumpLand")) worstLand = Mathf.Min(worstLand, lean);
                if (state.IsName("JumpStart") || state.IsName("JumpLoop") || state.IsName("JumpLand")) worst = Mathf.Min(worst, lean);
                if (!shot && state.IsName("JumpLoop")) { shot = true; SideShot("JP_" + label); }
            }
            Set(pad.leftStick, Vector2.zero);
            Debug.Log($"[MatoiTest] jump pose {label}: worst={worst:0.0} start={worstStart:0.0} loop={worstLoop:0.0} land={worstLand:0.0}");
            Assert.Greater(worst, MaxBackLean, $"{label}: ジャンプで上半身が後ろへ反っている");
        }

        [UnityTest]
        public IEnumerator StandingJump_KeepsTheTorsoUpright()
        {
            yield return Load();
            yield return MeasureJump("standing", false);
        }

        [UnityTest]
        public IEnumerator RunningJump_KeepsTheTorsoUpright()
        {
            yield return Load();
            yield return MeasureJump("running", true);
        }

        [UnityTest]
        public IEnumerator JumpWithRifle_KeepsTheTorsoUpright()
        {
            yield return Load();
            Press(pad.rightShoulder); yield return null; yield return null; Release(pad.rightShoulder);
            yield return new WaitForSeconds(0.6f);
            yield return MeasureJump("rifle", false);
        }

        void SideShot(string name)
        {
            Transform root = player.transform;
            Vector3 center = root.position + Vector3.up * 1f;
            var go = new GameObject("TempShot");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(Camera.main);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            go.transform.position = center + root.right * 3.6f + root.forward * 0.3f;
            go.transform.LookAt(center);
            var rt = new RenderTexture(720, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "../Logs/Shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(go);
        }
    }
}
