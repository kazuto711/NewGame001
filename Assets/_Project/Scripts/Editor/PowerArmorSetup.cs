// パワーアーマーの部品を人型の骨に取り付けるエディタツール。
// Blender で作った部品（Tools/Blender/build_power_armor.py）を、
//   ・骨の向きに部品の上方向（Y）をそろえ
//   ・部品の "Front" の目印をキャラの正面に向け
//   ・骨の中央に置いて骨の子にする
// 骨の軸の向きに依存しないので、別の人型モデルにもそのまま付けられる。
using System.Collections.Generic;
using System.Linq;
using Matoi.Equipment;
using UnityEditor;
using UnityEngine;

namespace Matoi.EditorTools
{
    public static class PowerArmorSetup
    {
        public const string ArmorFbx = "Assets/_Project/Art/Equipment/PowerArmor/PowerArmor.fbx";
        const string StationPrefab = "Assets/_Project/Prefabs/PowerArmorStation.prefab";

        // 部品名 → （始点の骨, 終点の骨, 中央の位置 0〜1, 正面の向きのヒント）
        struct Slot { public HumanBodyBones a, b; public float t; public bool footHint; public bool upFromA; }

        static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>
        {
            { "PA_Helmet",     new Slot { a = HumanBodyBones.Head, upFromA = true, t = 0.11f } },
            { "PA_Chest",      new Slot { a = HumanBodyBones.UpperChest, b = HumanBodyBones.Neck, t = 0.45f } },
            { "PA_Abdomen",    new Slot { a = HumanBodyBones.Chest, b = HumanBodyBones.UpperChest, t = 0.5f } },
            { "PA_Pelvis",     new Slot { a = HumanBodyBones.Hips, b = HumanBodyBones.Spine, t = 0.2f } },
            { "PA_UpperArm_L", new Slot { a = HumanBodyBones.LeftUpperArm, b = HumanBodyBones.LeftLowerArm, t = 0.5f } },
            { "PA_UpperArm_R", new Slot { a = HumanBodyBones.RightUpperArm, b = HumanBodyBones.RightLowerArm, t = 0.5f } },
            { "PA_LowerArm_L", new Slot { a = HumanBodyBones.LeftLowerArm, b = HumanBodyBones.LeftHand, t = 0.5f } },
            { "PA_LowerArm_R", new Slot { a = HumanBodyBones.RightLowerArm, b = HumanBodyBones.RightHand, t = 0.5f } },
            { "PA_Hand_L",     new Slot { a = HumanBodyBones.LeftHand, b = HumanBodyBones.LeftMiddleProximal, t = 0.6f } },
            { "PA_Hand_R",     new Slot { a = HumanBodyBones.RightHand, b = HumanBodyBones.RightMiddleProximal, t = 0.6f } },
            { "PA_UpperLeg_L", new Slot { a = HumanBodyBones.LeftUpperLeg, b = HumanBodyBones.LeftLowerLeg, t = 0.5f } },
            { "PA_UpperLeg_R", new Slot { a = HumanBodyBones.RightUpperLeg, b = HumanBodyBones.RightLowerLeg, t = 0.5f } },
            { "PA_LowerLeg_L", new Slot { a = HumanBodyBones.LeftLowerLeg, b = HumanBodyBones.LeftFoot, t = 0.5f } },
            { "PA_LowerLeg_R", new Slot { a = HumanBodyBones.RightLowerLeg, b = HumanBodyBones.RightFoot, t = 0.5f } },
            { "PA_Foot_L",     new Slot { a = HumanBodyBones.LeftFoot, b = HumanBodyBones.LeftToes, t = 0.45f, footHint = true } },
            { "PA_Foot_R",     new Slot { a = HumanBodyBones.RightFoot, b = HumanBodyBones.RightToes, t = 0.45f, footHint = true } },
        };

