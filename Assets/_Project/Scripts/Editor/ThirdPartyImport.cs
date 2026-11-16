// 外部アセット（Quaternius / CC0）や Blender から書き出したモデルの取り込み設定を自動で行う。
// 置き場所でルールを決めているので、同じフォルダに新しいファイルを入れるだけで同じ設定になる。
//   Art/Characters/**      … 人型キャラ（Humanoid）
//   Art/Animations/**      … 人型アニメーション（Humanoid、_Loop は自動でループ）
//   Art/Environment/**     … 背景モデル（アニメーションなし、葉・草は切り抜き透過）
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public class ThirdPartyImport : AssetPostprocessor
    {
        static bool IsCharacter(string p) => p.Contains("/_Project/Art/Characters/");
        static bool IsAnimation(string p) => p.Contains("/_Project/Art/Animations/");
        static bool IsEnvironment(string p) => p.Contains("/_Project/Art/Environment/") || p.Contains("/_Project/Art/Weapons/");

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            string p = assetPath;
            if (!(IsCharacter(p) || IsAnimation(p) || IsEnvironment(p))) return;

            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;   // Blender の軸の違いをモデルに焼き込む（Quaternius 推奨設定）

            if (IsCharacter(p) || IsAnimation(p))
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.optimizeGameObjects = false;
                mi.importAnimation = IsAnimation(p);
                if (IsAnimation(p)) mi.materialImportMode = ModelImporterMaterialImportMode.None;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
                mi.addCollider = false;
            }
        }

        /// <summary>
        /// Blender で "COL_" から始まる名前を付けた箱 → 当たり判定（BoxCollider）にして、見た目からは消す。
        /// 建物を手作りする時も、この名前の付け方をすれば自動で当たり判定が付く。
        /// </summary>
        void OnPostprocessModel(GameObject root)
        {
            if (!IsEnvironment(assetPath)) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("COL_")) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = mf.sharedMesh.bounds.size;
                }
                var mr = t.GetComponent<MeshRenderer>();
                if (mr) Object.DestroyImmediate(mr);
                if (mf) Object.DestroyImmediate(mf);
            }
        }

        void OnPreprocessAnimation()
        {
            if (!IsAnimation(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                // "Armature|Armature|Idle_Loop" → "Idle_Loop"
                c.name = c.takeName.Split('|').Last();
                bool loop = c.name.EndsWith("_Loop");
                c.loopTime = loop;
                c.loopPose = false;
                // 向き・高さはアニメーションに焼き込む（キャラの向きはプログラムが決める）
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                // 前後の移動は焼き込まない → 歩幅の速度（averageSpeed）を計測して足滑りを防ぐのに使う
                c.lockRootPositionXZ = false;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!(IsCharacter(assetPath) || IsEnvironment(assetPath))) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_Normal.png")) ti.textureType = TextureImporterType.NormalMap;
            // 武器は画面上で小さいので 2048 で十分（4096 はメモリの無駄）
            if (assetPath.Contains("/_Project/Art/Weapons/")) ti.maxTextureSize = 2048;
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!IsEnvironment(assetPath)) return;
            material.enableInstancing = true;
            string n = material.name.ToLowerInvariant();
            var tex = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            string tn = tex ? tex.name.ToLowerInvariant() : "";
            bool cutout = new[] { "leaf", "leaves", "grass", "flower", "fern", "petal", "plant", "clover", "bush" }
                .Any(k => n.Contains(k) || tn.Contains(k));
            if (cutout)
            {
                // 葉・草：アルファで切り抜き、両面描画
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.45f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cull", 0f);
                material.SetFloat("_Smoothness", 0.1f);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            // M8: トゥーン表現が ON なら、埋め込み材質もトゥーンシェーダーにする
            if (ToonConverter.Enabled)
            {
                var toon = Shader.Find(ToonConverter.ToonShaderName);
                if (toon) ToonConverter.Convert(material, toon, assetPath);
                if (cutout) material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
        }
    }
}
