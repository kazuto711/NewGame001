# 主人公（仮）を組み立てる Blender スクリプト。
#  - Quaternius「Universal Base Characters」の素体（CC0）を読み込む
#  - 髪・ひげを同じ骨格に付け替える
#  - 素体を複製して少し膨らませ、部位ごとに色分けして「服」を作る（ベスト・シャツ・ズボン・ブーツ・手袋）
#  - Unity 用 FBX として書き出す
#
# 実行方法（コマンドライン）:
#   blender -b --factory-startup -P build_hero.py -- <素体.fbx> <出力.fbx> <髪1.fbx> [<髪2.fbx> ...]
# 本番モデルを Blender で手作りしたら、このスクリプトは不要になる。
import sys
import bpy
import bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
BODY_FBX, OUT_FBX, HAIR_FBXS = argv[0], argv[1], argv[2:]

# ---------------- 服の設定 ----------------
# 部位名: (色 RGBA, 膨らませる量 m)
OUTFIT = {
    "Outfit_Vest":   ((0.20, 0.23, 0.15, 1), 0.022),  # 胴：くすんだオリーブのベスト
    "Outfit_Shirt":  ((0.42, 0.38, 0.30, 1), 0.012),  # 腕：カーキのシャツ
    "Outfit_Pants":  ((0.24, 0.22, 0.20, 1), 0.012),  # 脚：濃いグレーブラウンのズボン
    "Outfit_Boots":  ((0.10, 0.08, 0.07, 1), 0.020),  # 足首から下：ブーツ
    "Outfit_Gloves": ((0.08, 0.08, 0.08, 1), 0.006),  # 手：手袋
}
BOOT_TOP_Z = 0.30   # 床からこの高さ未満はブーツ
SLEEVE_END_FRAC = 1.0  # 前腕は手首まで袖


def region_for(bone, z):
    b = bone.lower()
    if b in ("head", "neck_01"):
        return None  # 素肌のまま（服を作らない）
    if any(k in b for k in ("hand", "thumb", "index", "middle", "ring", "pinky")):
        return "Outfit_Gloves"
    if z < BOOT_TOP_Z or b.startswith(("foot", "ball")):
        return "Outfit_Boots"
    if b.startswith(("thigh", "calf", "pelvis")):
        return "Outfit_Pants"
    if b.startswith(("upperarm", "lowerarm")):
        return "Outfit_Shirt"
    if b.startswith(("spine", "clavicle")):
        return "Outfit_Vest"
    return "Outfit_Pants"


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


# ---------------- 読み込み ----------------
bpy.ops.wm.read_factory_settings(use_empty=True)
body_objs = import_fbx(BODY_FBX)
arm = next(o for o in body_objs if o.type == "ARMATURE")
body = max((o for o in body_objs if o.type == "MESH"), key=lambda o: len(o.data.vertices))
print("body:", body.name, "armature:", arm.name)

# 髪・ひげ：自分の骨格を捨てて、素体の骨格に付け替える（ボーン名が同じなので頂点グループがそのまま使える）
for hp in HAIR_FBXS:
    objs = import_fbx(hp)
    for o in objs:
        if o.type == "MESH":
            mw = o.matrix_world.copy()
            o.parent = arm
            o.matrix_world = mw
            for m in o.modifiers:
                if m.type == "ARMATURE":
                    m.object = arm
    for o in objs:
        if o.type == "ARMATURE":
            bpy.data.objects.remove(o, do_unlink=True)

# ---------------- 服を作る ----------------
outfit = body.copy()
outfit.data = body.data.copy()
outfit.name = "Outfit"
outfit.data.name = "Outfit"
bpy.context.collection.objects.link(outfit)

mesh = outfit.data
mesh.materials.clear()
mat_index = {}
for i, (name, (color, _)) in enumerate(OUTFIT.items()):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = 0.85
    mesh.materials.append(mat)
    mat_index[name] = i

group_names = {g.index: g.name for g in outfit.vertex_groups}
mw = outfit.matrix_world

# 頂点ごとに「一番影響の強いボーン」で部位を決める
vert_region = []
for v in mesh.vertices:
    best, best_w = None, -1.0
    for g in v.groups:
        if g.weight > best_w:
            best, best_w = group_names.get(g.group), g.weight
    z = (mw @ v.co).z
    vert_region.append(region_for(best or "", z))

bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
bm.normal_update()

# 部位を頂点そのものに記録する（面を消すと頂点番号が振り直されるため）
names = list(OUTFIT.keys())
layer = bm.verts.layers.int.new("region")
for v in bm.verts:
    r = vert_region[v.index]
    v[layer] = -1 if r is None else names.index(r)

# 素肌部分の面を削除
to_delete = [f for f in bm.faces if any(v[layer] < 0 for v in f.verts)]
bmesh.ops.delete(bm, geom=to_delete, context="FACES")

# 面の材質を、頂点の部位の多数決で決める
for f in bm.faces:
    counts = {}
    for v in f.verts:
        counts[v[layer]] = counts.get(v[layer], 0) + 1
    f.material_index = mat_index[names[max(counts, key=counts.get)]]

# 法線方向へ膨らませる（部位ごとに厚みを変える）
bm.normal_update()
offset_of = [OUTFIT[n][1] for n in names]
moves = [(v, v.normal * offset_of[v[layer]]) for v in bm.verts if v.link_faces and v[layer] >= 0]
for v, d in moves:
    v.co += d

# ブーツ：足の指の形が出ないように、つま先側を強くならす（靴らしい丸い形にする）
boots_i = names.index("Outfit_Boots")
toe_verts = [v for v in bm.verts if v.link_faces and v[layer] == boots_i and (mw @ v.co).z < 0.09]
for _ in range(8):
    bmesh.ops.smooth_vert(bm, verts=toe_verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)

# 何も付いていない頂点を掃除
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context="VERTS")
bm.to_mesh(mesh)
bm.free()
mesh.update()

# 服の下に隠れる素体は基本そのまま残す（脱げる装備・ダメージ表現に使える）。
# ただし足の指はブーツを突き抜けるので、素体の足先の面だけ削除する。
bm2 = bmesh.new()
bm2.from_mesh(body.data)
bm2.verts.ensure_lookup_table()
bgroups = {g.index: g.name for g in body.vertex_groups}
bmw = body.matrix_world
def is_toe(v):
    dv = body.data.vertices[v.index]
    best = max(dv.groups, key=lambda g: g.weight, default=None)
    name = bgroups.get(best.group, "") if best else ""
    return name.startswith(("ball", "foot")) and (bmw @ dv.co).z < 0.12
toe_flags = [is_toe(v) for v in bm2.verts]
bmesh.ops.delete(bm2, geom=[f for f in bm2.faces if all(toe_flags[v.index] for v in f.verts)], context="FACES")
bm2.to_mesh(body.data)
bm2.free()

# ---------------- 書き出し ----------------
for o in bpy.data.objects:
    o.select_set(o.type in {"ARMATURE", "MESH"})
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX,
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z",
    axis_up="Y",
    add_leaf_bones=False,
    bake_anim=False,
    use_armature_deform_only=False,
    mesh_smooth_type="FACE",
    path_mode="STRIP",
)
print("EXPORTED", OUT_FBX)
for o in bpy.data.objects:
    if o.type == "MESH":
        print("MESH", o.name, len(o.data.vertices), [m.name for m in o.data.materials])
