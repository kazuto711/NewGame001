// 動作確認用：シーンを開いて「ゲームカメラ視点」と「俯瞰視点」の2枚を PNG 保存する。
// メニュー「Matoi > Capture Test Field」/ バッチ: -executeMethod Matoi.EditorTools.MatoiCapture.Capture
//   -matoiscene <シーンパス>（省略時 TestField） -matoishot <出力フォルダ>（省略時 Logs/Shots）
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Matoi.EditorTools
{
    public static class MatoiCapture
    {
        [MenuItem("Matoi/Capture Test Field (スクリーンショット)")]
        public static void Capture()
        {
            string scene = TestFieldBuilder.ScenePath;
            string outDir = "Logs/Shots";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-matoiscene") scene = args[i + 1];
                if (args[i] == "-matoishot") outDir = args[i + 1];
            }
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var main = Camera.main;
            if (main == null) { Debug.LogError("[Matoi] No main camera"); return; }

            // シーンを開いた直後の1枚目はライティングが未初期化で暗くなるため、捨て撮りする
            Shot(main, Path.Combine(outDir, "_warmup.png"));
            File.Delete(Path.Combine(outDir, "_warmup.png"));
            Shot(main, Path.Combine(outDir, "game_view.png"));

            // 俯瞰（全体の配置確認用の一時カメラ）
            var go = new GameObject("TempOverviewCamera");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.fieldOfView = 50f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.transform.position = new Vector3(-48f, 46f, -62f);
            go.transform.LookAt(new Vector3(6f, 0f, 6f));
            Shot(cam, Path.Combine(outDir, "overview.png"));
            Object.DestroyImmediate(go);

            Debug.Log("[Matoi] Shots saved to " + outDir);
        }

        static void Shot(Camera cam, string path)
        {
            // sRGB を明示しないと、リニア値のまま保存されて画像が暗くなる
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
        }
    }
}
