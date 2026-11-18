# 道ばたの廃車・廃バス・ドラム缶を作る Blender スクリプト（トゥーン向けのローポリ、テクスチャ無しの色分け）。
#   blender -b --factory-startup -P build_wreck_props.py -- <出力フォルダ> [<確認画像.png>]
#
# 作るもの：WreckedCar_White、WreckedCar_Blue（屋根がつぶれた車）、WreckedBus、Drum_Rust、Drum_Blue
#
# Unity 側との約束（build_japanese_buildings.py と同じ）:
#   - 名前が "COL_" で始まる箱 … 当たり判定になる（見た目には出ない）
#   - 原点は地面の高さ・物の中心。車体の長さ方向は Blender の Y（Unity の Z）
import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR = args[0]
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT_DIR, exist_ok=True)

# PALETTE は見た目の色（sRGB）。材質にはリニア値へ変換して入れる
PALETTE = {
    "Paint_White": (0.66, 0.65, 0.58),
    "Paint_Blue":  (0.27, 0.38, 0.43),
    "Bus_Cream":   (0.72, 0.66, 0.48),
    "Bus_Red":     (0.50, 0.17, 0.12),
    "Rust":        (0.40, 0.20, 0.11),
    "Rust_Dark":   (0.23, 0.13, 0.09),
    "Glass":       (0.10, 0.13, 0.15),
    "Rubber":      (0.07, 0.07, 0.07),
    "Steel":       (0.36, 0.37, 0.38),
    "Light":       (0.70, 0.66, 0.50),
    "Tail":        (0.42, 0.09, 0.07),
    "Drum_Blue":   (0.16, 0.30, 0.42),
    "Drum_Rust":   (0.38, 0.22, 0.13),
}
MATS = {}


def mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new("M_Wreck_" + name)
    c = tuple(v ** 2.2 for v in PALETTE[name])
    m.diffuse_color = (*c, 1)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Roughness"].default_value = 0.35 if name == "Glass" else 0.85
    MATS[name] = m
    return m


