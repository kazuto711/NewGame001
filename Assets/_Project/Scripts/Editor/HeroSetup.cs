// 主人公モデルの材質・アニメーションコントローラを作るエディタツール。
// モデル（Hero.fbx）やアニメーション（UAL）を差し替えたら、メニュー「Matoi > Setup Hero」を実行し直す。
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class HeroSetup
    {
        public const string HeroFbx = "Assets/_Project/Art/Characters/Hero/Hero.fbx";
        const string HeroTex = "Assets/_Project/Art/Characters/Hero/Textures";
        const string HeroMatDir = "Assets/_Project/Art/Characters/Hero/Materials";
        public const string AnimFbx = "Assets/_Project/Art/Animations/UAL/UAL1_Standard_RM.fbx";
        public const string ControllerPath = "Assets/_Project/Animations/AC_Humanoid_Locomotion.controller";

        public struct ClipSpeeds { public float walk, jog, sprint; }
        public static ClipSpeeds LastSpeeds;

        [MenuItem("Matoi/Setup Hero (主人公モデル設定)")]
        public static void Setup()
        {
            SetupMaterials();
            BuildController();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Matoi] Hero setup done. clip speeds walk={LastSpeeds.walk:0.00} jog={LastSpeeds.jog:0.00} sprint={LastSpeeds.sprint:0.00}");
        }

        public static GameObject HeroModel => AssetDatabase.LoadAssetAtPath<GameObject>(HeroFbx);
        public static Avatar HeroAvatar => AssetDatabase.LoadAllAssetsAtPath(HeroFbx).OfType<Avatar>().FirstOrDefault();
        public static RuntimeAnimatorController Controller => AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        // ---------- 材質 ----------
        static Material Lit(string name, Color color, float smooth, string baseMap = null, string normalMap = null)
        {
            EnsureFolder(HeroMatDir);
            string path = $"{HeroMatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            if (baseMap != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{HeroTex}/{baseMap}"));
            if (normalMap != null)
            {
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{HeroTex}/{normalMap}"));
                m.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetupMaterials()
        {
            var skin = Lit("M_Hero_Skin", Color.white, 0.3f, "T_Hero_Skin.png", "T_Hero_Skin_Normal.png");
            var eye = Lit("M_Hero_Eye", Color.white, 0.8f, "T_Hero_Eye.png");
            var hair = Lit("M_Hero_Hair", new Color(0.55f, 0.45f, 0.38f), 0.15f, "T_Hero_Hair.png");
            var map = new Dictionary<string, Material>
            {
                { "MI_Superhero_Male", skin },
                { "MI_Eyes", eye },
                { "MI_Hair_1", hair }, { "MI_Hair_1.001", hair }, { "MI_Hair_1.002", hair },
                { "Outfit_Vest", Lit("M_Hero_Vest", new Color(0.20f, 0.23f, 0.15f), 0.12f) },
                { "Outfit_Shirt", Lit("M_Hero_Shirt", new Color(0.44f, 0.40f, 0.31f), 0.08f) },
                { "Outfit_Pants", Lit("M_Hero_Pants", new Color(0.25f, 0.23f, 0.21f), 0.08f) },
                { "Outfit_Boots", Lit("M_Hero_Boots", new Color(0.11f, 0.09f, 0.08f), 0.3f) },
                { "Outfit_Gloves", Lit("M_Hero_Gloves", new Color(0.09f, 0.09f, 0.09f), 0.25f) },
            };
            var importer = (ModelImporter)AssetImporter.GetAtPath(HeroFbx);
            foreach (var kv in map)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            importer.SaveAndReimport();
        }

        // ---------- アニメーション ----------
        static AnimationClip Clip(IEnumerable<AnimationClip> clips, string name)
        {
            var c = clips.FirstOrDefault(x => x.name == name);
            if (c == null) Debug.LogError("[Matoi] clip not found: " + name);
            return c;
        }

        static float PlanarSpeed(AnimationClip c)
        {
            var v = c.averageSpeed; v.y = 0;
            return v.magnitude;
        }

        public static void BuildController()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimFbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToList();
            var idle = Clip(clips, "Idle_Loop");
            var walk = Clip(clips, "Walk_Loop");
            var jog = Clip(clips, "Jog_Fwd_Loop");
            var sprint = Clip(clips, "Sprint_Loop");

            LastSpeeds = new ClipSpeeds { walk = PlanarSpeed(walk), jog = PlanarSpeed(jog), sprint = PlanarSpeed(sprint) };
            // 計測できなかった時の保険
            if (LastSpeeds.walk < 0.3f) LastSpeeds.walk = 1.4f;
            if (LastSpeeds.jog <= LastSpeeds.walk) LastSpeeds.jog = 3.5f;
            if (LastSpeeds.sprint <= LastSpeeds.jog) LastSpeeds.sprint = 6f;

            EnsureFolder("Assets/_Project/Animations");
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter(new AnimatorControllerParameter
                { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            // 速度で 待機→歩き→小走り→ダッシュ をなめらかに切り替える。
            // しきい値＝各アニメーション本来の速度 にすることで、実際の速度と歩幅が一致する（足が滑らない）。
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            // マイナス側＝後ずさり：前進アニメーションを逆再生する（後退専用のアニメーションが無いため）
            tree.AddChild(jog, -LastSpeeds.jog);
            tree.AddChild(walk, -LastSpeeds.walk);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, LastSpeeds.walk);
            tree.AddChild(jog, LastSpeeds.jog);
            tree.AddChild(sprint, LastSpeeds.sprint);
            var children = tree.children;
            children[0].timeScale = -1f;
            children[1].timeScale = -1f;
            tree.children = children;

            var sm = ctrl.layers[0].stateMachine;
            var state = sm.AddState("Locomotion");
            state.motion = tree;
            state.speedParameterActive = true;
            state.speedParameter = "AnimSpeed";
            sm.defaultState = state;

            BuildUpperBodyLayer(ctrl, clips);
            EditorUtility.SetDirty(ctrl);
        }

        const string UpperMaskPath = "Assets/_Project/Animations/Mask_UpperBody.mask";

        /// <summary>武器を持っている時だけ上半身を上書きするレイヤー（重みはスクリプトが制御）。</summary>
        static void BuildUpperBodyLayer(AnimatorController ctrl, List<AnimationClip> clips)
        {
            ctrl.AddParameter("WeaponDrawn", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Aiming", AnimatorControllerParameterType.Bool);

            AssetDatabase.DeleteAsset(UpperMaskPath);
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                             || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                             || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                             || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            AssetDatabase.CreateAsset(mask, UpperMaskPath);

            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            var layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.defaultWeight = 0f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;

            var usm = ctrl.layers[layers.Length - 1].stateMachine;
            var hold = usm.AddState("PistolHold");
            hold.motion = Clip(clips, "Pistol_Idle_Loop");
            var aim = usm.AddState("PistolAim");
            aim.motion = Clip(clips, "Pistol_Aim_Neutral");
            usm.defaultState = hold;

            var toAim = hold.AddTransition(aim);
            toAim.hasExitTime = false;
            toAim.duration = 0.08f;
            toAim.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
            var toHold = aim.AddTransition(hold);
            toHold.hasExitTime = false;
            toHold.duration = 0.18f;
            toHold.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");

            // M6: リロード（上半身のみ。どの状態からでも割り込む）
            ctrl.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            var reload = usm.AddState("Reload");
            reload.motion = Clip(clips, "Pistol_Reload");
            reload.speed = 1.3f;
            var toReload = usm.AddAnyStateTransition(reload);
            toReload.hasExitTime = false;
            toReload.duration = 0.1f;
            toReload.canTransitionToSelf = false;
            toReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
            var fromReload = reload.AddTransition(hold);
            fromReload.hasExitTime = true;
            fromReload.exitTime = 0.9f;
            fromReload.duration = 0.15f;

            // M6: 近接攻撃（全身。重みはスクリプトが制御）
            ctrl.AddParameter("Melee", AnimatorControllerParameterType.Trigger);
            ctrl.AddLayer("Action");
            var all = ctrl.layers;
            all[all.Length - 1].defaultWeight = 0f;
            ctrl.layers = all;
            var asm = ctrl.layers[all.Length - 1].stateMachine;
            var empty = asm.AddState("Empty");
            asm.defaultState = empty;
            var punch = asm.AddState("Punch");
            punch.motion = Clip(clips, "Punch_Cross");
            punch.speed = 1.9f;   // 元のアニメーションはゆっくりなので、速くして鋭くする
            var toPunch = asm.AddAnyStateTransition(punch);
            toPunch.hasExitTime = false;
            toPunch.duration = 0.04f;
            toPunch.canTransitionToSelf = true;
            toPunch.AddCondition(AnimatorConditionMode.If, 0, "Melee");
            var fromPunch = punch.AddTransition(empty);
            fromPunch.hasExitTime = true;
            fromPunch.exitTime = 0.95f;
            fromPunch.duration = 0.1f;

            // M7: 被弾（ひるみ）と死亡
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            toPunch.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            var hit = asm.AddState("Hit");
            hit.motion = Clip(clips, "Hit_Chest");
            hit.speed = 1.2f;
            var toHit = asm.AddAnyStateTransition(hit);
            toHit.hasExitTime = false;
            toHit.duration = 0.03f;
            toHit.canTransitionToSelf = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            toHit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var fromHit = hit.AddTransition(empty);
            fromHit.hasExitTime = true;
            fromHit.exitTime = 0.9f;
            fromHit.duration = 0.1f;

            // M8: 回避（前転）
            ctrl.AddParameter("Roll", AnimatorControllerParameterType.Trigger);
            var roll = asm.AddState("Roll");
            roll.motion = Clip(clips, "Roll");
            roll.speed = 2.9f;   // 元は 1.5 秒 → 約 0.5 秒に
            var toRoll = asm.AddAnyStateTransition(roll);
            toRoll.hasExitTime = false;
            toRoll.duration = 0.04f;
            toRoll.canTransitionToSelf = true;
            toRoll.AddCondition(AnimatorConditionMode.If, 0, "Roll");
            toRoll.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var fromRoll = roll.AddTransition(empty);
            fromRoll.hasExitTime = true;
            fromRoll.exitTime = 0.92f;
            fromRoll.duration = 0.08f;

            var death = asm.AddState("Death");
            death.motion = Clip(clips, "Death01");
            var toDeath = asm.AddAnyStateTransition(death);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            var revive = death.AddTransition(empty);
            revive.hasExitTime = false;
            revive.duration = 0.1f;
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
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
