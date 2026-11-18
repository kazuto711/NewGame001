// MPFB2 で試作した主人公（Art/Characters/MatoiHero/MatoiHero.fbx）を、人型キャラとして使えるように設定する。
// モデルは Tools/Blender/build_mpfb_hero.py で作る。
// メニュー「Matoi > Player Model 切替 (MPFB)」で、前の主人公（survival_character）と切り替えて比べられる。
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class MpfbHeroSetup
    {
        public const string Fbx = "Assets/_Project/Art/Characters/MatoiHero/MatoiHero.fbx";
        const string Dir = "Assets/_Project/Art/Characters/MatoiHero";
        const string PrefKey = "Matoi.UseMpfbHero";
        const string P = "mixamorig:";

        /// <summary>true ＝ プレイヤーに MPFB の主人公を使う（既定）。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true) && AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) != null;
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static GameObject Model => AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        public static Avatar Avatar => AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();

        [MenuItem("Matoi/Player Model 切替 (MPFB)")]
        public static void Toggle()
        {
            Enabled = !EditorPrefs.GetBool(PrefKey, true);
            PlayerSetup.InstallIntoTestField();
            Debug.Log("[Matoi] MPFB の主人公: " + (Enabled ? "ON" : "OFF"));
        }

        /// <summary>バッチ実行用：試作モデルでシーンを作り直す。</summary>
        public static void BuildWithMpfb() { Enabled = true; TestFieldBuilder.Build(); }

        /// <summary>バッチ実行用：今の主人公に戻してシーンを作り直す。</summary>
        public static void BuildWithCurrent() { Enabled = false; TestFieldBuilder.Build(); }

        public static void Setup()
        {
            SetupMaterials();
            SetupAvatar();
            AssetDatabase.SaveAssets();
            var avatar = Avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                Debug.LogError("[Matoi] MatoiHero の Avatar を作れませんでした");
        }

        // ---------- 骨の対応 ----------
        static IEnumerable<HumanBone> BuildHumanBones()
        {
            var map = new Dictionary<string, string>
            {
                { "Hips", "Hips" }, { "Spine", "Spine" }, { "Chest", "Spine1" }, { "UpperChest", "Spine2" },
                { "Neck", "Neck" }, { "Head", "Head" },
            };
            foreach (var side in new[] { "Left", "Right" })
            {
                map[side + "Shoulder"] = side + "Shoulder";
                map[side + "UpperArm"] = side + "Arm";
                map[side + "LowerArm"] = side + "ForeArm";
                map[side + "Hand"] = side + "Hand";
                map[side + "UpperLeg"] = side + "UpLeg";
                map[side + "LowerLeg"] = side + "Leg";
                map[side + "Foot"] = side + "Foot";
                map[side + "Toes"] = side + "ToeBase";
                foreach (var (human, bone) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") })
                {
                    map[$"{side} {human} Proximal"] = $"{side}Hand{bone}1";
                    map[$"{side} {human} Intermediate"] = $"{side}Hand{bone}2";
                    map[$"{side} {human} Distal"] = $"{side}Hand{bone}3";
                }
            }
            foreach (var kv in map)
            {
                var name = HumanTrait.BoneName.FirstOrDefault(x => x.Replace(" ", "") == kv.Key.Replace(" ", ""));
                if (name == null) { Debug.LogError("[Matoi] 人型の骨の名前が不正: " + kv.Key); continue; }
                var b = new HumanBone { humanName = name, boneName = P + kv.Value };
                b.limit.useDefaultValues = true;
                yield return b;
            }
        }

        // ---------- Avatar ----------
        static void SetupAvatar()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            var source = Model;
            var go = Object.Instantiate(source);
            try
            {
                // モデルは A ポーズなので、基準姿勢だけ T ポーズに直す（ひじ・ひざは少し曲げておく）
                var map = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform B(string n) => map[P + n];
                var forward = B("LeftToeBase").position - B("LeftFoot").position;
                forward.y = 0f;
                forward.Normalize();
                foreach (var s in new[] { "Left", "Right" })
                {
                    float side = Mathf.Sign(B(s + "Hand").position.x - B(s + "Arm").position.x);
                    var outward = Vector3.right * side;
                    var forearm = (outward + forward * 0.08f).normalized;
                    Aim(B(s + "Arm"), B(s + "ForeArm"), outward);
                    Aim(B(s + "ForeArm"), B(s + "Hand"), forearm);
                    Aim(B(s + "Hand"), B(s + "HandMiddle1"), forearm);
                    Aim(B(s + "UpLeg"), B(s + "Leg"), (Vector3.down + forward * 0.03f).normalized);
                    Aim(B(s + "Leg"), B(s + "Foot"), (Vector3.down - forward * 0.03f).normalized);
                }

                var desc = mi.humanDescription;
                desc.human = BuildHumanBones().ToArray();
                desc.skeleton = go.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t == go.transform ? source.name : t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray();
                desc.upperArmTwist = 0.5f;
                desc.lowerArmTwist = 0.5f;
                desc.upperLegTwist = 0.5f;
                desc.lowerLegTwist = 0.5f;
                desc.armStretch = 0.05f;
                desc.legStretch = 0.05f;
                desc.feetSpacing = 0f;
                desc.hasTranslationDoF = false;
                mi.humanDescription = desc;
                mi.SaveAndReimport();
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Aim(Transform bone, Transform child, Vector3 worldDir)
        {
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, worldDir) * bone.rotation;
        }

        // ---------- 材質 ----------
        static Material Toon(string name, Color color, float smooth, float outline, string texture = null, bool cutout = false)
        {
            string dir = Dir + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Dir, "Materials");
            string path = $"{dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(ToonConverter.LitShaderName));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            if (texture != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Textures/{texture}"));
            if (cutout)
            {
                // 髪・眉・まつ毛：画像の透明な部分を切り抜き、裏からも見えるようにする
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.2f);   // 低めにして、毛の隙間から地肌が見えないようにする
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            if (ToonConverter.Enabled)
            {
                ToonConverter.Convert(m, Shader.Find(ToonConverter.ToonShaderName), path);
                m.SetFloat("_OutlineWidth", outline);
                if (cutout) m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetupMaterials()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            void Map(Material m) => importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), m);
            Map(Toon("M_Mpfb_Skin", Color.white, 0.2f, 1.2f, "T_Mpfb_Skin.png"));
            Map(Toon("M_Mpfb_Scalp", new Color(0.035f, 0.03f, 0.028f), 0f, 0f));   // 髪の下の地肌
            Map(Toon("M_Mpfb_Eye",new Color(0.75f, 0.68f, 0.62f), 0.7f, 0f, "T_Mpfb_Eye.png"));
            Map(Toon("M_Mpfb_Brow", new Color(0.12f, 0.09f, 0.07f), 0f, 0f, "T_Mpfb_Brow.png", cutout: true));
            Map(Toon("M_Mpfb_Lash", new Color(0.08f, 0.06f, 0.05f), 0f, 0f, "T_Mpfb_Lash.png", cutout: true));
            Map(Toon("M_Mpfb_Hair", Color.white, 0.1f, 0f, "T_Mpfb_Hair.png", cutout: true));
            // 服：世界観に合わせて、くすんだ色に染める（ズボンは色を抜いたテクスチャに色を掛ける）
            Map(Toon("M_Mpfb_Shirt", new Color(0.30f, 0.33f, 0.20f), 0f, 1.8f));
            Map(Toon("M_Mpfb_Pants", new Color(0.30f, 0.30f, 0.33f), 0f, 1.8f, "T_Mpfb_Pants.png"));
            Map(Toon("M_Mpfb_Shoes", new Color(0.55f, 0.42f, 0.32f), 0.2f, 1.8f, "T_Mpfb_Shoes.png"));
            importer.SaveAndReimport();
        }
    }
}