class Builder:
    """部品を組み立てて、最後に 1 メッシュにまとめる。"""

    def __init__(self, name):
        self.name = name
        self.parts = []
        self.cols = []

    def box(self, size, loc, m, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def cyl(self, r, h, loc, m, rot=(0, 0, 0), verts=16):
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc,
                                            rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def profile(self, pts, width, m, x=0.0):
        """横から見た形（y, z の折れ線）を、x 方向に width の厚みで押し出す。"""
        bm = bmesh.new()
        hx = width / 2
        a = [bm.verts.new((x - hx, y, z)) for y, z in pts]
        b = [bm.verts.new((x + hx, y, z)) for y, z in pts]
        bm.faces.new(a)
        bm.faces.new(b)
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new("profile")
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new("profile", me)
        bpy.context.collection.objects.link(o)
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def col(self, size, loc, suffix="Body"):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.name = f"COL_{self.name}_{suffix}"
        self.cols.append(o)
        return o

    def finish(self):
        bpy.ops.object.select_all(action="DESELECT")
        for p in self.parts:
            p.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.join()
        body = bpy.context.active_object
        body.name = self.name
        bpy.ops.object.shade_flat()
        for c in self.cols:
            c.parent = body
        return body


def wheel(b, x, y, flat=False):
    """タイヤ。flat=True はパンクして沈んだタイヤ。"""
    r = 0.27 if flat else 0.33
    b.cyl(r, 0.24, (x, y, r), "Rubber", rot=(0, 90, 0), verts=14)
    side = 1 if x > 0 else -1
    b.cyl(r * 0.55, 0.03, (x + side * 0.12, y, r), "Rust_Dark", rot=(0, 90, 0), verts=10)


def build_car(name, paint, crushed):
    b = Builder(name)
    # 下半分の車体（横から見た形）
    b.profile([(-2.15, 0.38), (2.15, 0.38), (2.15, 0.72), (1.98, 0.84), (0.98, 0.95),
               (-1.55, 0.98), (-2.05, 0.94), (-2.15, 0.82)], 1.74, paint)
    # 窓まわり：暗いガラスの塊の上に屋根と柱を乗せる
    roof_z = 1.22 if crushed else 1.44
    b.profile([(-1.62, 0.96), (0.94, 0.94), (0.26, roof_z - 0.04), (-1.18, roof_z - 0.04)], 1.42, "Glass")
    b.box((1.36, 1.54, 0.07), (0, -0.46, roof_z), paint, rot=(4 if crushed else 0, 0, 0))
    for s in (-1, 1):
        x = s * 0.70
        b.box((0.05, 0.10, 0.60), (x, 0.60, (0.96 + roof_z) / 2), paint, rot=(38, 0, 0))   # A ピラー
        b.box((0.05, 0.09, roof_z - 0.94), (x, -0.36, (0.94 + roof_z) / 2), paint)          # B ピラー
        b.box((0.05, 0.12, 0.56), (x, -1.40, (0.96 + roof_z) / 2), paint, rot=(-32, 0, 0))  # C ピラー
        # ドアの継ぎ目・取っ手・サビ
        px = s * 0.875
        for y in (0.82, -0.34, -1.26):
            b.box((0.012, 0.025, 0.50), (px, y, 0.68), "Rust_Dark")
        b.box((0.03, 0.16, 0.04), (px, 0.02, 0.84), "Steel")
        b.box((0.03, 0.16, 0.04), (px, -1.08, 0.84), "Steel")
        b.box((0.012, 1.3, 0.13), (px, 0.25 * s, 0.46), "Rust")
        b.box((0.012, 0.55, 0.26), (px, -1.55 * s, 0.56), "Rust")
        # タイヤハウス
        b.box((0.02, 0.86, 0.34), (px, 1.32, 0.48), "Rust_Dark")
        b.box((0.02, 0.86, 0.34), (px, -1.32, 0.48), "Rust_Dark")
    # 前（ボンネットが少し開いて錆びている）
    b.box((1.60, 1.02, 0.045), (0, 1.50, 0.98), paint, rot=(-11, 0, 0))
    b.box((0.75, 0.48, 0.012), (0.22, 1.60, 1.01), "Rust", rot=(-11, 0, 0))
    b.box((1.72, 0.10, 0.17), (0, 2.18, 0.44), "Steel")
    b.box((0.80, 0.03, 0.20), (0, 2.16, 0.64), "Rust_Dark")
    for s in (-1, 1):
        b.box((0.34, 0.03, 0.15), (s * 0.66, 2.16, 0.66), "Light")
        b.box((0.32, 0.03, 0.15), (s * 0.68, -2.16, 0.70), "Tail")
    # 後ろ
    b.box((1.72, 0.10, 0.17), (0, -2.18, 0.46), "Steel")
    b.box((1.20, 0.03, 0.22), (0, -2.16, 0.74), "Rust_Dark")
    # 屋根のサビ・へこみ
    b.box((0.60, 0.70, 0.012), (-0.25, -0.70, roof_z + 0.04), "Rust")
    # タイヤ（1 本だけパンク）
    wheel(b, -0.78, 1.32)
    wheel(b, 0.78, 1.32, flat=True)
    wheel(b, -0.78, -1.32)
    wheel(b, 0.78, -1.32)
    b.col((1.80, 4.40, 0.72), (0, 0, 0.62))
    b.col((1.42, 2.40, roof_z - 0.95), (0, -0.40, (0.95 + roof_z) / 2), "Cabin")
    return b.finish()


def build_bus():
    b = Builder("WreckedBus")
    L = 9.5
    W = 2.46
    # 車体（クリーム色）＋ 赤い帯
    b.profile([(-L / 2, 0.55), (L / 2, 0.55), (L / 2, 2.72), (L / 2 - 0.25, 2.96),
               (-L / 2 + 0.2, 2.96), (-L / 2, 2.76)], W, "Bus_Cream")
    for s in (-1, 1):
        x = s * (W / 2 + 0.006)
        b.box((0.012, L - 0.1, 0.34), (x, 0, 1.28), "Bus_Red")
        # 窓の列（柱を残して暗い板を並べる）
        for i in range(7):
            y = -3.75 + i * 1.12
            if s > 0 and i == 6:
                continue   # 乗降口のぶん
            b.box((0.012, 0.98, 0.82), (x, y, 2.10), "Glass")
        # タイヤハウスと下まわりのサビ
        for y in (2.75, -2.65):
            b.box((0.012, 1.15, 0.56), (x, y, 0.82), "Rust_Dark")
        b.box((0.014, 2.30, 0.20), (x, -0.20 * s, 0.68), "Rust")
        b.box((0.014, 0.75, 0.40), (x, 1.05 * s, 1.72), "Rust")
    # 乗降口（右前）
    door_x = W / 2 + 0.008
    b.box((0.012, 0.92, 2.00), (door_x, 3.15, 1.56), "Rust_Dark")
    b.box((0.016, 0.38, 1.66), (door_x, 2.94, 1.64), "Glass")
    b.box((0.016, 0.38, 1.66), (door_x, 3.36, 1.64), "Glass")
    # 前面
    fy = L / 2 + 0.006
    b.box((2.14, 0.012, 1.02), (0, fy, 2.02), "Glass")
    b.box((0.07, 0.016, 1.04), (0, fy + 0.002, 2.02), "Bus_Cream")
    b.box((1.20, 0.014, 0.26), (0, fy, 2.72), "Rust_Dark")     # 行き先表示
    b.box((W - 0.05, 0.014, 0.28), (0, fy, 1.28), "Bus_Red")
    b.box((W + 0.05, 0.14, 0.22), (0, L / 2 + 0.05, 0.66), "Steel")
    for s in (-1, 1):
        b.cyl(0.13, 0.04, (s * 0.86, fy, 0.98), "Light", rot=(90, 0, 0), verts=12)
    # 後面
    ry = -L / 2 - 0.006
    b.box((1.70, 0.012, 0.62), (0, ry, 2.16), "Glass")
    b.box((W - 0.05, 0.014, 0.28), (0, ry, 1.28), "Bus_Red")
    b.box((W + 0.05, 0.14, 0.22), (0, -L / 2 - 0.05, 0.66), "Steel")
    for s in (-1, 1):
        b.box((0.26, 0.016, 0.34), (s * 0.92, ry, 1.02), "Tail")
    # 屋根：換気口と大きなサビ
    b.box((0.72, 0.72, 0.13), (0, 1.75, 3.02), "Steel")
    b.box((0.72, 0.72, 0.13), (0, -1.95, 3.02), "Rust_Dark")
    b.box((1.45, 2.70, 0.012), (-0.25, -0.35, 2.965), "Rust")
    # タイヤ（前の 1 本はパンク）
    for x, y, flat in ((-1.08, 2.75, False), (1.08, 2.75, True), (-1.08, -2.65, False), (1.08, -2.65, False)):
        r = 0.36 if flat else 0.47
        b.cyl(r, 0.30, (x, y, r), "Rubber", rot=(0, 90, 0), verts=16)
        side = 1 if x > 0 else -1
        b.cyl(r * 0.55, 0.03, (x + side * 0.15, y, r), "Rust_Dark", rot=(0, 90, 0), verts=10)
    b.col((W, L, 2.55), (0, 0, 1.70))
    return b.finish()


def build_drum(name, paint):
    b = Builder(name)
    R, H = 0.30, 0.90
    b.cyl(R, H - 0.04, (0, 0, H / 2), paint, verts=20)
    # 上下のふち・胴の 2 本の輪
    for z in (0.015, H - 0.015):
        b.cyl(R + 0.012, 0.03, (0, 0, z), "Steel" if paint == "Drum_Blue" else "Rust_Dark", verts=20)
    for z in (H * 0.34, H * 0.66):
        b.cyl(R + 0.010, 0.022, (0, 0, z), paint, verts=20)
    # ふたと注ぎ口
    b.cyl(R - 0.018, 0.006, (0, 0, H - 0.002), "Rust_Dark", verts=20)
    b.cyl(0.035, 0.018, (0.14, 0.06, H + 0.004), "Steel", verts=10)
    # サビの筋
    for a, z, h in ((35, 0.22, 0.28), (150, 0.62, 0.24), (265, 0.42, 0.42)):
        r = math.radians(a)
        b.box((0.006, 0.13, h), (math.cos(r) * (R + 0.002), math.sin(r) * (R + 0.002), z), "Rust",
              rot=(0, 0, a))
    b.col((R * 2, R * 2, H), (0, 0, H / 2))
    return b.finish()


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for c in obj.children:
        c.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE",
    )


def render_preview(objs, path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    for o in bpy.data.objects:
        if o.name.startswith("COL_"):
            o.hide_render = True
    positions = [(-6.5, 3.5), (-1.5, 3.5), (5.5, 2.5), (-4.5, -2.0), (-3.0, -2.0)]
    for o, (x, y) in zip(objs, positions):
        o.location = (x, y, 0)
    bpy.ops.object.camera_add(location=(9, -13, 8))
    cam = bpy.context.active_object
    cam.rotation_euler = (Vector((0, 1.5, 0.8)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 38
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()

objects = [
    build_car("WreckedCar_White", "Paint_White", False),
    build_car("WreckedCar_Blue", "Paint_Blue", True),
    build_bus(),
    build_drum("Drum_Rust", "Drum_Rust"),
    build_drum("Drum_Blue", "Drum_Blue"),
]
for o in objects:
    export(o, os.path.join(OUT_DIR, o.name + ".fbx"))
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    print(f"[wreck] {o.name}: {tris} tris")
if PREVIEW:
    render_preview(objects, PREVIEW)
print("[wreck] done")
