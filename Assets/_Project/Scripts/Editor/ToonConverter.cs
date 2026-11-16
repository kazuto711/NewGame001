// トゥーン表現の一括切り替え。メニュー「Matoi > Toon Look ON / OFF」。
//  - プロジェクト内の .mat（URP/Lit）のシェーダーを Matoi/Toon に差し替える（値はそのまま引き継がれる）
//  - FBX に埋め込まれた材質（自然・武器）は、取り込み時（ThirdPartyImport）に差し替える
//  - OFF にすると URP/Lit に戻る（比較用）
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class ToonConverter
    {
        const string PrefKey = "Matoi.ToonEnabled";
        public const string ToonShaderName = "Matoi/Toon";
        public const string LitShaderName = "Universal Render Pipeline/Lit";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem("Matoi/Toon Look ON (トゥーン表現)")]
        public static void TurnOn() { Enabled = true; ApplyAll(); }

        [MenuItem("Matoi/Toon Look OFF (通常の表現に戻す)")]
        public static void TurnOff() { Enabled = false; ApplyAll(); }

        /// <summary>現在の設定（ON/OFF）を全ての材質に反映する。</summary>
        public static void ApplyAll() => ApplyAll(true);

        public static void ApplyAll(bool reimportModels)
        {
            var toon = Shader.Find(ToonShaderName);
            var lit = Shader.Find(LitShaderName);
            if (toon == null) { Debug.LogError("[Matoi] Matoi/Toon shader not found"); return; }
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if (Enabled && m.shader == lit) { Convert(m, toon, path); n++; }
                else if (!Enabled && m.shader == toon) { m.shader = lit; RestoreKeywords(m); EditorUtility.SetDirty(m); n++; }
            }
            // FBX 埋め込みの材質は取り込み直して反映
            var models = !reimportModels ? new string[0] : AssetDatabase
                .FindAssets("t:Model", new[] { "Assets/_Project/Art/Environment", "Assets/_Project/Art/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            AssetDatabase.StartAssetEditing();
            try { foreach (var p in models) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Matoi] Toon {(Enabled ? "ON" : "OFF")}: {n} materials, {models.Length} models reimported");
        }

        /// <summary>1つの材質をトゥーンにする（置き場所に応じてアウトラインの太さ・色を決める）。</summary>
        public static void Convert(Material m, Shader toon, string path)
        {
            m.shader = toon;
            RestoreKeywords(m);
            string p = path.ToLowerInvariant();
            float outline = 1.1f;
            Color outlineColor = new Color(0.08f, 0.07f, 0.07f);
            if (p.Contains("/characters/enemies/")) { outline = 2.0f; outlineColor = new Color(0.30f, 0.04f, 0.34f); } // 敵は紫の縁取りで見分けやすく
            else if (p.Contains("/characters/")) outline = 1.8f;
            else if (p.Contains("/weapons/")) outline = 1.2f;
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) outline = 0f; // 葉・草は縁取りしない
            string n = m.name.ToLowerInvariant();
            if (n.Contains("ground") || n.Contains("asphalt") || n.Contains("roadline") || n.Contains("paddy") || n.Contains("mountain"))
                outline = 0f; // 地面・遠景は縁取りしない（線だらけになるため）
            // 背景は平らな大きい面が多く、硬いハイライトが面全体に乗って白飛びするため、つやを抑える
            if (p.Contains("/environment/") && m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.12f));
            m.SetFloat("_OutlineWidth", outline);
            m.SetColor("_OutlineColor", outlineColor);
            if (!AssetDatabase.IsSubAsset(m) && AssetDatabase.Contains(m)) EditorUtility.SetDirty(m);
        }

        static void RestoreKeywords(Material m)
        {
            if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) m.EnableKeyword("_ALPHATEST_ON");
            if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap")) m.EnableKeyword("_NORMALMAP");
            if (m.HasProperty("_OcclusionMap") && m.GetTexture("_OcclusionMap")) m.EnableKeyword("_OCCLUSIONMAP");
            if (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.001f) m.EnableKeyword("_EMISSION");
        }
    }
}
