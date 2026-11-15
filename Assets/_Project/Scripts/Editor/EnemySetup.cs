// M7: 仮の敵「侵食者」（謎の生命体に侵された人間）を作るエディタツール。
// 主人公と同じ人型モデル・アニメーションを使い、材質と性能データだけを変えている。
// 本番では専用モデルに差し替える（Humanoid ならアニメーションはそのまま使える）。
using System.Collections.Generic;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class EnemySetup
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy_Infected.prefab";
        const string DefPath = "Assets/_Project/Data/Enemies/Enemy_Infected.asset";
        const string MovePath = "Assets/_Project/Data/Movement/Move_Infected.asset";
        const string MatDir = "Assets/_Project/Art/Characters/Enemies/Materials";

        public static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        [MenuItem("Matoi/Setup Enemies (M7 敵の作成)")]
        public static GameObject Setup()
        {
            var def = CreateDefinition();
            var prefab = CreatePrefab(def);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        static EnemyDefinition CreateDefinition()
        {
            EnsureFolder("Assets/_Project/Data/Enemies");
            var move = AssetDatabase.LoadAssetAtPath<MovementProfile>(MovePath);
            if (move == null)
            {
                move = ScriptableObject.CreateInstance<MovementProfile>();
                // 人間よりやや遅く、ぎこちない（プレイヤーは走れば逃げられる）
                move.walkSpeed = 1.2f;
                move.jogSpeed = 3.4f;
                move.sprintSpeed = 5.2f;
                move.acceleration = 11f;
                move.sprintAcceleration = 7f;
                move.deceleration = 14f;
                move.turnSpeedIdle = 600f;
                move.turnSpeedMoving = 420f;
                move.turnSpeedSprint = 260f;
                move.aimTurnSpeed = 540f;
                AssetDatabase.CreateAsset(move, MovePath);
            }
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<EnemyDefinition>();
                AssetDatabase.CreateAsset(def, DefPath);
            }
            def.movement = move;
            EditorUtility.SetDirty(def);
            return def;
        }

        static Material Mat(string name, Color color, float smooth, Texture baseMap = null, Color? emission = null)
        {
            EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            if (baseMap) m.SetTexture("_BaseMap", baseMap);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject CreatePrefab(EnemyDefinition def)
        {
            EnsureFolder("Assets/_Project/Prefabs/Enemies");
            var skinTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Characters/Hero/Textures/T_Hero_Skin.png");
            // 主人公の材質 → 敵の材質 の対応表
            var growth = Mat("M_Enemy_Growth", new Color(0.30f, 0.07f, 0.34f), 0.75f, null, new Color(0.9f, 0.15f, 1.2f) * 0.6f);
            var map = new Dictionary<string, Material>
            {
                { "M_Hero_Skin", Mat("M_Enemy_Skin", new Color(0.78f, 0.66f, 0.95f), 0.45f, skinTex,
                    new Color(0.12f, 0.02f, 0.16f)) }, // 青白い紫の肌（主人公と一目で区別できるように）
                { "M_Hero_Eye", Mat("M_Enemy_Eye", Color.black, 0.9f, null, new Color(2.5f, 0.3f, 3f)) },
                { "M_Hero_Hair", Mat("M_Enemy_Hair", new Color(0.12f, 0.1f, 0.12f), 0.1f) },
                { "M_Hero_Vest", Mat("M_Enemy_Rags", new Color(0.13f, 0.11f, 0.12f), 0.05f) },
                { "M_Hero_Shirt", Mat("M_Enemy_Shirt", new Color(0.30f, 0.27f, 0.24f), 0.05f) },
                { "M_Hero_Pants", Mat("M_Enemy_Pants", new Color(0.17f, 0.15f, 0.14f), 0.05f) },
                { "M_Hero_Boots", growth },
                { "M_Hero_Gloves", growth }, // 手は生命体に侵食されている
            };

            var root = new GameObject("Enemy_Infected");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.35f;
            cc.minMoveDistance = 0f;
            var motor = root.AddComponent<CharacterMotor>();
            Set(motor, "profile", def.movement);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(HeroSetup.HeroModel);
            model.name = "Model";
            model.transform.SetParent(visual.transform, false);
            model.transform.localScale = Vector3.one * 1.04f;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                // 髪・ひげ・眉は無し（侵食で抜け落ちた）。シルエットで主人公と区別する
                if (r.name.Contains("Hair") || r.name.Contains("Beard") || r.name.Contains("Eyebrows"))
                {
                    r.enabled = false;
                    continue;
                }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && map.TryGetValue(mats[i].name, out var m)) mats[i] = m;
                r.sharedMaterials = mats;
            }
            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = HeroSetup.Controller;
            animator.avatar = HeroSetup.HeroAvatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var charAnim = root.AddComponent<CharacterAnimator>();
            Set(charAnim, "motor", motor);
            Set(charAnim, "animator", animator);
            Set(charAnim, "leanRoot", visual.transform);
            var so = new SerializedObject(charAnim);
            so.FindProperty("sprintClipSpeed").floatValue = HeroSetup.LastSpeeds.sprint > 0 ? HeroSetup.LastSpeeds.sprint : 7f;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<Health>();
            var melee = root.AddComponent<MeleeAttack>();
            Set(melee, "animator", animator);
            Set(melee, "motor", motor);
            Set(melee, "characterAnimator", charAnim);
            root.AddComponent<HitReaction>();
            var bar = root.AddComponent<FloatingHealthBar>();
            Set(bar, "backgroundMaterial", Unlit("M_UI_BarBack", new Color(0.06f, 0.06f, 0.06f)));
            Set(bar, "fillMaterial", Unlit("M_UI_BarFill_Enemy", new Color(1f, 0.38f, 0.16f)));
            var brain = root.AddComponent<EnemyBrain>();
            Set(brain, "definition", def);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static Material Unlit(string name, Color color)
        {
            string path = $"Assets/_Project/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Matoi] field not found: {target.GetType().Name}.{field}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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
