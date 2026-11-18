// 持ち込みの主人公モデル（Assets/survival_character.fbx）を、プレイヤーとして使えるように設定する。
// 旧モデル（Hero.fbx）は消さない。敵とパワーアーマーの台座は今も旧モデルを使っている。
// メニュー「Matoi > Player Model 切替 (新⇔旧)」でプレイヤーの見た目だけを切り替えられる。
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class SurvivalHeroSetup
    {
        public const string Fbx = "Assets/survival_character.fbx";
        const string MatDir = "Assets/_Project/Art/Characters/Survival/Materials";
        const string PrefKey = "Matoi.UseSurvivalHero";

        /// <summary>true ＝ 新モデルをプレイヤーに使う（既定）。false ＝ 旧 Hero に戻す。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        public static GameObject PlayerModel => MpfbHeroSetup.Enabled ? MpfbHeroSetup.Model
            : Enabled ? AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) : HeroSetup.HeroModel;
        public static Avatar PlayerAvatar => MpfbHeroSetup.Enabled ? MpfbHeroSetup.Avatar
            : Enabled ? AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault()
            : HeroSetup.HeroAvatar;

        [MenuItem("Matoi/Player Model 切替 (新⇔旧)")]
        public static void Toggle()
        {
            Enabled = !Enabled;
            PlayerSetup.InstallIntoTestField();
            Debug.Log("[Matoi] Player model: " + (Enabled ? "survival_character" : "Hero (旧)"));
        }

        /// <summary>変更前の主人公（ゲーム内の選択画面で選べる）。</summary>
        public static GameObject SurvivalModel => AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        public static Avatar SurvivalAvatar => AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();

        /// <summary>選択できる主人公をすべて使える状態にする。</summary>
        public static void Setup()
        {
            if (MpfbHeroSetup.Model) MpfbHeroSetup.Setup();
            if (!SurvivalModel) return;
            SetupMaterials();
            SetupAvatar();
            AssetDatabase.SaveAssets();
            var avatar = SurvivalAvatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                Debug.LogError("[Matoi] survival_character の Avatar を作れませんでした");
        }

        // ---------- 骨の対応 ----------
        // 背骨が 5 本ある（UE5 形式）ので、自動判定に任せず明示する
        static readonly Dictionary<string, string> Bones = new Dictionary<string, string>
        {
            { "Hips", "pelvis" }, { "Spine", "spine_01" }, { "Chest", "spine_03" }, { "UpperChest", "spine_05" },
            { "Neck", "neck_01" }, { "Head", "head" },
            { "LeftShoulder", "clavicle_l" }, { "LeftUpperArm", "upperarm_l" }, { "LeftLowerArm", "lowerarm_l" }, { "LeftHand", "hand_l" },
            { "RightShoulder", "clavicle_r" }, { "RightUpperArm", "upperarm_r" }, { "RightLowerArm", "lowerarm_r" }, { "RightHand", "hand_r" },
            { "LeftUpperLeg", "thigh_l" }, { "LeftLowerLeg", "calf_l" }, { "LeftFoot", "foot_l" }, { "LeftToes", "ball_l" },
            { "RightUpperLeg", "thigh_r" }, { "RightLowerLeg", "calf_r" }, { "RightFoot", "foot_r" }, { "RightToes", "ball_r" },
        };

        static readonly (string human, string bone)[] Fingers =
        {
            ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky"),
        };

        static IEnumerable<HumanBone> BuildHumanBones()
        {
            foreach (var kv in Bones) yield return Bone(kv.Key, kv.Value);
            foreach (var (side, s) in new[] { ("Left", "l"), ("Right", "r") })
                foreach (var (human, bone) in Fingers)
                {
                    yield return Bone($"{side} {human} Proximal", $"{bone}_01_{s}");
                    yield return Bone($"{side} {human} Intermediate", $"{bone}_02_{s}");
                    yield return Bone($"{side} {human} Distal", $"{bone}_03_{s}");
                }
        }

        static HumanBone Bone(string human, string bone)
        {
            var b = new HumanBone { humanName = ToHumanName(human), boneName = bone };
            b.limit.useDefaultValues = true;
            return b;
        }

        // "LeftUpperArm" → "LeftUpperArm"（Unity の HumanTrait の名前に合わせる）
        static string ToHumanName(string n)
        {
            var match = HumanTrait.BoneName.FirstOrDefault(x => x.Replace(" ", "") == n.Replace(" ", ""));
            if (match == null) Debug.LogError("[Matoi] 人型の骨の名前が不正: " + n);
            return match ?? n;
        }

        // ---------- Avatar ----------
        static void SetupAvatar()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var go = Object.Instantiate(source);
            try
            {
                // モデルは A ポーズ（腕が斜め下）なので、基準姿勢だけ T ポーズに直す。
                // これをしないと、アニメーションを流した時に腕の角度がずれる。
                var map = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                // ひじ・ひざは完全に伸ばさず少し曲げておく。一直線だと Unity が関節の曲がる向きを判定できず、
                // 腕の上下が逆になる（待機で腕が頭の上に上がる）。
                var forward = map["ball_l"].position - map["foot_l"].position;
                forward.y = 0f;
                forward.Normalize();
                foreach (var s in new[] { "l", "r" })
                {
                    float side = Mathf.Sign(map[$"hand_{s}"].position.x - map[$"upperarm_{s}"].position.x);
                    var outward = Vector3.right * side;
                    var forearm = (outward + forward * 0.08f).normalized;
                    Aim(map[$"upperarm_{s}"], map[$"lowerarm_{s}"], outward);
                    Aim(map[$"lowerarm_{s}"], map[$"hand_{s}"], forearm);
                    Aim(map[$"hand_{s}"], map[$"middle_01_{s}"], forearm);
                    Aim(map[$"thigh_{s}"], map[$"calf_{s}"], (Vector3.down + forward * 0.03f).normalized);
                    Aim(map[$"calf_{s}"], map[$"foot_{s}"], (Vector3.down - forward * 0.03f).normalized);
                }

                var skeleton = go.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                {
                    name = t == go.transform ? source.name : t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                }).ToArray();

                var desc = mi.humanDescription;
                desc.human = BuildHumanBones().ToArray();
                desc.skeleton = skeleton;
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
        // FBX が参照するテクスチャ（Assets/Textures/...）はファイルに含まれていないため、当面は単色のトゥーンで塗る
        static Material Toon(string name, Color color, float smooth, float outline)
        {
            EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(ToonConverter.LitShaderName));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            if (ToonConverter.Enabled)
            {
                ToonConverter.Convert(m, Shader.Find(ToonConverter.ToonShaderName), path);
                m.SetFloat("_OutlineWidth", outline);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetupMaterials()
        {
            var skin = Toon("M_Survival_Skin", new Color(0.78f, 0.58f, 0.46f), 0.2f, 1.2f);
            var brow = Toon("M_Survival_Brow", new Color(0.10f, 0.07f, 0.05f), 0f, 0f);   // 毛は板ポリなので縁取りしない
            var map = new Dictionary<string, Material>
            {
                { "Head", skin }, { "Body2", skin }, { "Arms", skin },
                { "Body_Arkit_Eye", Toon("M_Survival_Eye", new Color(0.13f, 0.09f, 0.07f), 0.7f, 0f) },   // FBX の "Body_Arkit:Eye" は Unity でこの名前になる
                { "Mouth", Toon("M_Survival_Mouth", new Color(0.55f, 0.30f, 0.28f), 0.2f, 0f) },
                { "Brows_Leashes", brow },
                { "Hair3", Toon("M_Survival_Hair", new Color(0.17f, 0.11f, 0.08f), 0.1f, 0f) },
                { "Jacket1", Toon("M_Survival_Jacket", new Color(0.27f, 0.30f, 0.20f), 0.1f, 1.8f) },
                { "Jeans1", Toon("M_Survival_Jeans", new Color(0.20f, 0.24f, 0.31f), 0.08f, 1.8f) },
                { "Shoes1", Toon("M_Survival_Shoes", new Color(0.19f, 0.14f, 0.10f), 0.2f, 1.8f) },
                { "Gloves1", Toon("M_Survival_Gloves", new Color(0.12f, 0.11f, 0.10f), 0.2f, 1.4f) },
                { "Backpack2", Toon("M_Survival_Backpack", new Color(0.33f, 0.27f, 0.18f), 0.1f, 1.8f) },
            };
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            foreach (var kv in map)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            importer.SaveAndReimport();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
