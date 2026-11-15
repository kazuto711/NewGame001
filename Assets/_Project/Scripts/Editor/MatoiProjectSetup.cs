// プロジェクトの初期設定と、動作確認用スクリーンショット撮影を行うエディタ専用ツール。
// メニュー「Matoi > ...」から実行できる。バッチ起動時は -executeMethod で呼ぶ。
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Matoi.EditorTools
{
    public static class MatoiProjectSetup
    {
        const string Root = "Assets/_Project";
        const string UrpDir = Root + "/Settings/URP";
        const string SandboxScene = Root + "/Scenes/Sandbox.unity";

        static readonly string[] Folders =
        {
            "Art/Characters", "Art/Environment", "Art/Materials", "Art/Shaders", "Art/VFX",
            "Animations", "Data", "Prefabs", "Scenes",
            "Scripts/Runtime", "Scripts/Editor", "Settings/Input",
        };

        [MenuItem("Matoi/Setup Project (初期設定)")]
        public static void Setup()
        {
            CreateFolders();
            ConfigureRenderPipeline();
            ConfigurePlayer();
            CreateSandboxScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Matoi] Setup finished.");
        }

        static void CreateFolders()
        {
            foreach (var f in Folders)
            {
                var full = Path.Combine(Root, f).Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(full)) continue;
                var parent = Path.GetDirectoryName(full).Replace('\\', '/');
                var parts = parent.Split('/');
                var cur = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = cur + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                    cur = next;
                }
                AssetDatabase.CreateFolder(parent, Path.GetFileName(full));
            }
        }

        static void ConfigureRenderPipeline()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpDir + "/PC_RPAsset.asset");
            if (rp == null) { Debug.LogError("[Matoi] PC_RPAsset not found"); return; }

            GraphicsSettings.defaultRenderPipeline = rp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
            }
            QualitySettings.SetQualityLevel(current, false);

            var gs = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(
                UrpDir + "/UniversalRenderPipelineGlobalSettings.asset");
            if (gs != null)
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(gs);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Matoi";
            PlayerSettings.productName = "MATOI";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Active Input Handling = Input System Package (New) のみ。
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null) { prop.intValue = 1; so.ApplyModifiedProperties(); }
            }
        }

        static void CreateSandboxScene()
        {
            if (File.Exists(SandboxScene)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.9f, 0.78f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.transform.position = new Vector3(0f, 14f, -12f);
            camGo.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);

            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "PlayerPlaceholder";
            player.transform.position = new Vector3(0f, 1f, 0f);

            for (int i = 0; i < 6; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Block_" + i;
                float a = i * Mathf.PI * 2f / 6f;
                c.transform.position = new Vector3(Mathf.Cos(a) * 7f, 1f, Mathf.Sin(a) * 7f);
                c.transform.localScale = new Vector3(2f, 2f, 2f);
            }

            EditorSceneManager.SaveScene(scene, SandboxScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SandboxScene, true) };
        }

        /// <summary>
        /// Sandbox シーンをメインカメラで撮影して PNG 保存する（動作確認用）。
        /// バッチ時は -matoishot &lt;出力パス&gt; で保存先を指定。
        /// </summary>
        [MenuItem("Matoi/Capture Sandbox Screenshot")]
        public static void CaptureSandbox()
        {
            string outPath = "Logs/sandbox.png";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-matoishot") outPath = args[i + 1];

            EditorSceneManager.OpenScene(SandboxScene, OpenSceneMode.Single);
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[Matoi] No main camera"); return; }

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Debug.Log("[Matoi] Screenshot saved: " + outPath
                      + " / pipeline=" + (GraphicsSettings.currentRenderPipeline != null
                          ? GraphicsSettings.currentRenderPipeline.name : "Built-in"));
        }
    }
}
