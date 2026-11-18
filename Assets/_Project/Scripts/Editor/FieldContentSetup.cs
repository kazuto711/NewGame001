// 採集・探索の中身（アイテムのデータ、物色の中身、フィールドへの配置）を作るエディタツール。
// TestFieldBuilder.Build から呼ばれる。配置を変える時は、下の一覧の座標を書き換えて「Build Test Field」を実行し直す。
// モデルは Tools/Blender/build_field_props.py で作る。
using System.Collections.Generic;
using System.Linq;
using Matoi.Combat;
using Matoi.Items;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class FieldContentSetup
    {
        const string DataDir = "Assets/_Project/Data/Items";
        const string PropDir = "Assets/_Project/Art/Environment/Props";

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

        public static void CreateData()
        {
            if (!AssetDatabase.IsValidFolder(DataDir)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Items");
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Project/Data/Weapons/Weapon_AK47.asset");

            Item("Bandage", "包帯", "清潔な布の包帯。傷口を押さえれば、まだ動ける。", ItemCategory.Consumable, 0.2f, "Item_Bandage", heal: 25f);
            Item("Medkit", "救急箱", "避難所から持ち出された救急箱。体力を大きく回復する。", ItemCategory.Consumable, 1f, "Item_Medkit", heal: 60f);
            Item("Can", "缶詰", "賞味期限は読めないが、開ければ食べられる。", ItemCategory.Consumable, 0.5f, "Item_Can", heal: 15f);
            Item("Water", "飲料水", "封の切られていない水。少しだけ体力が戻る。", ItemCategory.Consumable, 0.5f, "Item_Bottle", heal: 8f);
            Item("RifleAmmo", "小銃弾（30 発）", "AK47 に使える弾の箱。使うと予備の弾が 30 発増える。", ItemCategory.Ammo, 0.6f, "Item_Ammo", ammoFor: rifle, ammo: 30);
            Item("Herb", "薬草", "畦に生える薬草。今は素材として取っておく。", ItemCategory.Material, 0.1f, "Item_Herb");
            Item("Wood", "木材", "まだ使える板切れ。", ItemCategory.Material, 1f, "Item_Wood");
            Item("Scrap", "鉄くず", "錆びた鉄の切れ端。", ItemCategory.Material, 1f, "Item_Scrap");
            Item("Cloth", "布切れ", "破れた衣類。", ItemCategory.Material, 0.2f, "Item_Cloth");
            Item("Parts", "機械部品", "歯車や銅線。纏の修理に役立ちそうだ。", ItemCategory.Material, 0.5f, "Item_Parts");
            Item("Charm", "古いお守り", "色あせたお守り。誰かの無事を願ったものだろう。", ItemCategory.Valuable, 0.1f, "Item_Charm");

            Table("Common", 0.15f, ("Can", 0.45f, 1, 1), ("Cloth", 0.5f, 1, 2), ("Wood", 0.3f, 1, 2), ("Bandage", 0.2f, 1, 1));
            Table("Vending", 0f, ("Water", 0.9f, 1, 2), ("Can", 0.4f, 1, 1));
            Table("Car", 0.1f, ("Scrap", 0.6f, 1, 2), ("Parts", 0.45f, 1, 2), ("Bandage", 0.25f, 1, 1), ("RifleAmmo", 0.2f, 1, 1));
            Table("Tools", 0f, ("Parts", 0.8f, 1, 3), ("Scrap", 0.6f, 1, 2), ("RifleAmmo", 0.25f, 1, 1));
            Table("House", 0.05f, ("Can", 0.55f, 1, 2), ("Water", 0.4f, 1, 1), ("Bandage", 0.4f, 1, 2), ("Cloth", 0.6f, 1, 2), ("Charm", 0.12f, 1, 1));
            Table("Rare", 0f, ("Medkit", 0.8f, 1, 1), ("RifleAmmo", 0.8f, 1, 2), ("Parts", 0.7f, 2, 3), ("Charm", 0.5f, 1, 1));
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
            PlaceDecor();
            PlaceBuildingInteriors();
            Physics.SyncTransforms();
            PlacePickups();
            PlaceNodes();
            PlaceContainers();
            PlacePoints();
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
                var box = Put("ToolBox", InHouse(barn, 2.3f, 0.6f, 0.12f), yaw);
                if (box) box.AddComponent<LootContainer>().Configure("工具箱", tables["Tools"], 0.8f, 0.3f);
                Put("Shelf", InHouse(barn, 0.2f, 1.75f, 0.12f), yaw + 180f);
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
            Pickup("Charm", 1, 22f, 31.6f);
            Pickup("Herb", 2, -6.6f, 18.2f);
            Pickup("Can", 2, 30.2f, -7.6f);
            Pickup("Water", 1, 32f, -1.8f);
            Pickup("Medkit", 1, 19.3f, -33.6f);
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
            Container("Crate", "奉納箱", "Rare", 22f, 33.6f, 0f);
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