        /// <summary>
        /// モデル（バインドポーズ＝Tポーズのまま）に装甲部品を付け、付けた部品の一覧を返す。
        /// </summary>
        public static List<GameObject> Fit(GameObject model, Avatar avatar)
        {
            var bones = BoneMap(model, avatar);
            var armor = AssetDatabase.LoadAssetAtPath<GameObject>(ArmorFbx);
            var result = new List<GameObject>();
            Vector3 charFwd = model.transform.forward;
            foreach (Transform src in armor.transform)
            {
                if (!Slots.TryGetValue(src.name, out var slot)) continue;
                if (!bones.TryGetValue(slot.a, out var a)) { Debug.LogWarning("[Matoi] bone missing " + slot.a); continue; }
                Vector3 start = a.position, dir, center;
                if (slot.upFromA)
                {
                    dir = Vector3.up;
                    center = start + Vector3.up * slot.t;
                }
                else
                {
                    if (!bones.TryGetValue(slot.b, out var b)) { Debug.LogWarning("[Matoi] bone missing " + slot.b); continue; }
                    dir = (b.position - start).normalized;
                    center = Vector3.Lerp(start, b.position, slot.t);
                }
                Vector3 hint = slot.footHint ? Vector3.up : charFwd;
                Vector3 fwd = Vector3.ProjectOnPlane(hint, dir);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(charFwd, dir);
                Quaternion target = Quaternion.LookRotation(fwd.normalized, dir);

                // 部品の「正面」と「上」を、FBX 全体の座標系（Unity：Y が上）で求める。
                //  - 上：Blender の Z → FBX 全体では Y（部品ごとの回転が残っていても、全体では必ず Y が上）
                //  - 正面：子の目印 "Front"（Blender が "Front.001" のように番号を付けるので前方一致で探す）
                //  （Blender で置いた目印 "Front"・"Up" の位置から求めるので、取り込み時の軸変換に左右されない）
                Transform frontMark = null, upMark = null;
                foreach (Transform c in src)
                {
                    if (c.name.StartsWith("Front")) frontMark = c;
                    if (c.name.StartsWith("Up")) upMark = c;
                }
                Vector3 upInAsset = upMark ? (upMark.position - src.position).normalized : Vector3.up;
                Vector3 frontInAsset = frontMark ? Vector3.ProjectOnPlane(frontMark.position - src.position, upInAsset) : Vector3.forward;
                if (frontInAsset.sqrMagnitude < 1e-4f) frontInAsset = Vector3.forward;
                Quaternion frameInAsset = Quaternion.LookRotation(frontInAsset.normalized, upInAsset);

                var piece = Object.Instantiate(src.gameObject);
                piece.name = src.name;
                // 「FBX 内での向き」→「目印の正面・上」→「骨の向き・キャラの正面」の順に置き換える
                piece.transform.SetPositionAndRotation(center, target * Quaternion.Inverse(frameInAsset) * src.rotation);
                piece.transform.SetParent(a, true);
                foreach (var r in piece.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                result.Add(piece);
            }
            return result;
        }

        /// <summary>アバターの人型の骨 → モデル内の Transform。エディタ上（Animator 未初期化）でも使える。</summary>
        static Dictionary<HumanBodyBones, Transform> BoneMap(GameObject model, Avatar avatar)
        {
            var byName = model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var map = new Dictionary<HumanBodyBones, Transform>();
            var humanNames = HumanTrait.BoneName;
            foreach (var hb in avatar.humanDescription.human)
            {
                int idx = System.Array.IndexOf(humanNames, hb.humanName);
                if (idx < 0 || !byName.TryGetValue(hb.boneName, out var t)) continue;
                map[(HumanBodyBones)idx] = t;
            }
            return map;
        }

        // ---------- 置いてあるアーマー ----------
        public static GameObject CreateStationPrefab()
        {
            EnsureFolder("Assets/_Project/Prefabs");
            var root = new GameObject("PowerArmorStation");
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 1.05f, 0);
            col.height = 2.1f;
            col.radius = 0.5f;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 1.12f;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(HeroSetup.HeroModel);
            model.name = "Frame";
            model.transform.SetParent(visual.transform, false);
            var anim = model.GetComponent<Animator>();
            if (!anim) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = HeroSetup.Controller;
            anim.avatar = HeroSetup.HeroAvatar;
            anim.applyRootMotion = false;
            // 中の人はいない：人体のメッシュは隠し、装甲だけを見せる
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.enabled = false;
            Fit(model, HeroSetup.HeroAvatar);

            var station = root.AddComponent<PowerArmorStation>();
            var so = new SerializedObject(station);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("visualAnimator").objectReferenceValue = anim;
            so.FindProperty("blocker").objectReferenceValue = col;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, StationPrefab);
            Object.DestroyImmediate(root);
            return prefab;
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
