# MPFB2 で主人公（20 代のアジア系男性）を作る Blender スクリプト。
#   set BLENDER_USER_RESOURCES=<MPFB を入れた隔離フォルダ>
#   set HERO_FBX=<出力する FBX>（省略すると書き出さない）
#   blender -b --factory-startup -P build_mpfb_hero.py -- <出力.blend> <確認画像フォルダ> [<アセットのフォルダ> ...]
#
# 使うアセットはすべて CC0（MakeHuman system assets）。
# 体格・顔・髪・服は下の「設定」だけを変えれば作り直せる。髪・服は別の部品なので、後から差し替えられる。
import os
import shutil
import sys
import addon_utils
import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
OUT_BLEND, PREVIEW_DIR, PACKS = args[0], args[1], args[2:]
os.makedirs(PREVIEW_DIR, exist_ok=True)

# ---------------------------------------------------------------- 設定
MACRO = {
    "gender": 1.0,       # 男性
    "age": 0.47,         # 20 代（0.5 が 25 歳）
    "muscle": 0.62,      # 標準よりわずかに引き締まった体
    "weight": 0.40,      # 細身
    "proportions": 0.70, # 頭を小さく、脚を長く
    "height": 0.61,      # 身長 170cm 台前半を狙う
    "cupsize": 0.5,
    "firmness": 0.5,
    "race": {"asian": 1.0, "caucasian": 0.0, "african": 0.0},
}
# 顔の細かい調整（名前は MPFB の targets フォルダ内のファイル名、値は 0〜1）
FACE_TARGETS = {
    # 頭：丸く幼い印象を減らし、面長で小さめに
    "head/head-scale-horiz-decr": 0.30, "head/head-scale-vert-decr": 0.12, "head/head-rectangular": 0.25,
    "head/head-fat-decr": 0.35, "forehead/forehead-scale-vert-decr": 0.35,
    # あご・ほお：骨ばった大人の男性の輪郭に
    "chin/chin-bones-incr": 0.35, "chin/chin-prominent-incr": 0.25, "chin/chin-height-incr": 0.15,
    "cheek/l-cheek-volume-decr": 0.35, "cheek/r-cheek-volume-decr": 0.35,
    # 鼻筋を少し通し、口は薄めに
    "nose/nose-scale-depth-incr": 0.20, "nose/nose-width2-decr": 0.15,
    "mouth/mouth-scale-horiz-incr": 0.12, "mouth/mouth-lowerlip-volume-decr": 0.20,
}
RIG = "mixamo_unity"
SKIN = "young_asian_male"
# (種類, ファイル名, 材質の名前, 色のテクスチャ)
ASSETS = [
    ("Eyes", "low-poly", "Eye", "brown_eye.png"),
    ("Eyebrows", "eyebrow001", "Brow", "eyebrow001.png"),
    ("Eyelashes", "eyelashes01", "Lash", "eyelashes01.png"),
    ("Hair", "short01", "Hair", "short01_diffuse.png"),
    ("Clothes", "male_casualsuit02", "Outfit", "male_casualsuit02_diffuse.png"),
    ("Clothes", "shoes04", "Shoes", "shoes04_diffuse.png"),
]
PREVIEW_COLORS = {"Shirt": (0.13, 0.15, 0.07, 1)}   # テクスチャを使わない部品の確認用の色
TEXTURES = {}   # 書き出すテクスチャ：材質の名前 → 元のファイル

# ---------------------------------------------------------------- MPFB の準備
MOD = "bl_ext.user_default.mpfb"
addon_utils.enable(MOD, default_set=True, persistent=True)   # MPFB は設定の登録が済んでいないと起動できない

from bl_ext.user_default.mpfb.services.locationservice import LocationService  # noqa: E402
from bl_ext.user_default.mpfb.services.humanservice import HumanService  # noqa: E402
from bl_ext.user_default.mpfb.services.targetservice import TargetService  # noqa: E402
from bl_ext.user_default.mpfb.services.assetservice import AssetService  # noqa: E402

user_data = LocationService.get_user_data()
os.makedirs(user_data, exist_ok=True)
for pack in PACKS:
    target = os.path.join(user_data, "eyes", "low-poly") if os.path.basename(pack) == "low-poly" else user_data
    shutil.copytree(pack, target, dirs_exist_ok=True)
    print("[hero] assets installed:", os.path.basename(pack))
AssetService.update_all_asset_lists()


def find_file(name):
    """アセットのフォルダから、ファイル名が完全に一致するものを探す。"""
    for root, _, files in os.walk(user_data):
        for f in files:
            if f.lower() == name.lower():
                return os.path.join(root, f)
    return None


