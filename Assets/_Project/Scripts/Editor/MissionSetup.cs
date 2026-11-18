// 第一話「社の灯」のデータと、ボス戦の場所を作るエディタツール。
// 目的や会話を変える時は CreateMission の一覧を書き換えて「Build Test Field」を実行し直す。
using System.Linq;
using Matoi.AI;
using Matoi.Crafting;
using Matoi.Items;
using Matoi.Missions;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class MissionSetup
    {
        const string Dir = "Assets/_Project/Data/Missions";
        const string MissionPath = Dir + "/Mission_01.asset";
        const string Mio = "ミオ（無線）";

        public static readonly Vector3 ArenaCenter = new Vector3(22f, 2.4f, 36f);
        public static readonly Vector2 ArenaHalfSize = new Vector2(11f, 8f);

        /// <summary>第一話のデータ。まだ無ければ作る。</summary>
        public static MissionDefinition LoadMission()
        {
            var mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionPath);
            if (mission) return mission;
            FieldContentSetup.CreateData();
            return AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionPath);
        }

        static RadioLine[] Radio(params string[] lines) => lines.Select(l => new RadioLine(Mio, l)).ToArray();

        static EncounterSpawn Spawn(string enemyId, string marker) => new EncounterSpawn { enemyId = enemyId, marker = marker };

        public static void CreateMission(GameCatalog catalog)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Missions");
            var mission = AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionPath);
            if (!mission)
            {
                mission = ScriptableObject.CreateInstance<MissionDefinition>();
                AssetDatabase.CreateAsset(mission, MissionPath);
            }
            RecipeDefinition Recipe(string id) => catalog.recipes.First(r => r.name == "Recipe_" + id);

            mission.title = "第一話　社の灯";
            mission.steps = new[]
            {
                new ObjectiveStep
                {
                    text = "道ばたの拳銃弾を拾う", kind = ObjectiveKind.HaveItem, markerGroup = "start_ammo",
                    item = catalog.FindItem("Item_PistolAmmo"),
                    radio = Radio("……聞こえますか。こちらミオ。そこで倒れていた人、動けますか。",
                                  "名前も思い出せない？　今は後回しです。足元の弾を拾ってください。"),
                },
                new ObjectiveStep
                {
                    text = "近づいてくる侵食者を倒す", kind = ObjectiveKind.DefeatEnemies, markerGroup = "first_enemy",
                    spawns = new[] { Spawn("Infected", "first_enemy") },
                    radio = Radio("南から侵食者が来ます。もう人じゃありません。ためらわないで。"),
                },
                new ObjectiveStep
                {
                    text = "東の農家の納屋まで行く", kind = ObjectiveKind.ReachMarker, markerGroup = "base", radius = 7f,
                    radio = Radio("東に納屋があります。以前、私たちが使っていた場所です。"),
                },
                new ObjectiveStep
                {
                    text = "納屋の寝床で休む", kind = ObjectiveKind.Signal, signal = MissionSignals.Rest, markerGroup = "bed",
                    radio = Radio("そこなら奴らは近寄りません。寝床で少し休んでください。"),
                },
                new ObjectiveStep
                {
                    text = "布切れと薬草を集め、作業台で包帯を作る", kind = ObjectiveKind.Craft, recipe = Recipe("Bandage"), markerGroup = "craft",
                    radio = Radio("この先は薬なしでは持ちません。布切れ 2 枚と薬草 1 束で包帯が作れます。"),
                },
                new ObjectiveStep
                {
                    text = "無線機の部品を集める", kind = ObjectiveKind.HaveItem, item = catalog.FindItem("Item_RadioPart"), count = 3, markerGroup = "radio_part",
                    spawns = new[]
                    {
                        Spawn("Heavy", "guard_bus"), Spawn("Fast", "guard_site_a"), Spawn("Fast", "guard_site_b"), Spawn("Ranged", "guard_kura"),
                    },
                    radio = Radio("私の無線機はもう限界です。そちらで直せれば、社の様子が分かるはず。",
                                  "部品はバス、工事現場、蔵の 3 か所。どこも侵食者が巣くっています。",
                                  "殻を背負った奴に銃は効きにくい。懐に入って殴ってください。"),
                },
                new ObjectiveStep
                {
                    text = "作業台で無線機を修理する", kind = ObjectiveKind.Craft, recipe = Recipe("Radio"), markerGroup = "bench",
                    radio = Radio("全部そろいましたね。納屋の作業台で組み立ててください。"),
                },
                new ObjectiveStep
                {
                    text = "鎮守の社で「社の主」を倒す", kind = ObjectiveKind.Signal, signal = MissionSignals.BossDefeated, markerGroup = "shrine",
                    radio = Radio("聞こえました……社の方角から、この土地の侵食が広がっています。",
                                  "社に巣くう大物を倒せば、流れは止まるはず。備えてから向かってください。",
                                  "南のパワーアーマーはまだ動きます。使えるものは全部使って。"),
                },
                new ObjectiveStep
                {
                    text = "社の灯籠に火をともす", kind = ObjectiveKind.Signal, signal = MissionSignals.LampLit, markerGroup = "lamp",
                    radio = Radio("やった……！　社の灯籠に火を入れてください。生きている人への合図になります。"),
                },
            };
            mission.endingRadio = Radio("見えます。谷の向こうからでも、あなたの灯が。",
                                        "ありがとう。……ところで、あなたが倒れていた場所に、妙な布の切れ端がありました。",
                                        "まるで、誰かが身にまとっていたような。");
            mission.endingText = "社に灯がともり、侵食の流れは止まった。\nけれど、失われた記憶と、あの布の正体はまだ分からない。";
            EditorUtility.SetDirty(mission);
        }

        /// <summary>敵のプレハブができた後に、目的で出す敵を結び付ける。</summary>
        public static void LinkEnemies()
        {
            var mission = LoadMission();
            foreach (var step in mission.steps)
            {
                if (step.spawns == null) continue;
                foreach (var spawn in step.spawns)
                {
                    var prefab = spawn.enemyId == "Infected" ? EnemySetup.Prefab : EnemySetup.VariantPrefab(spawn.enemyId);
                    spawn.prefab = prefab ? prefab.GetComponent<EnemyBrain>() : null;
                    if (!spawn.prefab) Debug.LogError("[Matoi] ミッションの敵が見つかりません: " + spawn.enemyId);
                }
            }
            EditorUtility.SetDirty(mission);
            AssetDatabase.SaveAssets();
        }

        /// <summary>高台にボスと結界を置く。</summary>
        public static void PlaceBossArena()
        {
            var root = new GameObject("--- BossArena").transform;
            root.position = ArenaCenter;

            var barrierMaterial = EnemySetup.FxMaterial("M_Fx_Barrier", new Color(1f, 0.25f, 0.3f, 0.08f));
            var walls = new GameObject("Barrier");
            walls.transform.SetParent(root, false);
            const float height = 4.5f, thick = 0.08f;
            void Wall(Vector3 position, Vector3 size)
            {
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall";
                Object.DestroyImmediate(wall.GetComponent<Collider>());   // 見た目だけ。押し戻しは BossArena が行う（カメラがぶつからないように）
                wall.transform.SetParent(walls.transform, false);
                wall.transform.localPosition = position + Vector3.up * (height * 0.5f);
                wall.transform.localScale = size;
                var meshRenderer = wall.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = barrierMaterial;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Vector2 h = ArenaHalfSize;
            Wall(new Vector3(0f, 0f, h.y), new Vector3(h.x * 2f, height, thick));
            Wall(new Vector3(0f, 0f, -h.y), new Vector3(h.x * 2f, height, thick));
            Wall(new Vector3(h.x, 0f, 0f), new Vector3(thick, height, h.y * 2f));
            Wall(new Vector3(-h.x, 0f, 0f), new Vector3(thick, height, h.y * 2f));
            walls.SetActive(false);

            var prefab = EnemySetup.CreateBoss();
            var boss = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            boss.name = "Boss_YashiroNoNushi";
            boss.transform.SetPositionAndRotation(ArenaCenter + new Vector3(0f, 0.05f, 3f), Quaternion.Euler(0f, 180f, 0f));
            boss.SetActive(false);

            root.gameObject.AddComponent<BossArena>().Configure(boss.GetComponent<BossBrain>(), walls, ArenaHalfSize);
        }
    }
}
