// 敵を作るエディタツール。基本の「侵食者」と、性格の違う 3 種類（疾駆体・重殻体・吐瘴体）を作る。
// 主人公と同じ人型モデル・アニメーションを使い、大きさ・材質・体に付けた殻や袋で見分ける。
// 本番では専用モデルに差し替える（Humanoid ならアニメーションはそのまま使える）。
// 数値は下の CreateFast / CreateHeavy / CreateRanged にまとめてある。Setup のたびに書き込まれる。
using System;
using System.Collections.Generic;
using System.Linq;
using Matoi.AI;
using Matoi.Characters;
using Matoi.Combat;
using Matoi.Items;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Matoi.EditorTools
{
    public static class EnemySetup
    {
        const string PrefabDir = "Assets/_Project/Prefabs/Enemies";
        const string PrefabPath = PrefabDir + "/Enemy_Infected.prefab";
        const string DataDir = "Assets/_Project/Data/Enemies";
        const string DefPath = DataDir + "/Enemy_Infected.asset";
        const string MoveDir = "Assets/_Project/Data/Movement";
        const string MovePath = MoveDir + "/Move_Infected.asset";
        const string MatDir = "Assets/_Project/Art/Characters/Enemies/Materials";
        const string CommonDrops = "Assets/_Project/Data/Items/Loot_EnemyDrop.asset";
        const string EliteDrops = "Assets/_Project/Data/Items/Loot_EliteDrop.asset";

        public static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        /// <summary>種類ごとの見た目の違い。</summary>
        class Look
        {
            public string id;                  // 空なら基本の侵食者
            public float scale = 1.04f, height = 1.8f, radius = 0.35f, barHeight = 2.25f;
            public Color skin = new Color(0.78f, 0.66f, 0.95f), skinGlow = new Color(0.12f, 0.02f, 0.16f);
            public Color growth = new Color(0.30f, 0.07f, 0.34f), growthGlow = new Color(0.9f, 0.15f, 1.2f) * 0.6f;
            public Color eyeGlow = new Color(2.5f, 0.3f, 3f);
            public Action<GameObject, Material> addParts;
            public bool boss;                  // ボス（EnemyBrain の代わりに BossBrain を使う）
        }

        /// <summary>透ける演出用の材質（攻撃範囲の予告、結界）。</summary>
        public static Material FxMaterial(string name, Color color)
        {
            string path = $"Assets/_Project/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>重殻体とボスの体を覆う、角ばった岩のような殻。</summary>
        static void AddRockShell(GameObject model)
        {
            var rock = Mat("M_Enemy_Shell", new Color(0.15f, 0.14f, 0.14f), 0.08f);
            void Plate(HumanBodyBones bone, Vector3 offset, Vector3 size, Vector3 tilt) => Part(model, bone, offset, size, rock, PrimitiveType.Cube, tilt);
            foreach (float side in new[] { -1f, 1f })
            {
                var arm = side < 0f ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                var fore = side < 0f ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                Plate(arm, new Vector3(side * 0.02f, 0.1f, 0f), new Vector3(0.27f, 0.1f, 0.25f), new Vector3(6f, 12f, side * -20f));
                Plate(arm, new Vector3(side * 0.13f, 0.03f, 0.02f), new Vector3(0.17f, 0.09f, 0.2f), new Vector3(-8f, -10f, side * -38f));
                Plate(fore, new Vector3(side * 0.12f, 0.05f, 0f), new Vector3(0.24f, 0.07f, 0.13f), new Vector3(0f, 8f, side * -8f));
                Plate(HumanBodyBones.Chest, new Vector3(side * 0.11f, 0.17f, 0.12f), new Vector3(0.2f, 0.16f, 0.07f), new Vector3(-10f, side * 14f, side * 8f));
                Plate(HumanBodyBones.Chest, new Vector3(side * 0.12f, 0.15f, -0.14f), new Vector3(0.22f, 0.24f, 0.1f), new Vector3(12f, side * -12f, side * -7f));
            }
            Plate(HumanBodyBones.Chest, new Vector3(0f, -0.02f, 0.14f), new Vector3(0.28f, 0.17f, 0.07f), new Vector3(8f, 0f, 3f));
            Plate(HumanBodyBones.Spine, new Vector3(0f, 0.04f, -0.14f), new Vector3(0.3f, 0.2f, 0.09f), new Vector3(-8f, 0f, -4f));
            Plate(HumanBodyBones.Head, new Vector3(0f, 0.13f, 0.01f), new Vector3(0.19f, 0.07f, 0.21f), new Vector3(-8f, 20f, 5f));
        }

        /// <summary>ボス「社の主」を作る。基本の敵と吐瘴体を先に作っておくこと。</summary>
        public static GameObject CreateBoss()
        {
            var def = LoadOrCreate<EnemyDefinition>($"{DataDir}/Enemy_Boss.asset");
            def.displayName = "社の主";
            def.movement = Move("Boss", 1.5f, 3f, 9f, 18f, 24f, 420f);
            EditorUtility.SetDirty(def);
            var prefab = CreatePrefab(def, new Look
            {
                id = "Boss", boss = true, scale = 1.85f, height = 3.2f, radius = 0.75f,
                skin = new Color(0.34f, 0.24f, 0.36f), skinGlow = new Color(0.08f, 0.01f, 0.1f),
                growth = new Color(0.22f, 0.03f, 0.1f), growthGlow = new Color(1.6f, 0.1f, 0.3f) * 0.6f,
                eyeGlow = new Color(4f, 0.4f, 0.5f),
                addParts = (model, _) => AddRockShell(model),
            });
            AssetDatabase.SaveAssets();
            return prefab;
        }

        [MenuItem("Matoi/Setup Enemies (M7 敵の作成)")]
        public static GameObject Setup()
        {
            var prefab = CreatePrefab(CreateDefinition(), new Look());
            CreateFast();
            CreateHeavy();
            CreateRanged();
            AssetDatabase.SaveAssets();
            return prefab;
        }

        /// <summary>基本の敵以外の種類（フィールド配置とウェーブで使う）。</summary>
        public static GameObject VariantPrefab(string id) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Enemy_{id}.prefab");
        public static readonly string[] VariantIds = { "Fast", "Heavy", "Ranged" };

        static EnemyDefinition CreateDefinition()
        {
            EnsureFolder(DataDir);
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
            def.drops = AssetDatabase.LoadAssetAtPath<LootTable>(CommonDrops);
            EditorUtility.SetDirty(def);
            return def;
        }

        // ---------- 種類ごとのデータ ----------
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static MovementProfile Move(string id, float walk, float jog, float sprint, float accel, float decel, float turn)
        {
            var move = LoadOrCreate<MovementProfile>($"{MoveDir}/Move_{id}.asset");
            move.walkSpeed = walk;
            move.jogSpeed = jog;
            move.sprintSpeed = sprint;
            move.acceleration = accel;
            move.sprintAcceleration = accel * 0.7f;
            move.deceleration = decel;
            move.turnSpeedIdle = turn;
            move.turnSpeedMoving = turn * 0.7f;
            move.turnSpeedSprint = turn * 0.45f;
            move.aimTurnSpeed = turn * 0.9f;
            EditorUtility.SetDirty(move);
            return move;
        }

        /// <summary>疾駆体：体力は低いが、プレイヤーの走りに迫る速さで一気に距離を詰める。</summary>
        static void CreateFast()
        {
            var def = LoadOrCreate<EnemyDefinition>($"{DataDir}/Enemy_Fast.asset");
            def.displayName = "疾駆体";
            def.maxHealth = 55f;
            def.movement = Move("Fast", 1.6f, 4.6f, 6.2f, 20f, 20f, 820f);
            def.detectRange = 17f;
            def.hearRange = 6f;
            def.loseRange = 34f;
            def.sprintDistance = 2f;
            def.attackRange = 1.6f;
            def.windupTime = 0.28f;
            def.attackCooldown = 0.75f;
            def.attackDamage = 9f;
            def.attackKnockback = 2.5f;
            def.attackLunge = 5f;
            def.staggerTime = 0.4f;
            def.staggerCooldown = 0.5f;
            def.knockbackScale = 1.2f;
            def.respawnDelay = 20f;
            def.drops = AssetDatabase.LoadAssetAtPath<LootTable>(CommonDrops);
            EditorUtility.SetDirty(def);

            CreatePrefab(def, new Look
            {
                id = "Fast", scale = 0.95f, height = 1.65f, radius = 0.32f, barHeight = 2.05f,
                skin = new Color(0.95f, 0.66f, 0.58f), skinGlow = new Color(0.16f, 0.03f, 0.02f),
                growth = new Color(0.45f, 0.07f, 0.04f), growthGlow = new Color(1.5f, 0.28f, 0.08f) * 0.7f,
                eyeGlow = new Color(3.2f, 0.7f, 0.15f),
                addParts = (model, shell) =>
                {
                    // 前腕から後ろへ伸びる刃のような突起
                    Part(model, HumanBodyBones.LeftLowerArm, new Vector3(-0.08f, 0.04f, -0.04f), new Vector3(0.3f, 0.09f, 0.035f), shell);
                    Part(model, HumanBodyBones.RightLowerArm, new Vector3(0.08f, 0.04f, -0.04f), new Vector3(0.3f, 0.09f, 0.035f), shell);
                    Part(model, HumanBodyBones.Spine, new Vector3(0f, 0.1f, -0.16f), new Vector3(0.1f, 0.34f, 0.16f), shell);
                },
            });
        }

        /// <summary>重殻体：遅いが硬く、一撃が重い。殻のせいで銃が効きにくく、近接攻撃がよく効く。</summary>
        static void CreateHeavy()
        {
            var def = LoadOrCreate<EnemyDefinition>($"{DataDir}/Enemy_Heavy.asset");
            def.displayName = "重殻体";
            def.maxHealth = 320f;
            def.gunDamageScale = 0.6f;
            def.movement = Move("Heavy", 1f, 2.2f, 3.2f, 6f, 9f, 300f);
            def.detectRange = 12f;
            def.hearRange = 4f;
            def.loseRange = 24f;
            def.sprintDistance = 100f;
            def.attackRange = 2.2f;
            def.attackReach = 1.35f;
            def.attackRadius = 1.05f;
            def.windupTime = 0.9f;
            def.attackCooldown = 2.2f;
            def.attackDamage = 32f;
            def.attackKnockback = 9f;
            def.attackLunge = 1.5f;
            def.staggerTime = 0.15f;
            def.staggerCooldown = 2.5f;
            def.knockbackScale = 0.25f;
            def.respawnDelay = 40f;
            def.drops = AssetDatabase.LoadAssetAtPath<LootTable>(EliteDrops);
            EditorUtility.SetDirty(def);

            CreatePrefab(def, new Look
            {
                id = "Heavy", scale = 1.28f, height = 2.25f, radius = 0.5f, barHeight = 2.85f,
                skin = new Color(0.55f, 0.50f, 0.47f), skinGlow = new Color(0.03f, 0.02f, 0.01f),
                growth = new Color(0.17f, 0.15f, 0.13f), growthGlow = new Color(0.9f, 0.45f, 0.08f) * 0.25f,
                eyeGlow = new Color(3f, 1.5f, 0.2f),
                addParts = (model, _) => AddRockShell(model),
            });
        }

        /// <summary>吐瘴体：距離を保って弾を吐く。近づかれると後ずさるので、物陰から詰めると倒しやすい。</summary>
        static void CreateRanged()
        {
            var shot = Mat("M_Enemy_Projectile", new Color(0.45f, 0.9f, 0.2f), 0.6f, null, new Color(1.2f, 3f, 0.4f));
            var def = LoadOrCreate<EnemyDefinition>($"{DataDir}/Enemy_Ranged.asset");
            def.displayName = "吐瘴体";
            def.maxHealth = 70f;
            def.movement = Move("Ranged", 1.3f, 3f, 4.2f, 10f, 14f, 600f);
            def.detectRange = 15f;
            def.hearRange = 4f;
            def.loseRange = 26f;
            def.attackRange = 1.5f;
            def.windupTime = 0.45f;
            def.attackCooldown = 1.4f;
            def.attackDamage = 8f;
            def.attackKnockback = 3f;
            def.attackLunge = 2f;
            def.ranged = true;
            def.rangedRange = 13f;
            def.rangedPreferredDistance = 9f;
            def.rangedMinDistance = 5f;
            def.rangedWindup = 0.7f;
            def.rangedCooldown = 2.6f;
            def.projectileDamage = 12f;
            def.projectileSpeed = 13f;
            def.projectileMaterial = shot;
            def.projectileColor = new Color(0.55f, 1f, 0.25f);
            def.staggerTime = 0.4f;
            def.staggerCooldown = 0.8f;
            def.respawnDelay = 30f;
            def.drops = AssetDatabase.LoadAssetAtPath<LootTable>(CommonDrops);
            EditorUtility.SetDirty(def);

            CreatePrefab(def, new Look
            {
                id = "Ranged", scale = 1.02f,
                skin = new Color(0.68f, 0.82f, 0.58f), skinGlow = new Color(0.03f, 0.1f, 0.01f),
                growth = new Color(0.20f, 0.36f, 0.08f), growthGlow = new Color(0.55f, 1.3f, 0.15f) * 0.7f,
                eyeGlow = new Color(1.2f, 3.2f, 0.3f),
                addParts = (model, shell) =>
                {
                    // ふくらんだのどと、背中の毒袋
                    Part(model, HumanBodyBones.Neck, new Vector3(0f, -0.02f, 0.08f), new Vector3(0.2f, 0.17f, 0.2f), shell);
                    Part(model, HumanBodyBones.Chest, new Vector3(-0.1f, 0.12f, -0.17f), new Vector3(0.27f, 0.3f, 0.25f), shell);
                    Part(model, HumanBodyBones.Chest, new Vector3(0.11f, 0.06f, -0.16f), new Vector3(0.22f, 0.24f, 0.21f), shell);
                    Part(model, HumanBodyBones.Spine, new Vector3(0f, 0.02f, -0.17f), new Vector3(0.18f, 0.19f, 0.17f), shell);
                },
            });
        }

        /// <summary>骨に部品を付ける。位置と傾きはモデルの向き（x=右、y=上、z=前）で指定する。</summary>
        static void Part(GameObject model, HumanBodyBones boneId, Vector3 offset, Vector3 size, Material material,
                         PrimitiveType shape = PrimitiveType.Sphere, Vector3 tilt = default)
        {
            string humanName = HumanTrait.BoneName[(int)boneId];
            string boneName = HeroSetup.HeroAvatar.humanDescription.human.FirstOrDefault(h => h.humanName == humanName).boneName;
            var bone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == boneName);
            if (!bone) { Debug.LogWarning("[Matoi] 敵の部品を付ける骨がありません: " + boneId); return; }

            float scale = model.transform.lossyScale.x;
            var part = GameObject.CreatePrimitive(shape);
            part.name = "Part_" + boneId;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.transform.SetPositionAndRotation(bone.position + model.transform.rotation * (offset * scale), model.transform.rotation * Quaternion.Euler(tilt));
            part.transform.localScale = size * scale;
            part.transform.SetParent(bone, true);
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

        static GameObject CreatePrefab(EnemyDefinition def, Look look)
        {
            EnsureFolder(PrefabDir);
            string suffix = string.IsNullOrEmpty(look.id) ? "" : "_" + look.id;
            string objectName = string.IsNullOrEmpty(look.id) ? "Enemy_Infected" : "Enemy_" + look.id;
            var skinTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Characters/Hero/Textures/T_Hero_Skin.png");
            // 主人公の材質 → 敵の材質 の対応表
            var growth = Mat("M_Enemy_Growth" + suffix, look.growth, 0.75f, null, look.growthGlow);
            var map = new Dictionary<string, Material>
            {
                { "M_Hero_Skin", Mat("M_Enemy_Skin" + suffix, look.skin, 0.45f, skinTex, look.skinGlow) }, // 主人公と一目で区別できる肌の色
                { "M_Hero_Eye", Mat("M_Enemy_Eye" + suffix, Color.black, 0.9f, null, look.eyeGlow) },
                { "M_Hero_Hair", Mat("M_Enemy_Hair", new Color(0.12f, 0.1f, 0.12f), 0.1f) },
                { "M_Hero_Vest", Mat("M_Enemy_Rags", new Color(0.13f, 0.11f, 0.12f), 0.05f) },
                { "M_Hero_Shirt", Mat("M_Enemy_Shirt", new Color(0.30f, 0.27f, 0.24f), 0.05f) },
                { "M_Hero_Pants", Mat("M_Enemy_Pants", new Color(0.17f, 0.15f, 0.14f), 0.05f) },
                { "M_Hero_Boots", growth },
                { "M_Hero_Gloves", growth }, // 手は生命体に侵食されている
            };

            var root = new GameObject(objectName);
            var cc = root.AddComponent<CharacterController>();
            cc.height = look.height;
            cc.radius = look.radius;
            cc.center = new Vector3(0, look.height * 0.5f, 0);
            cc.stepOffset = 0.35f;
            cc.minMoveDistance = 0f;
            var motor = root.AddComponent<CharacterMotor>();
            Set(motor, "profile", def.movement);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(HeroSetup.HeroModel);
            model.name = "Model";
            model.transform.SetParent(visual.transform, false);
            model.transform.localScale = Vector3.one * look.scale;
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
            look.addParts?.Invoke(model, growth);

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
            if (look.boss)
            {
                var boss = root.AddComponent<BossBrain>();
                Set(boss, "shotDefinition", AssetDatabase.LoadAssetAtPath<EnemyDefinition>($"{DataDir}/Enemy_Ranged.asset"));
                Set(boss, "minionPrefab", Prefab.GetComponent<EnemyBrain>());
                Set(boss, "telegraphMaterial", FxMaterial("M_Fx_Telegraph", Color.white));
                var bossPrefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{objectName}.prefab");
                Object.DestroyImmediate(root);
                return bossPrefab;
            }
            var melee = root.AddComponent<MeleeAttack>();
            Set(melee, "animator", animator);
            Set(melee, "motor", motor);
            Set(melee, "characterAnimator", charAnim);
            root.AddComponent<HitReaction>();
            var bar = root.AddComponent<FloatingHealthBar>();
            Set(bar, "backgroundMaterial", Unlit("M_UI_BarBack", new Color(0.06f, 0.06f, 0.06f)));
            Set(bar, "fillMaterial", Unlit("M_UI_BarFill_Enemy", new Color(1f, 0.38f, 0.16f)));
            var barSo = new SerializedObject(bar);
            barSo.FindProperty("height").floatValue = look.barHeight;
            barSo.ApplyModifiedPropertiesWithoutUndo();
            var brain = root.AddComponent<EnemyBrain>();
            Set(brain, "definition", def);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{objectName}.prefab");
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
