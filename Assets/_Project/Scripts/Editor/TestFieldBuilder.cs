// M1: テストフィールド（仮地形）と、光・空・霧・ポストプロセスの土台を自動生成するエディタツール。
// メニュー「Matoi > Build Test Field」で TestField.unity を作り直す。
// ※作り直すとシーンへの手作業の変更は消える。手で編集を始めたら、このツールは使わないこと。
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Matoi.EditorTools
{
    public static class TestFieldBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/TestField.unity";
        const string MatDir = "Assets/_Project/Art/Materials/Blockout";
        const string EnvDir = "Assets/_Project/Settings/Environment";

        static System.Random rng;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        // ---------- 色（仮マテリアル。トゥーン化の段階で差し替える） ----------
        static Material M(string name) => mats[name];

        static void CreateMaterials()
        {
            EnsureFolder(MatDir);
            Mat("Ground_Grass", new Color(0.30f, 0.33f, 0.20f), 0.05f);
            Mat("Ground_Dirt", new Color(0.40f, 0.35f, 0.26f), 0.05f);
            Mat("Asphalt", new Color(0.24f, 0.24f, 0.23f), 0.15f);
            Mat("Asphalt_Patch", new Color(0.13f, 0.13f, 0.13f), 0.1f);
            Mat("RoadLine", new Color(0.78f, 0.76f, 0.68f), 0.1f);
            Mat("Concrete", new Color(0.55f, 0.54f, 0.50f), 0.08f);
            Mat("Concrete_Dark", new Color(0.36f, 0.36f, 0.34f), 0.08f);
            Mat("Paddy_Water", new Color(0.30f, 0.32f, 0.25f), 0.7f);
            Mat("Paddy_Overgrown", new Color(0.42f, 0.45f, 0.18f), 0.05f);
            Mat("Paddy_Dry", new Color(0.46f, 0.39f, 0.27f), 0.03f);
            Mat("Paddy_Ridge", new Color(0.30f, 0.33f, 0.19f), 0.05f);
            Mat("Wall_Plaster", new Color(0.62f, 0.56f, 0.46f), 0.05f);
            Mat("Wall_Wood", new Color(0.34f, 0.24f, 0.16f), 0.08f);
            Mat("Roof_Tile", new Color(0.20f, 0.22f, 0.26f), 0.25f);
            Mat("Roof_Tin", new Color(0.46f, 0.25f, 0.15f), 0.3f, 0.3f);
            Mat("Window_Dark", new Color(0.05f, 0.05f, 0.06f), 0.6f);
            Mat("Rust", new Color(0.42f, 0.22f, 0.12f), 0.25f, 0.35f);
            Mat("Rust_Yellow", new Color(0.55f, 0.42f, 0.18f), 0.25f, 0.2f);
            Mat("Barrel_Blue", new Color(0.15f, 0.26f, 0.38f), 0.3f, 0.3f);
            Mat("Pole_Wood", new Color(0.30f, 0.26f, 0.22f), 0.05f);
            Mat("Wire", new Color(0.05f, 0.05f, 0.05f), 0.2f);
            Mat("Torii_Red", new Color(0.72f, 0.16f, 0.07f), 0.2f);
            Mat("Torii_Black", new Color(0.06f, 0.06f, 0.06f), 0.2f);
            Mat("Stone", new Color(0.48f, 0.47f, 0.43f), 0.1f);
            Mat("Tree_Trunk", new Color(0.24f, 0.17f, 0.12f), 0.05f);
            Mat("Tree_Cedar", new Color(0.12f, 0.20f, 0.12f), 0.05f);
            Mat("Tree_Broadleaf", new Color(0.22f, 0.32f, 0.14f), 0.05f);
            Mat("Mountain", new Color(0.16f, 0.22f, 0.16f), 0.0f);
            Mat("Steel_Dark", new Color(0.20f, 0.20f, 0.22f), 0.35f, 0.6f);
            Mat("Player_Temp", new Color(0.75f, 0.72f, 0.62f), 0.3f);
        }

        static void Mat(string name, Color c, float smooth, float metal = 0f)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            EditorUtility.SetDirty(m);
            mats[name] = m;
        }

        // ---------- 形状ヘルパー ----------
        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale,
            Vector3 euler, string mat)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = M(mat);
            return go;
        }

        static GameObject Box(string n, Transform p, Vector3 pos, Vector3 s, string mat, Vector3 euler = default)
            => Prim(PrimitiveType.Cube, n, p, pos, s, euler, mat);

        // 高さ h の円柱（Unity の Cylinder は高さ 2 なので y は h/2）
        static GameObject Cyl(string n, Transform p, Vector3 basePos, float radius, float h, string mat,
            Vector3 euler = default)
        {
            var go = Prim(PrimitiveType.Cylinder, n, p, Vector3.zero, new Vector3(radius * 2, h / 2, radius * 2),
                euler, mat);
            go.transform.localPosition = basePos + go.transform.localRotation * new Vector3(0, h / 2, 0);
            return go;
        }

        static GameObject Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // 2点を結ぶ細い棒（電線など）
        static void Beam(string n, Transform p, Vector3 a, Vector3 b, float thick, string mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = n;
            go.transform.SetParent(p, false);
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(b - a);
            go.transform.localScale = new Vector3(thick, thick, Vector3.Distance(a, b));
            go.GetComponent<Renderer>().sharedMaterial = M(mat);
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        // ---------- メイン ----------
        [MenuItem("Matoi/Build Test Field (M1 テストフィールド生成)")]
        public static void Build()
        {
            rng = new System.Random(20871023);
            CreateMaterials();
            TuneUrpAsset();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = Group("--- Environment").transform;
            BuildGround(env);
            BuildRoad(env);
            BuildPaddies(env);
            BuildVillage(env);
            BuildShrine(env);
            BuildProps(env);
            BuildTestArea(env);
            BuildTargets(env);
            BuildForest(env);
            BuildVegetation(env);
            BuildBackdrop(env);

            var lighting = Group("--- Lighting").transform;
            var sun = BuildSun(lighting);
            BuildEnvironmentLighting(sun);
            BuildVolume(lighting);

            BuildCamera(PlayerSetup.SpawnPoint);
            PlayerSetup.InstallIntoOpenScene(); // M2: プレイヤーと追従カメラ
            BakeNavMesh(env);                   // M7: 敵が歩ける場所の地図
            PlaceEnemies();                     // M7: 敵

            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeEnvironment();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = list.ToArray();
            if (ToonConverter.Enabled) ToonConverter.ApplyAll(false); // 新しく作られた材質もトゥーンに
            AssetDatabase.SaveAssets();
            Debug.Log("[Matoi] Test field built: " + ScenePath);
        }

        static void TuneUrpAsset()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Assets/_Project/Settings/URP/PC_RPAsset.asset");
            if (rp == null) return;
            rp.msaaSampleCount = 4;      // 見下ろし視点の輪郭のギザギザを抑える
            rp.shadowDistance = 70f;     // カメラから見える範囲に影を出す
            EditorUtility.SetDirty(rp);
        }

        // ---------- 地面 ----------
        static void BuildGround(Transform env)
        {
            var g = Group("Ground", env).transform;
            Box("Ground_Base", g, new Vector3(0, -0.5f, 0), new Vector3(900, 1, 900), "Ground_Grass");
            // 土がむき出しの部分
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3(R(-40, 40), 0.005f, R(-40, 40));
                if (Mathf.Abs(p.x) < 5) continue;
                Box("DirtPatch_" + i, g, p, new Vector3(R(3, 9), 0.01f, R(3, 9)), "Ground_Dirt",
                    new Vector3(0, R(0, 180), 0));
            }
        }

        // ---------- 道路（南北に通る旧県道） ----------
        static void BuildRoad(Transform env)
        {
            var g = Group("Road", env).transform;
            Box("Asphalt", g, new Vector3(0, 0.02f, 0), new Vector3(6, 0.04f, 120), "Asphalt");
            Box("Shoulder_L", g, new Vector3(-3.4f, 0.03f, 0), new Vector3(0.8f, 0.06f, 120), "Concrete_Dark");
            Box("Shoulder_R", g, new Vector3(3.4f, 0.03f, 0), new Vector3(0.8f, 0.06f, 120), "Concrete_Dark");
            Box("EdgeLine_L", g, new Vector3(-2.75f, 0.045f, 0), new Vector3(0.12f, 0.01f, 120), "RoadLine");
            Box("EdgeLine_R", g, new Vector3(2.75f, 0.045f, 0), new Vector3(0.12f, 0.01f, 120), "RoadLine");
            for (float z = -58; z < 60; z += 6)
                Box("CenterDash", g, new Vector3(0, 0.045f, z), new Vector3(0.15f, 0.01f, 3f), "RoadLine");
            // ひび割れ・補修跡
            for (int i = 0; i < 14; i++)
                Box("Patch_" + i, g, new Vector3(R(-2.5f, 2.5f), 0.042f, R(-55, 55)),
                    new Vector3(R(0.6f, 2.2f), 0.01f, R(0.8f, 3f)), "Asphalt_Patch", new Vector3(0, R(-30, 30), 0));
            // 用水路（道路の西側）
            Box("Canal_Wall", g, new Vector3(-4.6f, 0.15f, 0), new Vector3(1.4f, 0.3f, 120), "Concrete");
            Box("Canal_Water", g, new Vector3(-4.6f, 0.31f, 0), new Vector3(0.9f, 0.02f, 120), "Paddy_Water");
        }

        // ---------- 田んぼ（道路の西側） ----------
        static void BuildPaddies(Transform env)
        {
            var g = Group("Paddies", env).transform;
            const float size = 8f, gap = 0.6f;
            for (int col = 0; col < 3; col++)
            for (int row = 0; row < 6; row++)
            {
                float cx = -10f - col * (size + gap);
                float cz = -24f + row * (size + gap);
                var plot = Group($"Plot_{col}_{row}", g).transform;
                plot.localPosition = new Vector3(cx, 0, cz);
                double t = rng.NextDouble();
                string surf = t < 0.45 ? "Paddy_Water" : t < 0.8 ? "Paddy_Overgrown" : "Paddy_Dry";
                float y = surf == "Paddy_Overgrown" ? 0.12f : 0.03f;
                Box("Surface", plot, new Vector3(0, y / 2, 0), new Vector3(size, y, size), surf);
                // 畦（あぜ）
                Box("Ridge_N", plot, new Vector3(0, 0.15f, size / 2), new Vector3(size + gap, 0.3f, gap), "Paddy_Ridge");
                Box("Ridge_S", plot, new Vector3(0, 0.15f, -size / 2), new Vector3(size + gap, 0.3f, gap), "Paddy_Ridge");
                Box("Ridge_E", plot, new Vector3(size / 2, 0.15f, 0), new Vector3(gap, 0.3f, size), "Paddy_Ridge");
                Box("Ridge_W", plot, new Vector3(-size / 2, 0.15f, 0), new Vector3(gap, 0.3f, size), "Paddy_Ridge");
            }
        }

        // ---------- 集落（道路の東側） ----------
        static void BuildVillage(Transform env)
        {
            var g = Group("Village", env).transform;
            if (BuildingModel("Minka") != null)
            {
                // Blender で作った日本家屋（Tools/Blender/build_japanese_buildings.py）。正面を道路（西）へ向ける
                PlaceBuilding("Minka", g, 13f, -20f, Vector3.left, -3f);
                PlaceBuilding("Barn", g, 25f, -14f, Vector3.left, 6f);
                PlaceBuilding("Minka_Ruined", g, 14f, -2f, Vector3.left, 4f);
                PlaceBuilding("Kura", g, 27f, 5f, Vector3.left, -8f);
                PlaceBuilding("Minka", g, 14f, 14f, Vector3.left, 2f);
                // 道ばたの小物
                PlaceBuilding("VendingMachine", g, 5.7f, -3.6f, Vector3.left, 0f, occluder: false);
                PlaceBuilding("BusStop", g, 6.1f, 6.5f, Vector3.left, 0f, occluder: false);
            }
            else
            {
                // x, z, 幅, 奥行, 高さ, 向き, 損壊度(0:無事 1:屋根崩落 2:壁のみ), 屋根材
                BuildHouse(g, 13, -20, 7, 5.5f, 3.0f, 0, 0, "Roof_Tile");
                BuildHouse(g, 25, -14, 6, 5, 2.8f, 8, 1, "Roof_Tin");
                BuildHouse(g, 14, -2, 8, 6, 3.2f, -4, 1, "Roof_Tile");
                BuildHouse(g, 27, 5, 6, 5, 2.8f, 12, 2, "Roof_Tile");
                BuildHouse(g, 14, 14, 7, 5, 3.0f, 3, 0, "Roof_Tin");
            }

            // ブロック塀（道路沿い。腰の高さ＝将来の遮蔽物テスト用）
            var walls = Group("BlockWalls", g).transform;
            for (float z = -28; z < 24; z += 2.4f)
            {
                if (rng.NextDouble() < 0.18) continue; // 崩れて無い区間
                float h = rng.NextDouble() < 0.25 ? R(0.5f, 1.0f) : 1.4f;
                Box("Wall", walls, new Vector3(7.5f, h / 2, z), new Vector3(0.2f, h, 2.4f), "Concrete");
            }
        }

        const string BuildingDir = "Assets/_Project/Art/Environment/Buildings";

        static GameObject BuildingModel(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{BuildingDir}/{name}.fbx");

        /// <summary>
        /// 建物モデルを置き、子の "Entrance"（正面の目印）が face の方向を向くよう回す。
        /// 名前は "House_x_z"（カメラ透過テストなどが名前で探すため）。
        /// </summary>
        static GameObject PlaceBuilding(string model, Transform parent, float x, float z, Vector3 face, float extraYaw,
            bool occluder = true)
        {
            var prefab = BuildingModel(model);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = occluder ? $"House_{x}_{z}" : model;
            go.transform.position = new Vector3(x, 0f, z);
            Transform entrance = null;
            foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "Entrance") entrance = t;
            if (entrance)
            {
                Vector3 local = entrance.position - go.transform.position;
                local.y = 0f;
                if (local.sqrMagnitude > 0.01f) // 水平に回すだけ（真逆の向きでも上下が反転しないように）
                    go.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(local, face, Vector3.up), Vector3.up)
                                            * go.transform.rotation;
            }
            go.transform.rotation = Quaternion.Euler(0, extraYaw, 0) * go.transform.rotation;
            if (occluder) go.AddComponent<Matoi.CameraSystem.CameraOccluder>(); // プレイヤーを隠したら透ける
            return go;
        }

        static void BuildHouse(Transform parent, float x, float z, float w, float d, float h, float rotY,
            int ruin, string roofMat)
        {
            var house = Group($"House_{x}_{z}", parent).transform;
            house.localPosition = new Vector3(x, 0, z);
            house.localRotation = Quaternion.Euler(0, rotY, 0);
            house.gameObject.AddComponent<Matoi.CameraSystem.CameraOccluder>(); // M3: プレイヤーを隠したら透ける

            Box("Foundation", house, new Vector3(0, 0.15f, 0), new Vector3(w + 0.4f, 0.3f, d + 0.4f), "Concrete_Dark");
            float wallH = ruin == 2 ? h * 0.55f : h;
            Box("Walls", house, new Vector3(0, 0.3f + wallH / 2, 0), new Vector3(w, wallH, d), "Wall_Plaster");
            // 柱（木造らしさ）
            for (int i = 0; i < 4; i++)
            {
                float px = (i % 2 == 0 ? -1 : 1) * (w / 2 + 0.05f);
                float pz = (i < 2 ? -1 : 1) * (d / 2 + 0.05f);
                Box("Post", house, new Vector3(px, 0.3f + wallH / 2, pz), new Vector3(0.22f, wallH, 0.22f), "Wall_Wood");
            }
            // 窓・戸（道路側＝西向きの面）
            if (ruin < 2)
            {
                Box("Window_1", house, new Vector3(-w / 2 - 0.02f, 1.7f, -d / 4), new Vector3(0.05f, 1.0f, 1.4f), "Window_Dark");
                Box("Door", house, new Vector3(-w / 2 - 0.02f, 1.3f, d / 4), new Vector3(0.05f, 2.0f, 1.2f), "Wall_Wood");
            }

            if (ruin == 2)
            {
                // 壁だけ残った廃屋：瓦礫を散らす
                for (int i = 0; i < 10; i++)
                    Box("Rubble", house, new Vector3(R(-w / 2, w / 2), R(0.3f, 0.6f), R(-d / 2, d / 2)),
                        new Vector3(R(0.4f, 1.4f), R(0.15f, 0.5f), R(0.4f, 1.4f)),
                        rng.NextDouble() < 0.5 ? "Roof_Tile" : "Wall_Wood", new Vector3(R(-20, 20), R(0, 180), R(-20, 20)));
                return;
            }

            // 切妻屋根（棟は x 方向）
            const float pitch = 28f;
            float slabLen = (d / 2) / Mathf.Cos(pitch * Mathf.Deg2Rad) + 0.7f;
            float rise = (d / 2) * Mathf.Tan(pitch * Mathf.Deg2Rad);
            float top = 0.3f + h;
            Box("Roof_Front", house, new Vector3(0, top + rise / 2, -d / 4 - 0.15f),
                new Vector3(w + 1.2f, 0.22f, slabLen), roofMat, new Vector3(-pitch, 0, 0));
            if (ruin == 0)
            {
                Box("Roof_Back", house, new Vector3(0, top + rise / 2, d / 4 + 0.15f),
                    new Vector3(w + 1.2f, 0.22f, slabLen), roofMat, new Vector3(pitch, 0, 0));
                Box("Roof_Ridge", house, new Vector3(0, top + rise + 0.05f, 0), new Vector3(w + 1.3f, 0.25f, 0.4f), roofMat);
            }
            else
            {
                // 屋根の片側が崩れ落ちている
                Box("Roof_Fallen", house, new Vector3(0.5f, 0.9f, d / 2 + 1.4f),
                    new Vector3(w * 0.8f, 0.22f, slabLen * 0.9f), roofMat, new Vector3(52, 6, 4));
            }
        }

        // ---------- 神社（北東の高台） ----------
        static void BuildShrine(Transform env)
        {
            var g = Group("Shrine", env).transform;
            const float cx = 22f;
            Box("Hill", g, new Vector3(cx, 1.2f, 34), new Vector3(16, 2.4f, 14), "Ground_Dirt");
            Box("Hill_Wall", g, new Vector3(cx, 1.2f, 26.9f), new Vector3(16, 2.4f, 0.3f), "Stone");
            // 石段（段差の昇り降りテスト用。1段 0.3m）
            for (int i = 0; i < 8; i++)
            {
                float top = (i + 1) * 0.3f;
                Box("Step_" + i, g, new Vector3(cx, top / 2, 26.8f - (8 - i) * 0.45f + 0.225f),
                    new Vector3(3.2f, top, 0.45f), "Stone");
            }
            // 鳥居
            var torii = Group("Torii", g).transform;
            torii.localPosition = new Vector3(cx, 0, 21.5f);
            Cyl("Pillar_L", torii, new Vector3(-1.7f, 0, 0), 0.22f, 4.3f, "Torii_Red", new Vector3(0, 0, 2));
            Cyl("Pillar_R", torii, new Vector3(1.7f, 0, 0), 0.22f, 4.3f, "Torii_Red", new Vector3(0, 0, -2));
            Box("Nuki", torii, new Vector3(0, 3.5f, 0), new Vector3(4.6f, 0.22f, 0.22f), "Torii_Red");
            Box("Shimaki", torii, new Vector3(0, 4.3f, 0), new Vector3(5.4f, 0.28f, 0.4f), "Torii_Red");
            Box("Kasagi", torii, new Vector3(0, 4.55f, 0), new Vector3(6.0f, 0.22f, 0.5f), "Torii_Black");
            // 祠
            Box("Hokora", g, new Vector3(cx, 3.4f, 36), new Vector3(3f, 2.0f, 2.6f), "Wall_Wood");
            Box("Hokora_Roof", g, new Vector3(cx, 4.6f, 36), new Vector3(3.8f, 0.3f, 3.4f), "Roof_Tile", new Vector3(0, 0, 0));
            // 御神木
            Tree(g, new Vector3(cx - 5.5f, 2.4f, 33), true, 1.6f);
            Tree(g, new Vector3(cx + 5.5f, 2.4f, 37), true, 1.5f);
        }

        // ---------- 小物（電柱・廃車・ドラム缶・バス） ----------
        static void BuildProps(Transform env)
        {
            var g = Group("Props", env).transform;

            // 電柱と電線
            var poles = Group("UtilityPoles", g).transform;
            var tops = new List<(Vector3 l, Vector3 r)>();
            for (float z = -54; z <= 54; z += 13)
            {
                var pole = Group("Pole", poles).transform;
                pole.localPosition = new Vector3(4.4f, 0, z);
                pole.localRotation = Quaternion.Euler(R(-4, 4), 0, R(-7, 3));
                Cyl("Post", pole, Vector3.zero, 0.16f, 9f, "Pole_Wood");
                Box("CrossArm", pole, new Vector3(0, 8.4f, 0), new Vector3(1.6f, 0.12f, 0.12f), "Steel_Dark");
                tops.Add((pole.TransformPoint(new Vector3(-0.7f, 8.5f, 0)), pole.TransformPoint(new Vector3(0.7f, 8.5f, 0))));
            }
            for (int i = 0; i + 1 < tops.Count; i++)
            {
                if (i == 3) continue; // 1区間は断線
                Beam("Wire", poles, tops[i].l, tops[i + 1].l, 0.035f, "Wire");
                Beam("Wire", poles, tops[i].r, tops[i + 1].r, 0.035f, "Wire");
            }

            Car(g, new Vector3(1.4f, 0, -6f), 24, 0);
            Car(g, new Vector3(-1.6f, 0, 15f), -72, 9);

            // 錆びたバス（北側の空き地）
            var bus = Group("Bus", g).transform;
            bus.localPosition = new Vector3(-12f, 0, 34f);
            bus.localRotation = Quaternion.Euler(0, 78, 3);
            Box("Body", bus, new Vector3(0, 1.6f, 0), new Vector3(2.5f, 2.6f, 9.5f), "Rust_Yellow");
            Box("Windows_L", bus, new Vector3(-1.26f, 2.2f, 0.4f), new Vector3(0.04f, 0.9f, 7.6f), "Window_Dark");
            Box("Windows_R", bus, new Vector3(1.26f, 2.2f, 0.4f), new Vector3(0.04f, 0.9f, 7.6f), "Window_Dark");
            Box("Roof", bus, new Vector3(0, 2.95f, 0), new Vector3(2.3f, 0.12f, 9.2f), "Rust");

            // ドラム缶
            Barrels(g, new Vector3(5.6f, 0, -9f), 5);
            Barrels(g, new Vector3(-9f, 0, 30f), 4);
            Barrels(g, new Vector3(18f, 0, -8f), 3);
        }

        static void Car(Transform parent, Vector3 pos, float rotY, float tiltZ)
        {
            var car = Group("WreckedCar", parent).transform;
            car.localPosition = pos;
            car.localRotation = Quaternion.Euler(0, rotY, tiltZ);
            Box("Body", car, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 0.75f, 4.3f), "Rust");
            Box("Cabin", car, new Vector3(0, 1.2f, -0.3f), new Vector3(1.6f, 0.6f, 2.2f), "Rust");
            Box("Glass", car, new Vector3(0, 1.2f, 0.82f), new Vector3(1.5f, 0.5f, 0.04f), "Window_Dark", new Vector3(-30, 0, 0));
        }

        static void Barrels(Transform parent, Vector3 center, int count)
        {
            var g = Group("Barrels", parent).transform;
            g.localPosition = center;
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(R(-1.2f, 1.2f), 0, R(-1.2f, 1.2f));
                string m = rng.NextDouble() < 0.6 ? "Rust" : "Barrel_Blue";
                if (rng.NextDouble() < 0.25)
                    Cyl("Barrel_Fallen", g, p + new Vector3(0, 0.3f, 0), 0.3f, 0.9f, m, new Vector3(90, R(0, 180), 0));
                else
                    Cyl("Barrel", g, p, 0.3f, 0.9f, m);
            }
        }

        // ---------- 移動テスト区画（南東の工事現場跡） ----------
        static void BuildTestArea(Transform env)
        {
            var g = Group("MovementTestArea", env).transform;
            g.localPosition = new Vector3(16, 0, -36);
            Box("Pad", g, new Vector3(0, 0.02f, 0), new Vector3(18, 0.04f, 10), "Concrete_Dark");
            // 坂（約20度）
            Box("Ramp_20deg", g, new Vector3(-5, 0.8f, 0), new Vector3(3, 0.3f, 5), "Concrete", new Vector3(-20, 0, 0));
            Box("Ramp_Top", g, new Vector3(-5, 1.55f, 4f), new Vector3(3, 0.3f, 3), "Concrete");
            // 階段（1段 0.25m）
            for (int i = 0; i < 6; i++)
                Box("Stair_" + i, g, new Vector3(0, (i + 1) * 0.125f, -2 + i * 0.4f), new Vector3(2.5f, (i + 1) * 0.25f, 0.4f), "Concrete");
            // 低い障害物（0.5m）と遮蔽壁（1.2m）
            Box("LowObstacle_0.5m", g, new Vector3(4.5f, 0.25f, -2), new Vector3(2, 0.5f, 0.6f), "Concrete_Dark");
            Box("CoverWall_1.2m", g, new Vector3(4.5f, 0.6f, 2), new Vector3(3, 1.2f, 0.4f), "Concrete");
            // 資材の山
            Box("Pipe_Stack", g, new Vector3(7.5f, 0.4f, 0), new Vector3(1.5f, 0.8f, 3.5f), "Rust");
        }

        // ---------- M7: ナビメッシュ（敵の経路探索用）と敵の配置 ----------
        public static readonly Vector3[] EnemyPositions =
        {
            new Vector3(-1.5f, 0f, 22f),   // 道路の北
            new Vector3(28f, 0f, 12f),     // 集落の東
            new Vector3(-18f, 0.12f, 6f),  // 田んぼ
            new Vector3(22f, 2.4f, 33f),   // 神社の高台
        };

        static void BakeNavMesh(Transform env)
        {
            var surface = env.gameObject.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Volume;
            surface.center = new Vector3(0, 5, 0);
            surface.size = new Vector3(160, 30, 160);   // プレイ範囲だけ（地面全体は大きすぎる）
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            string path = "Assets/_Project/Scenes/TestField/NavMesh_Field.asset";
            EnsureFolder("Assets/_Project/Scenes/TestField");
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        static void PlaceEnemies()
        {
            var prefab = EnemySetup.Setup();
            var g = Group("--- Enemies").transform;
            for (int i = 0; i < EnemyPositions.Length; i++)
            {
                var e = (GameObject)PrefabUtility.InstantiatePrefab(prefab, g);
                e.name = $"Enemy_Infected_{i}";
                e.transform.SetPositionAndRotation(EnemyPositions[i], Quaternion.Euler(0, R(0, 360), 0));
            }

            // M8: ウェーブ式の戦闘テスト（Start / F2 で開始）
            var director = new GameObject("WaveDirector").AddComponent<Matoi.AI.WaveDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("enemyPrefab").objectReferenceValue = prefab.GetComponent<Matoi.AI.EnemyBrain>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- M6: 訓練用のかかし（撃つ・殴るの的） ----------
        public static readonly Vector3[] TargetPositions =
        {
            new Vector3(1.5f, 0f, -10f),   // スタート地点の正面 6m
            new Vector3(-1.8f, 0f, -11.5f),
            new Vector3(4.6f, 0f, -12f),
            new Vector3(-9.5f, 0.12f, -14f), // 田んぼの中
        };

        static void BuildTargets(Transform env)
        {
            var g = Group("Targets", env).transform;
            for (int i = 0; i < TargetPositions.Length; i++)
            {
                var root = new GameObject($"Scarecrow_{i}");
                root.transform.SetParent(g, false);
                root.transform.position = TargetPositions[i];
                root.transform.rotation = Quaternion.Euler(0, 180 + R(-25, 25), 0);
                var col = root.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0, 0.9f, 0);
                col.height = 1.8f;
                col.radius = 0.35f;
                root.AddComponent<Matoi.Combat.Health>();
                var dummy = root.AddComponent<Matoi.Combat.TargetDummy>();

                // 見た目：かかし（根元を原点にして揺らす）
                var v = Group("Visual", root.transform).transform;
                Cyl("Pole", v, Vector3.zero, 0.05f, 1.9f, "Pole_Wood");
                Box("Arms", v, new Vector3(0, 1.35f, 0), new Vector3(1.4f, 0.06f, 0.06f), "Pole_Wood");
                Cyl("Body", v, new Vector3(0, 0.75f, 0), 0.24f, 0.75f, "Paddy_Overgrown");
                Box("Sleeves", v, new Vector3(0, 1.32f, 0), new Vector3(1.1f, 0.2f, 0.22f), "Barrel_Blue");
                var head = Prim(PrimitiveType.Sphere, "Head", v, new Vector3(0, 1.68f, 0), Vector3.one * 0.32f, Vector3.zero, "Wall_Plaster");
                Cyl("Hat", v, new Vector3(0, 1.8f, 0), 0.32f, 0.06f, "Paddy_Dry");
                foreach (var c in v.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var so = new SerializedObject(dummy);
                so.FindProperty("visual").objectReferenceValue = v;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---------- 森（フィールドの外周） ----------
        static void BuildForest(Transform env)
        {
            var g = Group("Forest", env).transform;
            int n = 0;
            while (n < 220)
            {
                var p = new Vector3(R(-75, 75), 0, R(-75, 75));
                bool inside = p.x > -42 && p.x < 36 && p.z > -46 && p.z < 46;
                if (inside) continue;
                Tree(g, p, rng.NextDouble() < 0.65, R(0.8f, 1.4f));
                n++;
            }
            // 集落の中の木
            Tree(g, new Vector3(20, 0, -6), false, 1.0f);
            Tree(g, new Vector3(31, 0, -20), false, 1.2f);
            Tree(g, new Vector3(9.5f, 0, 21), true, 0.9f);
        }

        // ---------- 外部アセット（Quaternius Stylized Nature MegaKit / CC0） ----------
        const string NatureDir = "Assets/_Project/Art/Environment/Nature/Models";

        static GameObject NatureModel(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/{name}.fbx");

        static bool HasNature => NatureModel("Pine_1") != null;

        /// <summary>モデルを置き、指定の高さになるよう拡大縮小する。collider: 0=なし 1=幹(カプセル) 2=箱</summary>
        static GameObject PlaceModel(string name, Transform parent, Vector3 pos, float targetHeight, int collider = 0)
        {
            var prefab = NatureModel(name);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return go;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            if (targetHeight > 0 && b.size.y > 0.01f)
                go.transform.localScale *= targetHeight / b.size.y;
            if (collider == 1)
            {
                var c = go.AddComponent<CapsuleCollider>();
                float s = go.transform.lossyScale.y;
                c.radius = 0.35f / s;
                c.height = 4f / s;
                c.center = new Vector3(0, 2f / s, 0);
            }
            else if (collider == 2)
            {
                var c = go.AddComponent<BoxCollider>();
                // ローカル空間の大きさに直す
                rs = go.GetComponentsInChildren<Renderer>();
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                c.center = go.transform.InverseTransformPoint(b.center);
                var ls = go.transform.lossyScale;
                c.size = new Vector3(b.size.x / ls.x, b.size.y / ls.y, b.size.z / ls.z) * 0.85f;
            }
            return go;
        }

        static string Pick(params string[] names) => names[rng.Next(names.Length)];

        static void BuildVegetation(Transform env)
        {
            if (!HasNature) return;
            var g = Group("Vegetation", env).transform;

            // 荒れた田んぼ・道端・集落に伸び放題の草
            var grass = Group("Grass", g).transform;
            for (int i = 0; i < 1600; i++)
            {
                var p = new Vector3(R(-40, 36), 0, R(-44, 44));
                if (Mathf.Abs(p.x) < 3.2f) continue;                       // 道路の上には生やさない
                if (p.x > 9 && p.x < 32 && p.z > -26 && p.z < 22 && rng.NextDouble() < 0.6) continue; // 家の周りは控えめ
                if (p.x > 7 && p.x < 25 && p.z > -42 && p.z < -30) continue; // 移動テスト区画は空ける
                string n = Pick("Grass_Common_Tall", "Grass_Common_Short", "Grass_Wispy_Tall", "Grass_Wispy_Short");
                PlaceModel(n, grass, p, R(0.35f, 0.9f));
            }
            // 道路脇（路肩）の草むら
            for (float z = -58; z < 58; z += 1.6f)
            {
                if (rng.NextDouble() < 0.5) PlaceModel("Grass_Wispy_Tall", grass, new Vector3(R(3.6f, 4.2f), 0, z + R(-0.6f, 0.6f)), R(0.5f, 0.9f));
                if (rng.NextDouble() < 0.3) PlaceModel("Grass_Common_Tall", grass, new Vector3(R(-3.9f, -3.6f), 0.3f, z + R(-0.6f, 0.6f)), R(0.4f, 0.7f));
            }

            // 低木・シダ
            var bushes = Group("Bushes", g).transform;
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector3(R(-45, 40), 0, R(-50, 50));
                if (Mathf.Abs(p.x) < 4.5f) continue;
                PlaceModel(Pick("Fern_1", "Grass_Common_Tall", "Grass_Wispy_Tall"), bushes, p, R(0.7f, 1.4f));
            }

            // 岩（遮蔽物にもなる）
            var rocks = Group("Rocks", g).transform;
            for (int i = 0; i < 26; i++)
            {
                var p = new Vector3(R(-40, 34), 0, R(-46, 46));
                if (Mathf.Abs(p.x) < 5f) continue;
                if (p.x > 7 && p.x < 25 && p.z > -42 && p.z < -30) continue;
                PlaceModel(Pick("Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"), rocks, p, R(0.6f, 1.6f), 2);
            }
            for (int i = 0; i < 60; i++)
                PlaceModel(Pick("Pebble_Round_1", "Pebble_Round_3", "Pebble_Square_2", "Pebble_Square_5"), rocks,
                    new Vector3(R(-40, 34), 0, R(-46, 46)), R(0.1f, 0.3f));

            // 枯れ木（荒廃の目印）
            var dead = Group("DeadTrees", g).transform;
            foreach (var p in new[] { new Vector3(-6.5f, 0, -30), new Vector3(9, 0, 6), new Vector3(33, 0, -2), new Vector3(-20, 0, 24), new Vector3(6, 0, -24) })
                PlaceModel(Pick("DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5"), dead, p, R(5f, 8f), 1);
        }

        static void Tree(Transform parent, Vector3 basePos, bool cedar, float s)
        {
            if (HasNature)
            {
                // 杉林のイメージで針葉樹（Pine）にそろえる。広葉樹（CommonTree）は葉の透過が崩れるため現状不使用
                var m = PlaceModel(Pick("Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5"), parent, basePos,
                    (cedar ? 11f : 8f) * s, 1);
                if (m != null) { m.AddComponent<Matoi.CameraSystem.CameraOccluder>(); return; }
            }
            var t = Group(cedar ? "Cedar" : "Broadleaf", parent).transform;
            t.localPosition = basePos;
            t.localRotation = Quaternion.Euler(0, R(0, 360), 0);
            float trunkH = (cedar ? 5f : 3f) * s;
            Cyl("Trunk", t, Vector3.zero, 0.22f * s, trunkH, "Tree_Trunk");
            if (cedar)
            {
                var c = Prim(PrimitiveType.Capsule, "Foliage", t, new Vector3(0, trunkH + 1.2f * s, 0),
                    new Vector3(2.6f * s, 4.2f * s, 2.6f * s), Vector3.zero, "Tree_Cedar");
                Object.DestroyImmediate(c.GetComponent<Collider>());
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    var f = Prim(PrimitiveType.Sphere, "Foliage", t,
                        new Vector3(R(-0.9f, 0.9f) * s, trunkH + R(0.2f, 1.2f) * s, R(-0.9f, 0.9f) * s),
                        Vector3.one * R(2.4f, 3.4f) * s, Vector3.zero, "Tree_Broadleaf");
                    Object.DestroyImmediate(f.GetComponent<Collider>());
                }
            }
        }

        // ---------- 遠景（山・崩れた高架・鉄塔） ----------
        static void BuildBackdrop(Transform env)
        {
            var g = Group("Backdrop", env).transform;
            void Mountain(Vector3 p, Vector3 s)
            {
                var m = Prim(PrimitiveType.Sphere, "Mountain", g, p, s, Vector3.zero, "Mountain");
                Object.DestroyImmediate(m.GetComponent<Collider>());
            }
            Mountain(new Vector3(0, -10, 150), new Vector3(140, 70, 70));
            Mountain(new Vector3(-110, -10, 120), new Vector3(120, 90, 80));
            Mountain(new Vector3(120, -10, 110), new Vector3(130, 60, 80));
            Mountain(new Vector3(160, -10, -10), new Vector3(80, 70, 160));
            Mountain(new Vector3(-160, -10, -10), new Vector3(80, 60, 160));
            Mountain(new Vector3(0, -10, -170), new Vector3(200, 50, 70));

            // 崩れた高架道路（北側）
            var bridge = Group("CollapsedOverpass", g).transform;
            bridge.localPosition = new Vector3(0, 0, 62);
            for (float x = -90; x <= 90; x += 18)
            {
                if (x > -10 && x < 10) continue; // 中央が落ちている
                Box("Pier", bridge, new Vector3(x, 5, 0), new Vector3(2.2f, 10, 3), "Concrete");
            }
            Box("Deck_W", bridge, new Vector3(-52, 10.6f, 0), new Vector3(80, 1.2f, 11), "Concrete");
            Box("Deck_E", bridge, new Vector3(52, 10.6f, 0), new Vector3(80, 1.2f, 11), "Concrete");
            Box("Deck_Fallen", bridge, new Vector3(0, 4.5f, 0), new Vector3(26, 1.2f, 11), "Concrete_Dark",
                new Vector3(0, 0, -22));
            Box("Rail_W", bridge, new Vector3(-52, 11.8f, -5.3f), new Vector3(80, 1.2f, 0.3f), "Concrete");
            Box("Rail_E", bridge, new Vector3(52, 11.8f, -5.3f), new Vector3(80, 1.2f, 0.3f), "Concrete");

            // 送電鉄塔（遠景のシルエット）
            Pylon(g, new Vector3(-60, 0, 95), 38);
            Pylon(g, new Vector3(75, 0, 85), 32);
        }

        static void Pylon(Transform parent, Vector3 pos, float h)
        {
            var t = Group("Pylon", parent).transform;
            t.localPosition = pos;
            float b = h * 0.18f;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3((i % 2 == 0 ? -1 : 1) * b, 0, (i < 2 ? -1 : 1) * b);
                Beam("Leg", t, t.TransformPoint(c), t.TransformPoint(new Vector3(0, h, 0)), 0.5f, "Steel_Dark");
            }
            for (int k = 1; k <= 3; k++)
            {
                float y = h * (0.45f + k * 0.14f);
                Box("Arm", t, new Vector3(0, y, 0), new Vector3(h * (0.5f - k * 0.08f), 0.4f, 0.4f), "Steel_Dark");
            }
        }

        // ---------- ライティング ----------
        static Light BuildSun(Transform parent)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.80f, 0.60f);   // 夕方寄りの暖色
            light.intensity = 2.2f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            go.transform.rotation = Quaternion.Euler(30f, 55f, 0f); // 低めの角度で長い影
            return light;
        }

        static void BuildEnvironmentLighting(Light sun)
        {
            EnsureFolder(EnvDir);
            string skyPath = EnvDir + "/Sky_Dusk.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.58f, 0.55f, 0.52f));
            sky.SetColor("_GroundColor", new Color(0.32f, 0.30f, 0.27f));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // 影の中も色が見える明るさ（真っ黒な影はトゥーン調と相性が悪い）
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.55f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // 霞（遠景を溶かして奥行きを出す）
            RenderSettings.fog = true;
            // 線形フォグ：プレイ範囲（カメラから約40m）はくっきり、遠景だけ霞ませる
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.62f, 0.52f);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 230f;
        }

        static void BuildVolume(Transform parent)
        {
            EnsureFolder(EnvDir);
            string path = EnvDir + "/TestField_Volume.asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            // Neutral：ACES より暗部が潰れにくく、色味をコントロールしやすい（トゥーン向き）
            tone.mode.Override(TonemappingMode.Neutral);

            var ca = Add<ColorAdjustments>();
            ca.postExposure.Override(0.45f);
            ca.contrast.Override(6f);
            ca.saturation.Override(-8f);
            ca.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

            var split = Add<SplitToning>();
            split.shadows.Override(new Color(0.38f, 0.47f, 0.52f));
            split.highlights.Override(new Color(0.85f, 0.70f, 0.50f));
            split.balance.Override(-15f);

            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.7f);

            var vig = Add<Vignette>();
            vig.intensity.Override(0.26f);
            vig.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Global Volume");
            go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static void BuildCamera(Vector3 target)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // MSAA を使う
            // 斜め見下ろし（M3 で Cinemachine に置き換える仮カメラ）
            var rot = Quaternion.Euler(50f, 20f, 0f);
            camGo.transform.rotation = rot;
            camGo.transform.position = target - rot * Vector3.forward * 26f;
        }

        // 空から環境光・反射を生成（ライトマップは焼かない）
        static void BakeEnvironment()
        {
            var ls = new LightingSettings { bakedGI = false, realtimeGI = false };
            string lsPath = EnvDir + "/TestField_Lighting.lighting";
            AssetDatabase.DeleteAsset(lsPath);
            AssetDatabase.CreateAsset(ls, lsPath);
            Lightmapping.lightingSettings = ls;
            try { Lightmapping.Bake(); }
            catch (System.Exception e) { Debug.LogWarning("[Matoi] Environment bake skipped: " + e.Message); }
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
