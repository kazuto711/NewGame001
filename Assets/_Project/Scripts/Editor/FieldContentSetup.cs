// 採集・探索の中身（アイテムのデータ、物色の中身、フィールドへの配置）を作るエディタツール。
// TestFieldBuilder.Build から呼ばれる。配置を変える時は、下の一覧の座標を書き換えて「Build Test Field」を実行し直す。
// モデルは Tools/Blender/build_field_props.py で作る。
using System.Collections.Generic;
using System.Linq;
using Matoi.Combat;
using Matoi.Crafting;
using Matoi.Items;
using Matoi.Missions;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class FieldContentSetup
    {
        const string DataDir = "Assets/_Project/Data/Items";
        const string PropDir = "Assets/_Project/Art/Environment/Props";
        const string CraftDir = "Assets/_Project/Data/Crafting";
        const string CatalogPath = CraftDir + "/GameCatalog.asset";

        static readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>();
        static readonly Dictionary<string, LootTable> tables = new Dictionary<string, LootTable>();

        static GameObject Model(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{name}.fbx");

        // ---------- アイテム ----------
        static ItemDefinition Item(string id, string label, string text, ItemCategory category, float weight, string model,
                                   float heal = 0f, WeaponDefinition ammoFor = null, int ammo = 0)
        {
            string path = $"{DataDir}/Item_{id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (!def)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            def.displayName = label;
            def.description = text;
            def.category = category;
            def.weight = weight;
            def.heal = heal;
            def.ammoFor = ammoFor;
            def.ammoAmount = ammo;
            def.worldModel = Model(model);
            EditorUtility.SetDirty(def);
            items[id] = def;
            return def;
        }

        static void Table(string id, float empty, params (string item, float chance, int min, int max)[] rows)
        {
            string path = $"{DataDir}/Loot_{id}.asset";
            var table = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (!table)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                AssetDatabase.CreateAsset(table, path);
            }
            table.emptyChance = empty;
            table.entries = rows.Select(r => new LootTable.Entry { item = items[r.item], chance = r.chance, min = r.min, max = r.max }).ToArray();
            EditorUtility.SetDirty(table);
            tables[id] = table;
        }

        // ---------- クラフト ----------
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static ItemAmount[] Cost(params (string item, int count)[] rows) =>
            rows.Select(r => new ItemAmount(items[r.item], r.count)).ToArray();

        static RecipeDefinition Recipe(string id, string result, int count, params (string item, int count)[] rows)
        {
            var recipe = LoadOrCreate<RecipeDefinition>($"{CraftDir}/Recipe_{id}.asset");
            recipe.result = items[result];
            recipe.resultCount = count;
            recipe.ingredients = Cost(rows);
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        static WeaponUpgradeDefinition Upgrade(string id, UpgradeKind kind, string label, string text, params float[] bonuses)
        {
            var upgrade = LoadOrCreate<WeaponUpgradeDefinition>($"{CraftDir}/Upgrade_{id}.asset");
            upgrade.kind = kind;
            upgrade.displayName = label;
            upgrade.description = text;
            upgrade.levels = new[]
            {
                new WeaponUpgradeDefinition.Level { bonus = bonuses[0], cost = Cost(("Scrap", 4), ("Wood", 2)) },
                new WeaponUpgradeDefinition.Level { bonus = bonuses[1], cost = Cost(("Scrap", 6), ("Parts", 2)) },
                new WeaponUpgradeDefinition.Level { bonus = bonuses[2], cost = Cost(("Scrap", 8), ("Parts", 5)) },
            };
            EditorUtility.SetDirty(upgrade);
            return upgrade;
        }

        /// <summary>全データの一覧を返す。まだ無ければ作る。</summary>
        public static GameCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
            if (catalog) return catalog;
            CreateData();
            return AssetDatabase.LoadAssetAtPath<GameCatalog>(CatalogPath);
        }

        static void CreateCrafting(WeaponDefinition pistol, WeaponDefinition rifle)
        {
            if (!AssetDatabase.IsValidFolder(CraftDir)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Crafting");
            var recipes = new[]
            {
                Recipe("Bandage", "Bandage", 2, ("Cloth", 2), ("Herb", 1)),
                Recipe("Medkit", "Medkit", 1, ("Bandage", 3), ("Herb", 4)),
                Recipe("PistolAmmo", "PistolAmmo", 1, ("Scrap", 2)),
                Recipe("RifleAmmo", "RifleAmmo", 1, ("Scrap", 3), ("Parts", 1)),
                Recipe("Radio", "Radio", 1, ("RadioPart", 3)),
            };
            var upgrades = new[]
            {
                Upgrade("Damage", UpgradeKind.Damage, "威力", "1 発のダメージが上がる。", 0.10f, 0.20f, 0.30f),
                Upgrade("Magazine", UpgradeKind.Magazine, "装弾数", "弾倉に入る弾が増える。次のリロードから反映される。", 0.20f, 0.40f, 0.60f),
                Upgrade("Stability", UpgradeKind.Stability, "安定性", "弾のばらつきと反動が小さくなる。", 0.15f, 0.30f, 0.45f),
            };
            var catalog = LoadOrCreate<GameCatalog>(CatalogPath);
            catalog.items = items.Values.ToArray();
            catalog.recipes = recipes;
            catalog.upgrades = upgrades;
            catalog.weapons = new[] { pistol, rifle };
            EditorUtility.SetDirty(catalog);
            MissionSetup.CreateMission(catalog);
        }

        public static void CreateData()
        {
            if (!AssetDatabase.IsValidFolder(DataDir)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Items");
            var pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Weapon_Pistol.asset");
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Weapon_AK47.asset");

            Item("Bandage", "包帯", "清潔な布の包帯。傷口を押さえれば、まだ動ける。", ItemCategory.Consumable, 0.2f, "Item_Bandage", heal: 25f);
            Item("Medkit", "救急箱", "避難所から持ち出された救急箱。体力を大きく回復する。", ItemCategory.Consumable, 1f, "Item_Medkit", heal: 60f);
            Item("Can", "缶詰", "賞味期限は読めないが、開ければ食べられる。", ItemCategory.Consumable, 0.5f, "Item_Can", heal: 15f);
            Item("Water", "飲料水", "封の切られていない水。少しだけ体力が戻る。", ItemCategory.Consumable, 0.5f, "Item_Bottle", heal: 8f);
            Item("RifleAmmo", "小銃弾（30 発）", "AK47 に使える弾の箱。使うと予備の弾が 30 発増える。", ItemCategory.Ammo, 0.6f, "Item_Ammo", ammoFor: rifle, ammo: 30);
            Item("PistolAmmo", "拳銃弾（24 発）", "拳銃に使える弾の箱。使うと予備の弾が 24 発増える。", ItemCategory.Ammo, 0.4f, "Item_Ammo", ammoFor: pistol, ammo: 24);
            Item("Herb", "薬草", "畦に生える薬草。包帯や救急箱の材料になる。", ItemCategory.Material, 0.1f, "Item_Herb");
            Item("Wood", "木材", "まだ使える板切れ。武器の強化に使う。", ItemCategory.Material, 1f, "Item_Wood");
            Item("Scrap", "鉄くず", "錆びた鉄の切れ端。弾の材料や武器の強化に使う。", ItemCategory.Material, 1f, "Item_Scrap");
            Item("Cloth", "布切れ", "破れた衣類。包帯の材料になる。", ItemCategory.Material, 0.2f, "Item_Cloth");
            Item("Parts", "機械部品", "歯車や銅線。小銃弾の材料や武器の強化に使う。", ItemCategory.Material, 0.5f, "Item_Parts");
            Item("Charm", "古いお守り", "色あせたお守り。誰かの無事を願ったものだろう。", ItemCategory.Valuable, 0.1f, "Item_Charm");
            Item("RadioPart", "無線機の部品", "ミオの無線機を直すのに必要な部品。3 つ集めて作業台で組み立てる。", ItemCategory.Key, 0f, "Item_Parts");
            Item("Radio", "修理した無線機", "社の方角の異変を拾った無線機。", ItemCategory.Key, 0f, "Item_Parts");

            Table("Common", 0.15f, ("Can", 0.45f, 1, 1), ("Cloth", 0.5f, 1, 2), ("Wood", 0.3f, 1, 2), ("Bandage", 0.2f, 1, 1), ("PistolAmmo", 0.25f, 1, 1));
            Table("Vending", 0f, ("Water", 0.9f, 1, 2), ("Can", 0.4f, 1, 1));
            Table("Car", 0.1f, ("Scrap", 0.6f, 1, 2), ("Parts", 0.45f, 1, 2), ("Bandage", 0.25f, 1, 1), ("RifleAmmo", 0.2f, 1, 1), ("PistolAmmo", 0.3f, 1, 1));
            Table("Tools", 0f, ("Parts", 0.8f, 1, 3), ("Scrap", 0.6f, 1, 2), ("RifleAmmo", 0.25f, 1, 1), ("PistolAmmo", 0.25f, 1, 1));
            Table("House", 0.05f, ("Can", 0.55f, 1, 2), ("Water", 0.4f, 1, 1), ("Bandage", 0.4f, 1, 2), ("Cloth", 0.6f, 1, 2), ("Charm", 0.12f, 1, 1), ("PistolAmmo", 0.2f, 1, 1));
            Table("Rare", 0f, ("Medkit", 0.8f, 1, 1), ("RifleAmmo", 0.8f, 1, 2), ("Parts", 0.7f, 2, 3), ("Charm", 0.5f, 1, 1), ("PistolAmmo", 0.5f, 1, 2));
            // 敵が落とす物：採集できない布切れと機械部品が、取り尽くしても手に入るように
            Table("EnemyDrop", 0.45f, ("Cloth", 0.5f, 1, 1), ("Parts", 0.3f, 1, 1), ("PistolAmmo", 0.12f, 1, 1));
            Table("EliteDrop", 0f, ("Parts", 0.8f, 1, 2), ("Scrap", 0.6f, 1, 2), ("PistolAmmo", 0.35f, 1, 1), ("RifleAmmo", 0.25f, 1, 1));   // 重殻体が落とす物
            CreateCrafting(pistol, rifle);
            AssetDatabase.SaveAssets();
        }

        // ---------- 配置の部品 ----------
        static Transform root;

        static float GroundY(float x, float z)
        {
            float best = 0f;
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 6f, z), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.point.y > best && hit.normal.y > 0.8f && hit.point.y < 3.2f) best = hit.point.y;
            return best;
        }

        static GameObject Put(string model, Vector3 position, float yaw, Transform parent = null)
        {
            var prefab = Model(model);
            if (!prefab) { Debug.LogError("[Matoi] 小物のモデルがありません: " + model); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent ? parent : root);
            go.name = model;
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation;
            return go;
        }

        static GameObject Ground(string model, float x, float z, float yaw = 0f) => Put(model, new Vector3(x, GroundY(x, z), z), yaw);

        static void Pickup(string item, int count, float x, float z)
        {
            var def = items[item];
            var holder = new GameObject("Pickup_" + item);
            holder.transform.SetParent(root, false);
            holder.transform.SetPositionAndRotation(new Vector3(x, GroundY(x, z) + 0.01f, z), Quaternion.Euler(0f, (x * 37f + z * 53f) % 360f, 0f));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(def.worldModel, holder.transform);
            model.transform.localPosition = Vector3.zero;
            holder.AddComponent<WorldPickup>().Configure(def, count);
        }

        static void Node(string model, string label, string item, int min, int max, float x, float z)
        {
            var go = Ground(model, x, z, (x * 31f + z * 17f) % 360f);
            if (go) go.AddComponent<GatherNode>().Configure(label, items[item], min, max, 1.4f);
        }

        static GameObject Container(string model, string label, string table, float x, float z, float yaw, float height = 0.6f)
        {
            var go = Ground(model, x, z, yaw);
            if (go) go.AddComponent<LootContainer>().Configure(label, tables[table], 0.8f, height);
            return go;
        }

        /// <summary>廃車・自販機など、すでにある物の横に「物色できる場所」を作る。</summary>
        static void SpotOn(Transform body, string label, string table, Vector3 position, float height)
        {
            var go = new GameObject("Loot_" + label);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.AddComponent<LootContainer>().Configure(label, tables[table], 0.8f, height, body);
        }

        static void Point(string title, float x, float z, float radius)
        {
            var go = new GameObject("Point_" + title);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(x, GroundY(x, z), z);
            go.AddComponent<ExplorationPoint>().Configure(title, radius);
        }

        /// <summary>建物の入口の向きを基準にした位置。a=横、b=入口側が＋。</summary>
        static Vector3 InHouse(Transform house, float a, float b, float floor)
        {
            var entrance = house.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Entrance"));
            Vector3 front = entrance.position - house.position;
            front.y = 0f;
            front.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, front);
            return house.position + side * a + front * b + Vector3.up * floor;
        }

        static float FaceFront(Transform house)
        {
            var entrance = house.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Entrance"));
            Vector3 front = entrance.position - house.position;
            return Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;
        }

        /// <summary>車体の長い方向の端（物色する位置）。</summary>
        static Vector3 EndOf(Transform body, float distance)
        {
            var b = body.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
            var t = body.GetComponentInChildren<MeshFilter>().transform;
            Vector3 axis = b.x > b.y && b.x > b.z ? t.right : b.y > b.z ? t.up : t.forward;
            axis.y = 0f;
            return body.position + axis.normalized * distance;
        }

        // ---------- フィールドへの配置 ----------
        public static void Build(Transform env)
        {
            CreateData();
            root = new GameObject("FieldContent").transform;
            root.SetParent(env, false);
            Physics.SyncTransforms();

            ClearVegetationInside("House_27_5", 2.9f, 2.6f);
            ClearVegetationInside("House_14_-2", 5.4f, 4.0f);
            ClearVegetationInside("House_25_-14", 4.2f, 6.5f);   // 拠点の納屋と、前の焚き火のまわり
            ClearVegetationOnHill();
            PlaceDecor();
            PlaceBuildingInteriors();
            PlaceBase();
            Physics.SyncTransforms();
            PlacePickups();
            PlaceNodes();
            PlaceContainers();
            PlacePoints();
            PlaceMission();
            AssignSaveIds();
        }

        /// <summary>広げた高台に埋まった草や岩を取り除く。</summary>
        static void ClearVegetationOnHill()
        {
            var plants = GameObject.Find("Vegetation")?.transform;
            if (!plants) return;
            foreach (Transform group in plants)
                foreach (Transform plant in group.Cast<Transform>().ToArray())
                {
                    Vector3 d = plant.position - MissionSetup.ArenaCenter;
                    if (Mathf.Abs(d.x) < 12.5f && Mathf.Abs(d.z) < 9.5f) Object.DestroyImmediate(plant.gameObject);
                }
        }

        // ---------- 第一話の目印・部品・灯籠 ----------
        static GameObject Mark(string group, Vector3 position)
        {
            var go = new GameObject("Marker_" + group);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.AddComponent<MissionMarker>().Configure(group);
            return go;
        }

        static GameObject Mark(string group, float x, float z) => Mark(group, new Vector3(x, GroundY(x, z), z));

        static void RadioPart(Vector3 position)
        {
            var def = items["RadioPart"];
            var holder = Mark("radio_part", position);
            holder.name = "Pickup_RadioPart";
            var model = (GameObject)PrefabUtility.InstantiatePrefab(def.worldModel, holder.transform);
            model.transform.localPosition = Vector3.zero;
            holder.AddComponent<WorldPickup>().Configure(def, 1);
        }

        static void PlaceMission()
        {
            Mark("start_ammo", 4.1f, -18.6f);
            Mark("first_enemy", 1.5f, -29f);
            Mark("base", 25f, -14f);
            Mark("shrine", MissionSetup.ArenaCenter);

            var stations = root.GetComponentsInChildren<BaseStation>();
            Vector3 bed = stations.First(s => s.StationKind == BaseStation.Kind.Bed).transform.position;
            Vector3 bench = stations.First(s => s.StationKind == BaseStation.Kind.Workbench).transform.position;
            Mark("bed", bed);
            Mark("bench", bench);
            Mark("craft", bench);
            Mark("craft", 10.2f, 6.3f);    // 布切れ
            Mark("craft", 32f, -12f);      // 薬草

            // 無線機の部品（バス・工事現場・蔵）と、それを守る敵の出現場所
            RadioPart(new Vector3(-10.5f, GroundY(-10.5f, 31f) + 0.01f, 31f));
            RadioPart(new Vector3(14.5f, GroundY(14.5f, -33.5f) + 0.01f, -33.5f));
            var kura = GameObject.Find("House_27_5")?.transform;
            if (kura) RadioPart(InHouse(kura, 0.3f, -0.3f, 0.52f));
            Mark("guard_bus", -9f, 29f);
            Mark("guard_site_a", 12.5f, -31f);
            Mark("guard_site_b", 17f, -35.5f);
            Mark("guard_kura", 31f, 10f);

            // 社の灯籠（ボスを倒した後に火をともす）
            var lamp = Ground("StoneLantern", 22f, 40.2f);
            if (!lamp) return;
            var flame = new GameObject("LampFlame").AddComponent<Light>();
            flame.transform.SetParent(lamp.transform, false);
            flame.transform.position = lamp.transform.position + Vector3.up * 1.5f;
            flame.type = LightType.Point;
            flame.color = new Color(1f, 0.68f, 0.32f);
            flame.intensity = 8f;
            flame.range = 14f;
            flame.shadows = LightShadows.None;
            flame.enabled = false;
            lamp.AddComponent<MissionLamp>().Configure(flame);
            Mark("lamp", lamp.transform.position);
        }

        /// <summary>セーブデータで見分けられるよう、拾う物・採集場所・入れ物に名前を付ける。</summary>
        static void AssignSaveIds()
        {
            var used = new HashSet<string>();
            foreach (var target in root.GetComponentsInChildren<Interactable>(true))
            {
                if (target is BaseStation) continue;
                Vector3 p = target.transform.position;
                string id = $"{target.GetType().Name}_{target.name}_{Mathf.RoundToInt(p.x * 10f)}_{Mathf.RoundToInt(p.z * 10f)}";
                string unique = id;
                for (int i = 2; !used.Add(unique); i++) unique = $"{id}_{i}";
                target.SaveId = unique;
                EditorUtility.SetDirty(target);
            }
        }

        // ---------- 拠点（農家の納屋） ----------
        static void Station(string model, BaseStation.Kind kind, string label, Vector3 position, float yaw, float height, Transform parent)
        {
            var go = Put(model, position, yaw, parent);
            if (go) go.AddComponent<BaseStation>().Configure(kind, label, height);
        }

        static void PlaceBase()
        {
            var barn = GameObject.Find("House_25_-14")?.transform;
            if (!barn) { Debug.LogError("[Matoi] 拠点にする納屋が見つかりません"); return; }
            var group = new GameObject("Base").transform;
            group.SetParent(root, false);
            group.position = barn.position;
            group.gameObject.AddComponent<BaseZone>().Configure(9f);

            const float floor = 0.12f;
            float yaw = FaceFront(barn);
            Station("Workbench", BaseStation.Kind.Workbench, "作業台", InHouse(barn, 0f, -1.8f, floor), yaw + 180f, 0.9f, group);
            Station("StorageChest", BaseStation.Kind.Storage, "保管箱", InHouse(barn, 2.8f, -0.1f, floor), yaw + 90f, 0.6f, group);
            Station("Bedroll", BaseStation.Kind.Bed, "寝床", InHouse(barn, -2.55f, 0.75f, floor), yaw + 180f, 0.3f, group);

            Vector3 fire = InHouse(barn, 0f, 4.7f, 0f);
            var campfire = Put("Campfire", new Vector3(fire.x, GroundY(fire.x, fire.z), fire.z), yaw, group);
            if (!campfire) return;
            var glow = new GameObject("FireLight").AddComponent<Light>();
            glow.transform.SetParent(campfire.transform, false);
            glow.transform.position = campfire.transform.position + Vector3.up * 0.7f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.62f, 0.3f);
            glow.intensity = 5f;
            glow.range = 8f;
            glow.shadows = LightShadows.None;
        }

        /// <summary>中に入れる建物の室内に生えてしまった草や岩を取り除く。</summary>
        static void ClearVegetationInside(string houseName, float halfWidth, float halfDepth)
        {
            var house = GameObject.Find(houseName)?.transform;
            var plants = GameObject.Find("Vegetation")?.transform;
            if (!house || !plants) return;
            var entrance = house.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Entrance"));
            Vector3 front = Vector3.ProjectOnPlane(entrance.position - house.position, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, front);
            // Vegetation の下は 草・低木・岩 などのグループに分かれている
            foreach (Transform group in plants)
                foreach (Transform plant in group.Cast<Transform>().ToArray())
                {
                    Vector3 d = plant.position - house.position;
                    if (Mathf.Abs(Vector3.Dot(d, side)) < halfWidth && Mathf.Abs(Vector3.Dot(d, front)) < halfDepth)
                        Object.DestroyImmediate(plant.gameObject);
                }
        }

        static void PlaceDecor()
        {
            Ground("KeiTruck", 31f, -17f, 62f);
            Ground("Tiller", 29.5f, -9f, 140f);
            Ground("TireStack", 22f, -21f);
            Ground("TireStack", 10f, -27f, 40f);
            Ground("CrateStack", 30f, -22f, 20f);
            Ground("CrateStack", 22f, -31f, -15f);
            Ground("RoadSign", 5.2f, -24f, 90f);
            Ground("RoadSign", 5.2f, 26f, 90f);
            Ground("Mailbox", 8.8f, -18f, 90f);
            Ground("Mailbox", 8.8f, 12f, 90f);
            Ground("Bench", 9.6f, 2.2f, 90f);
            Ground("Bicycle", 6.8f, 1.2f, 12f);
            Ground("StoneLantern", 19.2f, 24f);
            Ground("StoneLantern", 24.8f, 24f);
            Ground("Hasa", -34f, -14f, 90f);
            Ground("Hasa", -34f, 18f, 90f);
            Ground("Jizo", -35f, 8f, 90f);
            Ground("Well", 33f, -4f, 30f);
            Ground("ToriiSmall", 32f, 30f, 200f);
            for (float z = -29f; z < -6f; z += 3.1f)
                if (Mathf.Repeat(z, 11f) > 2.5f) Ground("BambooFence", 35f, z, 90f + (z % 3f));
            for (float x = 9f; x < 21f; x += 3.1f) Ground("BambooFence", x, 22.5f, x % 2f);
        }

        static void PlaceBuildingInteriors()
        {
            var kura = GameObject.Find("House_27_5")?.transform;
            if (kura)
            {
                const float f = 0.5f;
                float yaw = FaceFront(kura);
                Put("Shelf", InHouse(kura, 0.5f, -1.65f, f), yaw + 180f);
                Put("RiceBags", InHouse(kura, 1.5f, -1.2f, f), yaw);
                var chest = Put("Tansu", InHouse(kura, -1.5f, -1.6f, f), yaw + 180f);
                if (chest) chest.AddComponent<LootContainer>().Configure("古いタンス", tables["Rare"], 0.8f, 0.8f);
                var crate = Put("Crate", InHouse(kura, -1.8f, 0.2f, f), yaw + 90f);
                if (crate) crate.AddComponent<LootContainer>().Configure("蔵の長持", tables["Rare"], 0.8f, 0.6f);
            }

            var ruin = GameObject.Find("House_14_-2")?.transform;
            if (ruin)
            {
                const float f = 0.45f;
                float yaw = FaceFront(ruin);
                Put("LowTable", InHouse(ruin, -1.8f, -0.8f, f), yaw);
                Put("Shelf", InHouse(ruin, 3.4f, -3.05f, f), yaw + 180f);
                var chest = Put("Tansu", InHouse(ruin, -3.6f, -3.0f, f), yaw + 180f);
                if (chest) chest.AddComponent<LootContainer>().Configure("タンス", tables["House"], 0.8f, 0.8f);
                var crate = Put("Crate", InHouse(ruin, -4.2f, 1.6f, f), yaw);
                if (crate) crate.AddComponent<LootContainer>().Configure("押し入れの箱", tables["House"], 0.8f, 0.6f);
            }

            var barn = GameObject.Find("House_25_-14")?.transform;
            if (barn)
            {
                float yaw = FaceFront(barn);
                var box = Put("ToolBox", InHouse(barn, 2.75f, 1.5f, 0.12f), yaw);
                if (box) box.AddComponent<LootContainer>().Configure("工具箱", tables["Tools"], 0.8f, 0.3f);
            }
        }

        static void PlacePickups()
        {
            Pickup("Bandage", 1, 3.3f, -13.6f);
            Pickup("Can", 1, 0.1f, -13.2f);
            Pickup("RifleAmmo", 1, 6.4f, -8.1f);
            Pickup("Can", 1, -3f, 3.6f);
            Pickup("Water", 1, 5.3f, 8.9f);
            Pickup("Bandage", 1, 10.6f, -16.2f);
            Pickup("RifleAmmo", 1, 20.4f, -11.5f);
            Pickup("Cloth", 2, 10.2f, 6.3f);
            Pickup("Parts", 1, -10.2f, 31.2f);
            Pickup("Scrap", 2, -7.4f, 28.3f);
            Pickup("Charm", 1, 15.5f, 42.5f);
            Pickup("Herb", 2, -6.6f, 18.2f);
            Pickup("Can", 2, 30.2f, -7.6f);
            Pickup("Water", 1, 32f, -1.8f);
            Pickup("Medkit", 1, 19.3f, -33.6f);
            Pickup("PistolAmmo", 1, 4.1f, -18.6f);
            Pickup("PistolAmmo", 1, 8.2f, 4.6f);
        }

        static void PlaceNodes()
        {
            foreach (var (x, z) in new[] { (-7f, -21f), (-8f, 12f), (32f, -12f), (18f, 19f), (-33.5f, 11f), (30f, 21f) })
                Node("Node_Herb", "薬草", "Herb", 2, 3, x, z);
            foreach (var (x, z) in new[] { (20f, -18.5f), (10f, 9f), (33f, 9f), (-8f, 38f) })
                Node("Node_WoodPile", "廃材の山", "Wood", 2, 3, x, z);
            foreach (var (x, z) in new[] { (10f, -33f), (-7f, 25f), (27f, -27f), (-15f, 38f) })
                Node("Node_ScrapPile", "鉄くずの山", "Scrap", 2, 3, x, z);
        }

        static void PlaceContainers()
        {
            Container("Crate", "木箱", "Common", 9.4f, -11f, 18f);
            Container("Crate", "木箱", "Common", 20.6f, -8.6f, -24f);
            Container("Crate", "木箱", "Common", 33f, 2.6f, 40f);
            Container("Crate", "木箱", "Common", -8f, 33.4f, 8f);
            Container("Crate", "奉納箱", "Rare", 28.5f, 42.5f, 0f);
            Container("Crate", "供え物の箱", "House", -35f, 6.2f, 90f);
            Container("Crate", "井戸端の箱", "Common", 31.3f, -5.4f, 30f);
            Container("Crate", "森の祠の箱", "Rare", 33.2f, 32.4f, 20f);
            Container("Locker", "作業員のロッカー", "Tools", 24f, -32.4f, 180f, 1f);
            Container("ToolBox", "工具箱", "Tools", 18.5f, -30.4f, 30f, 0.3f);

            var vending = GameObject.Find("VendingMachine")?.transform;
            if (vending) SpotOn(vending, "自販機", "Vending", vending.position, 1f);
            foreach (string name in new[] { "WreckedCar_White", "WreckedCar_Blue" })
            {
                var car = GameObject.Find(name)?.transform;
                if (car) SpotOn(car, "廃車", "Car", EndOf(car, 2f), 0.8f);
            }
            var bus = GameObject.Find("WreckedBus")?.transform;
            if (bus) SpotOn(bus, "廃バス", "Car", EndOf(bus, 4.6f), 1f);
            var drums = GameObject.Find("Barrels")?.transform;
            if (drums) SpotOn(drums, "ドラム缶", "Tools", drums.position, 0.6f);
        }

        static void PlacePoints()
        {
            Point("崩れた民家", 14f, -2f, 8f);
            Point("古い蔵", 27f, 5f, 6f);
            Point("農家の納屋", 25f, -14f, 7f);
            Point("鎮守の社", 22f, 34f, 7f);
            Point("錆びたバス", -12f, 34f, 7f);
            Point("工事現場の跡", 16f, -36f, 9f);
            Point("畦の地蔵", -35f, 8f, 6f);
            Point("裏の井戸", 33f, -4f, 6f);
            Point("森の小さな鳥居", 32f, 30f, 6f);
        }
    }
}