def make_material(name, texture=None):
    """Unity でトゥーン材質に置き換える、名前付きの材質を作る。"""
    m = bpy.data.materials.new("M_Mpfb_" + name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.7
    if texture:
        node = m.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(texture, check_existing=True)
        m.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        if name in ("Hair", "Brow", "Lash"):
            m.node_tree.links.new(node.outputs["Alpha"], bsdf.inputs["Alpha"])
        TEXTURES[name] = texture
    elif name in PREVIEW_COLORS:
        bsdf.inputs["Base Color"].default_value = PREVIEW_COLORS[name]
    return m


def grayscale_copy(source, dest):
    """服の模様を残したまま色を抜く（Unity 側で好きな色に染める）。"""
    img = bpy.data.images.load(source)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    gray = px[0::4] * 0.3 + px[1::4] * 0.59 + px[2::4] * 0.11
    gray = np.clip(gray / max(float(np.percentile(gray, 90)), 0.05), 0.0, 1.0)
    px[0::4] = px[1::4] = px[2::4] = gray
    img.pixels.foreach_set(px)
    img.filepath_raw = dest
    img.file_format = "PNG"
    img.save()
    return dest


def split_outfit(obj, shirt, pants):
    """上下つながった服を、独立した塊ごとに シャツ と ズボン の材質に分ける。"""
    obj.data.materials.clear()
    obj.data.materials.append(shirt)
    obj.data.materials.append(pants)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for start in bm.verts:
        if start.index in seen:
            continue
        stack, island = [start], []
        seen.add(start.index)
        while stack:
            v = stack.pop()
            island.append(v)
            for e in v.link_edges:
                o = e.other_vert(v)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        islands.append(island)
    waist = sum(v.co.z for v in bm.verts) / len(bm.verts)
    for island in islands:
        upper = sum(v.co.z for v in island) / len(island) > waist
        for v in island:
            for f in v.link_faces:
                f.material_index = 0 if upper else 1
    bm.to_mesh(obj.data)
    bm.free()
    print(f"[hero] outfit islands={len(islands)}")


def remove_covered_skin(body, clothes):
    """服の下に完全に隠れる肌を消す（動いた時に肌が服を突き抜けないように）。服の端の近くは少し残す。"""
    trees = []
    for c in clothes:
        verts = [c.matrix_world @ v.co for v in c.data.vertices]
        trees.append(BVHTree.FromPolygons(verts, [p.vertices[:] for p in c.data.polygons]))

    def hit(origin, direction, reach):
        return any(t.ray_cast(origin, direction, reach)[0] is not None for t in trees)

    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.normal_update()
    covered = set()
    for v in bm.verts:
        p, n = body.matrix_world @ v.co, (body.matrix_world.to_3x3() @ v.normal).normalized()
        if hit(p, n, 0.10) or hit(p, -n, 0.015):   # 外側に服がある、またはすでに服より外へ出ている
            covered.add(v.index)
    # 服の端から 2 周ぶんの肌は残す（袖口やえり元に隙間ができないように）
    for _ in range(2):
        covered -= {v.index for v in bm.verts if v.index in covered
                    and any(e.other_vert(v).index not in covered for e in v.link_edges)}
    faces = [f for f in bm.faces if all(v.index in covered for v in f.verts)]
    bmesh.ops.delete(bm, geom=faces, context="FACES_ONLY")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    print(f"[hero] covered skin removed: faces={len(faces)} left={len(body.data.polygons)}")


def darken_scalp(body, hair):
    """髪のすぐ下の地肌を暗い材質にして少し沈める（毛の隙間から肌が見えたり、髪とちらついたりしないように）。"""
    verts = [hair.matrix_world @ v.co for v in hair.data.vertices]
    tree = BVHTree.FromPolygons(verts, [p.vertices[:] for p in hair.data.polygons])
    body.data.materials.append(make_material("Scalp"))
    slot = len(body.data.materials) - 1
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.normal_update()
    covered = set()
    for v in bm.verts:
        p, n = body.matrix_world @ v.co, (body.matrix_world.to_3x3() @ v.normal).normalized()
        if tree.ray_cast(p, n, 0.025)[0] is not None or tree.ray_cast(p, -n, 0.01)[0] is not None:
            covered.add(v.index)
    covered -= {v.index for v in bm.verts if v.index in covered
                and any(e.other_vert(v).index not in covered for e in v.link_edges)}   # 生え際は肌のまま
    normals = {v.index: v.normal.copy() for v in bm.verts if v.index in covered}
    for v in bm.verts:
        if v.index in covered:
            v.co -= normals[v.index] * 0.006
    count = 0
    for f in bm.faces:
        if all(v.index in covered for v in f.verts):
            f.material_index = slot
            count += 1
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    print(f"[hero] scalp faces={count}")


# ---------------------------------------------------------------- 組み立て
for existing in list(bpy.data.objects):
    bpy.data.objects.remove(existing)
human = HumanService.create_human(macro_detail_dict=MACRO)
human.name = "Hero_Body"

for name, value in FACE_TARGETS.items():
    path = os.path.join(LocationService.get_mpfb_data("targets"), name + ".target.gz")
    if value > 0 and os.path.exists(path):
        TargetService.load_target(human, path, weight=value)
    elif value > 0:
        print("[hero] target not found:", name)

rig = HumanService.add_builtin_rig(human, RIG, import_weights=True)
rig.name = "Hero_Rig"

human.data.materials.clear()
human.data.materials.append(make_material("Skin", find_file("young_lightskinned_male_diffuse3.png")))

for kind, stem, material, texture in ASSETS:
    path = find_file(stem + ".mhclo")
    if not path:
        print("[hero] MISSING asset:", kind, stem)
        continue
    obj = HumanService.add_mhclo_asset(path, human, asset_type=kind, subdiv_levels=0, material_type="MAKESKIN")
    obj.name = f"Hero_{kind}_{stem}"
    print("[hero] asset:", kind, stem)
    tex = os.path.join(os.path.dirname(path), texture)
    if material == "Outfit":
        pants_tex = grayscale_copy(tex, os.path.join(os.path.dirname(OUT_BLEND), "T_Mpfb_Pants_gray.png"))
        split_outfit(obj, make_material("Shirt"), make_material("Pants", pants_tex))
    else:
        obj.data.materials.clear()
        obj.data.materials.append(make_material(material, tex))

# ---------------------------------------------------------------- 集計
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
total, top = 0, 0.0
for o in bpy.data.objects:
    if o.type != "MESH":
        continue
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    total += n
    if me.vertices and "Hair" not in o.name:
        top = max(top, max((ev.matrix_world @ v.co).z for v in me.vertices))
    print(f"[hero] mesh {o.name}: tris={n} mats={[m.name for m in o.data.materials if m]}")
    ev.to_mesh_clear()
print(f"[hero] total tris={total} height={top:.3f}m bones={len(rig.data.bones)}")

bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

# ---------------------------------------------------------------- 確認画像
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 900, 1200
scene.render.film_transparent = False
world = bpy.data.worlds.new("Preview")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.44, 0.46, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
scene.world = world
bpy.ops.object.light_add(type="SUN", rotation=(0.9, 0.1, -0.6))
bpy.context.active_object.data.energy = 3.0
bpy.ops.object.camera_add()
cam = bpy.context.active_object
scene.camera = cam


def shot(name, eye, target, lens):
    cam.location = eye
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    scene.render.filepath = os.path.join(PREVIEW_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)


h = top
shot("MPFB_full_front", (0, -3.4, h * 0.55), (0, 0, h * 0.5), 50)
shot("MPFB_full_quarter", (2.0, -2.8, h * 0.6), (0, 0, h * 0.5), 50)
shot("MPFB_full_side", (3.4, 0, h * 0.55), (0, 0, h * 0.5), 50)
shot("MPFB_face_front", (0, -1.1, h * 0.93), (0, 0, h * 0.93), 85)
shot("MPFB_face_quarter", (0.65, -0.9, h * 0.94), (0, 0, h * 0.93), 85)

# ---------------------------------------------------------------- Unity 用に書き出す（環境変数 HERO_FBX があれば）
OUT_FBX = os.environ.get("HERO_FBX")
if OUT_FBX:
    from bl_ext.user_default.mpfb.services.exportservice import ExportService

    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = human
    human.select_set(True)
    # 体型（シェイプキー）と、服の下に隠れる体・補助メッシュの非表示を確定する
    TargetService.bake_targets(human)
    ExportService.bake_modifiers_remove_helpers(human, bake_masks=True, remove_helpers=True, also_proxy=False)
    remove_covered_skin(human, [o for o in bpy.data.objects if o.type == "MESH" and "_Clothes_" in o.name])
    darken_scalp(human, next(o for o in bpy.data.objects if o.type == "MESH" and "_Hair_" in o.name))

    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        if o.type == "ARMATURE" or (o.type == "MESH" and o.parent == rig):
            o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_anim=False,
        use_armature_deform_only=False, mesh_smooth_type="FACE", path_mode="STRIP",
    )
    tex_dir = os.path.join(os.path.dirname(OUT_FBX), "Textures")
    os.makedirs(tex_dir, exist_ok=True)
    for name, src in TEXTURES.items():
        shutil.copy2(src, os.path.join(tex_dir, f"T_Mpfb_{name}.png"))
    print("[hero] exported:", OUT_FBX, "textures:", sorted(TEXTURES))
print("[hero] done")
