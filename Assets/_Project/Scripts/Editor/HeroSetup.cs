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

            AddLocomotionTransitions(ctrl, sm, state);
            AddJumpAndCrouch(ctrl, sm, state, clips);
            BuildUpperBodyLayer(ctrl, clips);
            EditorUtility.SetDirty(ctrl);
        }

        const string MixamoDir = "Assets/_Project/Art/Animations/Mixamo";

        /// <summary>Mixamo のアニメーションを読み込む。無ければ null。</summary>
        static AnimationClip MixamoClip(string file) => AssetDatabase.LoadAllAssetsAtPath($"{MixamoDir}/{file}.fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));

        /// <summary>
        /// 停止・その場旋回・切り返しの動き（Mixamo）。CharacterAnimator がトリガーを送った時だけ再生する。
        /// アニメーションが無い環境でも動くよう、見つからない物は作らない。
        /// </summary>
        static void AddLocomotionTransitions(AnimatorController ctrl, AnimatorStateMachine sm, AnimatorState locomotion)
        {
            void Add(string trigger, string file, float speed, float exitTime)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath($"{MixamoDir}/{file}.fbx").OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (clip == null) { Debug.LogWarning("[Matoi] Mixamo clip not found: " + file); return; }

                ctrl.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                var s = sm.AddState(trigger);
                s.motion = clip;
                s.speed = speed;
                s.iKOnFeet = true;   // 体格が違っても、足をアニメーションどおりの高さに置く（踏ん張りで地面に沈まない）

                var enter = locomotion.AddTransition(s);
                enter.hasExitTime = false;
                enter.duration = 0.12f;
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);

                var done = s.AddTransition(locomotion);
                done.hasExitTime = true;
                done.exitTime = exitTime;
                done.duration = 0.2f;

                // 途中で動き出したら、すぐに移動へ戻す。減速中の速度が残っているので、速度ではなく入力で判断する
                var cancel = s.AddTransition(locomotion);
                cancel.hasExitTime = false;
                cancel.duration = 0.12f;
                if (trigger == "Pivot")
                {
                    // 切り返しは入力が入ったまま再生するので、踏ん張りが終わって再加速したら戻す
                    cancel.AddCondition(AnimatorConditionMode.IfNot, 0, "Pivoting");
                    cancel.AddCondition(AnimatorConditionMode.Greater, 2f, "Speed");
                }
                else cancel.AddCondition(AnimatorConditionMode.If, 0, "WantsMove");
            }

            ctrl.AddParameter("WantsMove", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Pivoting", AnimatorControllerParameterType.Bool);

            Add("StopRun", "Mx_RunStop", 1.3f, 0.8f);
            Add("StopWalk", "Mx_WalkStop", 1.2f, 0.8f);
            Add("TurnLeft", "Mx_TurnLeft90", 1.6f, 0.8f);
            Add("TurnRight", "Mx_TurnRight90", 1.6f, 0.8f);
            Add("Pivot", "Mx_RunTurn180", 1.5f, 0.75f);
        }

        /// <summary>しゃがみ（待機・歩き）とジャンプ（踏み切り・空中・着地）。</summary>
        static void AddJumpAndCrouch(AnimatorController ctrl, AnimatorStateMachine sm, AnimatorState locomotion, List<AnimationClip> clips)
        {
            ctrl.AddParameter("Crouching", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);

            // しゃがみ：速度で 待機↔歩き を切り替える（後ずさりは逆再生）
            var crouchWalk = Clip(clips, "Crouch_Fwd_Loop");
            float crouchClipSpeed = Mathf.Max(0.5f, PlanarSpeed(crouchWalk));
            var tree = new BlendTree
            {
                name = "Crouch",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            tree.AddChild(crouchWalk, -crouchClipSpeed);
            tree.AddChild(Clip(clips, "Crouch_Idle_Loop"), 0f);
            tree.AddChild(crouchWalk, crouchClipSpeed);
            var children = tree.children;
            children[0].timeScale = -1f;
            tree.children = children;
            var crouch = sm.AddState("Crouch");
            crouch.motion = tree;

            AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, string param, bool value)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = duration;
                t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
                return t;
            }
            Link(locomotion, crouch, 0.2f, "Crouching", true);
            Link(crouch, locomotion, 0.2f, "Crouching", false);

            // ジャンプ：踏み切り → 空中 → 着地
            var jumpStart = sm.AddState("JumpStart");
            jumpStart.motion = Clip(clips, "Jump_Start");
            jumpStart.speed = 2.4f;
            var jumpLoop = sm.AddState("JumpLoop");
            jumpLoop.motion = Clip(clips, "Jump_Loop");
            var jumpLand = sm.AddState("JumpLand");
            jumpLand.motion = Clip(clips, "Jump_Land");
            jumpLand.speed = 1.5f;

            var toStart = sm.AddAnyStateTransition(jumpStart);
            toStart.hasExitTime = false;
            toStart.duration = 0.06f;
            toStart.canTransitionToSelf = false;
            toStart.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            var startToLoop = jumpStart.AddTransition(jumpLoop);
            startToLoop.hasExitTime = true;
            startToLoop.exitTime = 0.8f;
            startToLoop.duration = 0.1f;
            Link(jumpStart, jumpLand, 0.06f, "Airborne", false);

            // 足場から落ちた時は、踏み切りなしで空中の動きになる
            Link(locomotion, jumpLoop, 0.15f, "Airborne", true);
            Link(crouch, jumpLoop, 0.15f, "Airborne", true);
            Link(jumpLoop, jumpLand, 0.06f, "Airborne", false);

            var landToMove = jumpLand.AddTransition(locomotion);
            landToMove.hasExitTime = true;
            landToMove.exitTime = 0.7f;
            landToMove.duration = 0.15f;
        }

        /// <summary>ライフル専用の上半身（待機・構え）。リロードは WeaponHolder が両手を動かす。アニメーションが無ければ拳銃用のまま動く。</summary>
        static void AddRifleStates(AnimatorController ctrl, AnimatorStateMachine usm, AnimatorState hold, AnimatorState aim)
        {
            AnimationClip Load(string file) => AssetDatabase.LoadAllAssetsAtPath($"{MixamoDir}/{file}.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            var holdClip = Load("Mx_RifleHold_Loop");
            var aimClip = Load("Mx_RifleAim_Loop");
            ctrl.AddParameter("RifleStance", AnimatorControllerParameterType.Bool);
            if (!holdClip || !aimClip) { Debug.LogWarning("[Matoi] Mixamo rifle clips not found"); return; }

            AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, string param, bool value)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false;
                t.duration = duration;
                t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
                return t;
            }

            var rifleHold = usm.AddState("RifleHold");
            rifleHold.motion = holdClip;
            var rifleAim = usm.AddState("RifleAim");
            rifleAim.motion = aimClip;
            Link(hold, rifleHold, 0.15f, "RifleStance", true);
            Link(rifleHold, hold, 0.15f, "RifleStance", false);
            Link(aim, rifleAim, 0.15f, "RifleStance", true);
            Link(rifleAim, aim, 0.15f, "RifleStance", false);
            Link(rifleHold, rifleAim, 0.08f, "Aiming", true);
            Link(rifleAim, rifleHold, 0.18f, "Aiming", false);
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
            // 再生速度は WeaponShooter が「武器のリロード時間」に合わせて毎回設定する
            ctrl.AddParameter(new AnimatorControllerParameter
                { name = "ReloadSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            reload.speedParameterActive = true;
            reload.speedParameter = "ReloadSpeed";
            var toReload = usm.AddAnyStateTransition(reload);
            toReload.hasExitTime = false;
            toReload.duration = 0.1f;
            toReload.canTransitionToSelf = false;
            toReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
            var fromReload = reload.AddTransition(hold);
            fromReload.hasExitTime = true;
            fromReload.exitTime = 0.9f;
            fromReload.duration = 0.15f;

            // 拳銃の発砲（構え中に撃つたびに最初から再生する）
            ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            var fire = usm.AddState("PistolFire");
            fire.motion = Clip(clips, "Pistol_Shoot");
            fire.speed = 1.6f;
            var toFire = aim.AddTransition(fire);
            toFire.hasExitTime = false;
            toFire.duration = 0.03f;
            toFire.AddCondition(AnimatorConditionMode.If, 0, "Fire");
            var fireAgain = fire.AddTransition(fire);
            fireAgain.hasExitTime = false;
            fireAgain.duration = 0.03f;
            fireAgain.canTransitionToSelf = true;
            fireAgain.AddCondition(AnimatorConditionMode.If, 0, "Fire");
            var fromFire = fire.AddTransition(aim);
            fromFire.hasExitTime = true;
            fromFire.exitTime = 0.8f;
            fromFire.duration = 0.08f;

            AddRifleStates(ctrl, usm, hold, aim);

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
            ctrl.AddParameter("MeleeStep", AnimatorControllerParameterType.Int);
            var toPunch = asm.AddAnyStateTransition(punch);
            toPunch.hasExitTime = false;
            toPunch.duration = 0.04f;
            toPunch.canTransitionToSelf = true;
            toPunch.AddCondition(AnimatorConditionMode.If, 0, "Melee");
            toPunch.AddCondition(AnimatorConditionMode.Equals, 0, "MeleeStep");
            var fromPunch = punch.AddTransition(empty);
            fromPunch.hasExitTime = true;
            fromPunch.exitTime = 0.95f;
            fromPunch.duration = 0.1f;

            // コンボの他の段（MeleeStep: 1=ジャブ, 2=フック）
            var meleeDeadGuards = new List<AnimatorStateTransition> { toPunch };
            void AddMeleeStep(string name, AnimationClip clip, float speed, int value)
            {
                if (!clip) { Debug.LogWarning("[Matoi] melee clip not found: " + name); return; }
                var s = asm.AddState(name);
                s.motion = clip;
                s.speed = speed;
                var to = asm.AddAnyStateTransition(s);
                to.hasExitTime = false;
                to.duration = 0.04f;
                to.canTransitionToSelf = true;
                to.AddCondition(AnimatorConditionMode.If, 0, "Melee");
                to.AddCondition(AnimatorConditionMode.Equals, value, "MeleeStep");
                meleeDeadGuards.Add(to);
                var back = s.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.95f;
                back.duration = 0.1f;
            }
            AddMeleeStep("Jab", Clip(clips, "Punch_Jab"), 1.9f, 1);
            AddMeleeStep("Hook", MixamoClip("Mx_Hook"), 1.5f, 2);

            // 敵の攻撃：ため～振り下ろしを 1 本で再生する
            ctrl.AddParameter("EnemyAttack", AnimatorControllerParameterType.Trigger);
            var enemyClip = MixamoClip("Mx_EnemyAttack");
            AnimatorStateTransition toEnemyAttack = null;
            if (enemyClip)
            {
                var enemyAttack = asm.AddState("EnemyAttack");
                enemyAttack.motion = enemyClip;
                enemyAttack.speed = 1.5f;   // 振り抜きの瞬間（元の動きの約 1.0 秒）を、攻撃が当たる瞬間（約 0.67 秒後）に合わせた速さ
                toEnemyAttack = asm.AddAnyStateTransition(enemyAttack);
                toEnemyAttack.hasExitTime = false;
                toEnemyAttack.duration = 0.08f;
                toEnemyAttack.AddCondition(AnimatorConditionMode.If, 0, "EnemyAttack");
                var back = enemyAttack.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.12f;
            }

            // M7: 被弾（ひるみ）と死亡
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            foreach (var guard in meleeDeadGuards) guard.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            if (toEnemyAttack != null) toEnemyAttack.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            // 被弾：攻撃が来た方向で動きを変える（HitDir: 0=正面, 1=背後, 2=左, 3=右, 4=強い攻撃）
            ctrl.AddParameter("HitDir", AnimatorControllerParameterType.Int);
            void AddHit(string name, AnimationClip clip, float speed, int value)
            {
                if (!clip) { Debug.LogWarning("[Matoi] hit clip not found: " + name); return; }
                var s = asm.AddState(name);
                s.motion = clip;
                s.speed = speed;
                var to = asm.AddAnyStateTransition(s);
                to.hasExitTime = false;
                to.duration = 0.03f;
                to.canTransitionToSelf = true;
                to.AddCondition(AnimatorConditionMode.If, 0, "Hit");
                to.AddCondition(AnimatorConditionMode.Equals, value, "HitDir");
                to.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                var back = s.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.1f;
            }
            AddHit("Hit", MixamoClip("Mx_HitFront") ?? Clip(clips, "Hit_Chest"), 1.5f, 0);
            AddHit("HitBack", MixamoClip("Mx_HitBack"), 1.5f, 1);
            AddHit("HitLeft", MixamoClip("Mx_HitLeft"), 1.5f, 2);
            AddHit("HitRight", MixamoClip("Mx_HitRight"), 1.5f, 3);
            AddHit("HitHeavy", MixamoClip("Mx_HitHeavy"), 1.3f, 4);

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

            // 採集・物色：ひざをついて手を動かす
            ctrl.AddParameter("Gather", AnimatorControllerParameterType.Trigger);
            var gather = asm.AddState("Gather");
            gather.motion = Clip(clips, "Fixing_Kneeling");
            var toGather = asm.AddAnyStateTransition(gather);
            toGather.hasExitTime = false;
            toGather.duration = 0.15f;
            toGather.canTransitionToSelf = false;
            toGather.AddCondition(AnimatorConditionMode.If, 0, "Gather");
            toGather.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var fromGather = gather.AddTransition(empty);
            fromGather.hasExitTime = true;
            fromGather.exitTime = 0.95f;
            fromGather.duration = 0.15f;

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
