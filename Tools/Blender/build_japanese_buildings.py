# 日本の田舎の建物・小物を作る Blender スクリプト（トゥーン向けのローポリ、テクスチャ無しの色分け）。
#   blender -b --factory-startup -P build_japanese_buildings.py -- <出力フォルダ> [<確認画像フォルダ>]
#
# 作るもの：民家（Minka）、壊れた民家（Minka_Ruined）、蔵（Kura）、納屋（Barn）、自販機（VendingMachine）、バス停（BusStop）
#
# Unity 側との約束（他の建物を手作りする時も同じルールで作れば自動で設定される）:
#   - 名前が "COL_" で始まる箱 … 当たり判定になる（見た目には出ない）
#   - "Entrance" という空オブジェクト … 建物の正面（入口）の位置。配置時にこちらを道へ向ける
#   - 原点は地面の高さ・建物の中心
import sys
import math
import os
import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR = args[0]
PREVIEW_DIR = args[1] if len(args) > 1 else None
os.makedirs(OUT_DIR, exist_ok=True)

# ---------------------------------------------------------------- 材質
PALETTE = {
    "Wood_Dark":     (0.17, 0.11, 0.07),
    "Wood_Light":    (0.42, 0.30, 0.18),
    "Wood_Grey":     (0.32, 0.29, 0.26),   # 風雨にさらされた板
    "Plaster":       (0.86, 0.82, 0.72),
    "Plaster_Dirty": (0.64, 0.59, 0.50),
    "Kawara":        (0.20, 0.22, 0.26),   # 瓦
    "Kawara_Ridge":  (0.30, 0.32, 0.36),
    "Tin_Rust":      (0.48, 0.22, 0.11),
    "Tin_Blue":      (0.24, 0.36, 0.40),
    "Stone":         (0.48, 0.47, 0.43),
    "Shoji":         (0.88, 0.84, 0.72),
    "Opening":       (0.05, 0.05, 0.06),   # 窓・戸の暗い部分
    "Namako":        (0.13, 0.13, 0.14),
    "Lath":          (0.30, 0.22, 0.14),   # 壁が剥がれた下地
    "Vend_Red":      (0.72, 0.10, 0.08),
    "Vend_White":    (0.86, 0.86, 0.84),
    "Glass":         (0.16, 0.22, 0.25),
    "Metal":         (0.40, 0.42, 0.44),
    "Sign_Yellow":   (0.85, 0.66, 0.12),
    "COL":           (1.0, 0.0, 1.0),
}
MATS = {}


def mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new("M_JP_" + name)
    # PALETTE は見た目の色（sRGB）で書いてある。Blender / Unity の材質はリニア値なので変換する
    c = tuple(v ** 2.2 for v in PALETTE[name])
    m.diffuse_color = (*c, 1)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Roughness"].default_value = 0.4 if name in ("Glass", "Kawara", "Vend_Red") else 0.85
    b.inputs["Metallic"].default_value = 0.5 if name in ("Metal", "Tin_Blue") else 0.0
    MATS[name] = m
    return m


# ---------------------------------------------------------------- 形状ヘルパー
class Builder:
    """1つの建物を部品で組み立て、最後に 1 メッシュにまとめる。"""

    def __init__(self, name):
        self.name = name
        self.parts = []
        self.extras = []   # COL_ / Entrance など、まとめない物

    def box(self, size, loc, m, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def cyl(self, r, h, loc, m, rot=(0, 0, 0), verts=10):
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc,
                                            rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def prism(self, length, depth, height, loc, m, thickness=None):
        """切妻の三角の壁（x 方向に length の厚み）。底辺 depth・高さ height。"""
        bm = bmesh.new()
        hx = length / 2
        pts = [(-depth / 2, 0), (depth / 2, 0), (0, height)]
        front = [bm.verts.new((-hx, y, z)) for y, z in pts]
        back = [bm.verts.new((hx, y, z)) for y, z in pts]
        bm.faces.new(front[::-1])
        bm.faces.new(back)
        for i in range(3):
            j = (i + 1) % 3
            bm.faces.new((front[i], front[j], back[j], back[i]))
        me = bpy.data.meshes.new("prism")
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new("prism", me)
        bpy.context.collection.objects.link(o)
        o.location = loc
        o.data.materials.append(mat(m))
        self.parts.append(o)
        return o

    def collider(self, size, loc):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.name = f"COL_{self.name}_{len(self.extras)}"
        o.data.materials.append(mat("COL"))
        self.extras.append(o)

    def entrance(self, loc):
        bpy.ops.object.empty_add(type="PLAIN_AXES", location=loc)
        e = bpy.context.active_object
        e.name = "Entrance"
        self.extras.append(e)

    def finish(self):
        bpy.ops.object.select_all(action="DESELECT")
        for p in self.parts:
            p.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.join()
        body = bpy.context.active_object
        body.name = self.name
        bpy.ops.object.shade_flat()
        for e in self.extras:
            e.parent = body
        return body


def gable_roof(b, length, half_depth, wall_h, pitch, eave=0.9, gable_over=0.6, thick=0.16,
               tile_mat="Kawara", ridge_mat="Kawara_Ridge", tiles=True, skip=None, z0=0.0):
    """切妻屋根（棟は x 方向）。瓦の筋（縦の列）と棟・鬼瓦を付ける。skip(側, x) が True の範囲は作らない（崩落用）。"""
    p = math.radians(pitch)
    hr = z0 + wall_h + half_depth * math.tan(p)      # 棟の高さ
    span = half_depth + eave
    slope = span / math.cos(p)
    L = length + 2 * gable_over
    for side in (-1, 1):
        rot = (-pitch * side, 0, 0)
        yc = side * span / 2
        zc = hr - (span / 2) * math.tan(p) + thick / 2 / math.cos(p)
        if skip:
            # 長さ方向を 4 分割し、崩れた部分は作らない
            seg = L / 4
            for k in range(4):
                xc = -L / 2 + seg * (k + 0.5)
                if not skip(side, xc):
                    b.box((seg, slope, thick), (xc, yc, zc), tile_mat, rot)
        else:
            b.box((L, slope, thick), (0, yc, zc), tile_mat, rot)
        if tiles:
            n = int(L / 0.3)
            for i in range(n):
                x = -L / 2 + 0.15 + i * (L - 0.3) / max(1, n - 1)
                if skip and skip(side, x):
                    continue
                zt = zc + thick / 2 / math.cos(p) + 0.025
                b.box((0.09, slope * 0.98, 0.05), (x, yc, zt), tile_mat, rot)
            # 軒先の水平な線
            ye = side * (span - 0.05)
            ze = hr - (span - 0.05) * math.tan(p) + thick
            if not skip:
                b.box((L, 0.12, 0.1), (0, ye, ze), ridge_mat, rot)
    if not skip or not (skip(-1, 0) and skip(1, 0)):
        b.box((L + 0.2, 0.42, 0.34), (0, 0, hr + 0.12), ridge_mat)          # 棟
        for sx in (-1, 1):                                                    # 鬼瓦
            b.box((0.22, 0.62, 0.62), (sx * (L / 2 + 0.06), 0, hr + 0.22), ridge_mat)
    return hr


def plank_lines(b, x0, x1, y, z0, z1, m, spacing=0.22, along_x=True, depth=0.04):
    """壁の板張りの線（横板）。y は壁の外面の位置。"""
    n = int((z1 - z0) / spacing)
    for i in range(1, n + 1):
        z = z0 + i * spacing
        if along_x:
            b.box((x1 - x0, depth, 0.035), ((x0 + x1) / 2, y, z), m)
        else:
            b.box((depth, x1 - x0, 0.035), (y, (x0 + x1) / 2, z), m)


# ---------------------------------------------------------------- 民家
def build_minka(name="Minka", ruined=False):
    b = Builder(name)
    W, D, H = 10.0, 7.0, 2.9           # 幅・奥行・壁の高さ
    floor = 0.45
    hx, hy = W / 2, D / 2
    b.box((W + 0.5, D + 0.5, 0.35), (0, 0, 0.175), "Stone")                       # 基礎石
    # 壁：下は板張り、上は漆喰
    wall_mat = "Plaster_Dirty" if ruined else "Plaster"
    b.box((W, D, H), (0, 0, floor + H / 2), wall_mat)
    b.box((W + 0.04, D + 0.04, 1.0), (0, 0, floor + 0.5), "Wood_Dark")
    plank_lines(b, -hx, hx, -hy - 0.03, floor, floor + 1.0, "Wood_Light")
    plank_lines(b, -hx, hx, hy + 0.03, floor, floor + 1.0, "Wood_Light")
    # 柱
    for x in [-hx + i * (W / 5) for i in range(6)]:
        for y in (-hy - 0.05, hy + 0.05):
            b.box((0.18, 0.12, H), (x, y, floor + H / 2), "Wood_Dark")
    for y in [-hy + i * (D / 3) for i in range(4)]:
        for x in (-hx - 0.05, hx + 0.05):
            b.box((0.12, 0.18, H), (x, y, floor + H / 2), "Wood_Dark")
    b.box((W + 0.2, D + 0.2, 0.2), (0, 0, floor + H - 0.05), "Wood_Dark")         # 桁（軒下の横木）

    # 正面（-y 側）：縁側とガラス戸
    ey = -hy - 0.65
    b.box((W - 1.0, 1.2, 0.1), (0, ey, floor + 0.0), "Wood_Light")                 # 縁側の床
    for x in [-hx + 0.5 + i * ((W - 1.0) / 6) for i in range(7)]:
        b.box((0.12, 0.12, floor), (x, ey - 0.45, floor / 2), "Wood_Dark")        # 縁の束
    for i in range(8):                                                              # 戸（ガラスと障子）
        x = -hx + 1.0 + i * ((W - 2.0) / 8) + (W - 2.0) / 16
        b.box(((W - 2.0) / 8 - 0.06, 0.05, 1.75), (x, -hy - 0.04, floor + 0.05 + 0.9),
              "Glass" if i % 3 else "Shoji")
        b.box((0.05, 0.07, 1.8), (x - (W - 2.0) / 16, -hy - 0.06, floor + 0.95), "Wood_Dark")
    b.box((W - 2.0, 0.08, 0.06), (0, -hy - 0.06, floor + 1.85), "Wood_Dark")
    # 縁側の上の庇（ひさし、トタン）
    b.box((W - 0.6, 1.6, 0.08), (0, ey - 0.15, floor + 2.25), "Tin_Blue" if not ruined else "Tin_Rust", (-14, 0, 0))
    # 側面の窓（格子）
    for sx in (-1, 1):
        b.box((0.05, 1.4, 0.9), (sx * (hx + 0.04), 0.8, floor + 1.7), "Opening")
        for k in range(5):
            b.box((0.07, 0.04, 0.95), (sx * (hx + 0.07), 0.2 + k * 0.3, floor + 1.7), "Wood_Dark")
    # 背面の勝手口
    b.box((0.9, 0.05, 1.8), (2.5, hy + 0.04, floor + 0.9), "Wood_Grey")
    # 屋根
    skip = None
    if ruined:
        # 正面側の右半分が崩落
        skip = lambda side, x: side < 0 and x > 0.5
    hr = gable_roof(b, W, hy, H, 30, eave=1.0, gable_over=0.7, z0=floor, skip=skip)
    for sx in (-1, 1):                                                              # 妻壁
        b.prism(0.12, D, hr - floor - H, (sx * (hx - 0.05), 0, floor + H), wall_mat)
        b.box((0.06, 0.6, 0.35), (sx * (hx + 0.02), 0, floor + H + 0.7), "Opening")  # 換気口
    if ruined:
        b.box((4.6, 3.6, 0.5), (2.6, -1.6, floor + H - 0.2), "Opening")             # 屋根の穴から見える暗がり
        b.box((4.0, 3.2, 0.16), (2.4, -hy - 2.2, 1.0), "Kawara", (38, 8, -6))       # 落ちた屋根
        for k in range(14):                                                          # 瓦礫
            b.box((0.5, 0.3, 0.12), (1.0 + (k * 0.37) % 3.8, -hy - 1.4 - (k * 0.53) % 2.0, 0.08),
                  "Kawara" if k % 2 else "Wood_Grey", (k * 13, k * 29, k * 41))
        for (x, z, w, h) in ((-2.8, 2.0, 1.4, 0.8), (3.6, 2.6, 1.0, 0.6), (-0.6, 3.0, 0.8, 0.5)):  # 剥がれた壁
            b.box((w, 0.06, h), (x, -hy - 0.025, floor + z - 0.6), "Lath")
    b.collider((W + 0.4, D + 0.4, H + floor), (0, 0, (H + floor) / 2))
    b.collider((W - 1.0, 1.2, floor + 0.05), (0, ey, (floor + 0.05) / 2))         # 縁側（段差として上れる）
    b.entrance((0, -hy - 1.5, 0))
    return b.finish()


# ---------------------------------------------------------------- 蔵
def build_kura():
    b = Builder("Kura")
    W, D, H = 5.0, 4.2, 5.2
    hx, hy = W / 2, D / 2
    b.box((W + 0.5, D + 0.5, 0.5), (0, 0, 0.25), "Stone")
    b.box((W, D, H), (0, 0, 0.5 + H / 2), "Plaster")
    # 腰のなまこ壁（黒地に白い斜め格子）
    b.box((W + 0.06, D + 0.06, 1.4), (0, 0, 0.5 + 0.7), "Namako")
    for side in (-1, 1):
        for k in range(9):
            x = -hx + 0.3 + k * 0.55
            b.box((0.07, 0.04, 1.75), (x, side * (hy + 0.04), 1.2), "Plaster", (0, 45, 0))
            b.box((0.07, 0.04, 1.75), (x, side * (hy + 0.04), 1.2), "Plaster", (0, -45, 0))
    b.box((W + 0.1, D + 0.1, 0.12), (0, 0, 0.5 + 1.42), "Plaster")
    # 重い扉と小窓
    b.box((1.4, 0.3, 2.2), (0, -hy - 0.12, 0.5 + 1.1), "Namako")
    b.box((1.6, 0.36, 0.15), (0, -hy - 0.14, 0.5 + 2.3), "Plaster")
    for x in (-1.2, 1.2):
        b.box((0.7, 0.2, 0.7), (x, -hy - 0.08, 0.5 + 3.8), "Namako")
    b.box((W + 0.2, D + 0.2, 0.25), (0, 0, 0.5 + H - 0.1), "Plaster")
    hr = gable_roof(b, W, hy, H, 28, eave=0.7, gable_over=0.45, z0=0.5)
    for sx in (-1, 1):
        b.prism(0.12, D, hr - 0.5 - H, (sx * (hx - 0.05), 0, 0.5 + H), "Plaster")
    b.collider((W + 0.4, D + 0.4, H + 0.5), (0, 0, (H + 0.5) / 2))
    b.entrance((0, -hy - 1.2, 0))
    return b.finish()


# ---------------------------------------------------------------- 納屋（片流れのトタン屋根）
def build_barn():
    b = Builder("Barn")
    W, D, H = 7.0, 5.0, 3.0
    hx, hy = W / 2, D / 2
    b.box((W + 0.2, D + 0.2, 0.12), (0, 0, 0.06), "Stone")
    # 板壁（縦板）。正面は大きく開いている
    for (x0, x1, y) in ((-hx, hx, hy),):
        b.box((W, 0.12, H), (0, y, H / 2), "Wood_Grey")
    for sx in (-1, 1):
        b.box((0.12, D, H), (sx * hx, 0, H / 2), "Wood_Grey")
    for k in range(int(W / 0.25)):
        b.box((0.04, 0.05, H), (-hx + 0.12 + k * 0.25, hy + 0.07, H / 2), "Wood_Dark")
    for sx in (-1, 1):
        for k in range(int(D / 0.25)):
            b.box((0.05, 0.04, H), (sx * (hx + 0.07), -hy + 0.12 + k * 0.25, H / 2), "Wood_Dark")
    b.box((W - 0.2, 0.1, H), (0, hy - 0.3, H / 2), "Opening")                       # 中の暗がり
    b.box((W - 0.4, D - 0.4, 0.05), (0, 0, 0.13), "Opening")
    for x in (-hx, -hx / 3, hx / 3, hx):                                            # 正面の柱
        b.box((0.2, 0.2, H + 0.5), (x, -hy, (H + 0.5) / 2), "Wood_Dark")
    b.box((W + 0.2, 0.22, 0.25), (0, -hy, H + 0.4), "Wood_Dark")
    # 片流れ屋根（前が高く後ろが低い）＋トタンの波
    pitch = 12
    roof_len = (D + 1.6) / math.cos(math.radians(pitch))
    zc = H + 0.55
    b.box((W + 1.0, roof_len, 0.08), (0, 0, zc), "Tin_Rust", (pitch, 0, 0))
    for k in range(int((W + 1.0) / 0.22)):
        x = -(W + 1.0) / 2 + 0.11 + k * 0.22
        b.box((0.06, roof_len, 0.05), (x, 0, zc + 0.06), "Tin_Rust" if k % 7 else "Tin_Blue", (pitch, 0, 0))
    # わら・道具
    for (x, y) in ((-2.2, 1.2), (-1.4, 1.5), (1.8, 1.1)):
        b.cyl(0.45, 0.9, (x, y, 0.55), "Sign_Yellow", (90, 0, 0), verts=8)
    b.collider((0.3, D, H), (-hx, 0, H / 2))
    b.collider((0.3, D, H), (hx, 0, H / 2))
    b.collider((W, 0.4, H), (0, hy, H / 2))
    b.entrance((0, -hy - 1.2, 0))
    return b.finish()


# ---------------------------------------------------------------- 小物：自販機・バス停
def build_vending():
    b = Builder("VendingMachine")
    b.box((1.0, 0.75, 1.85), (0, 0, 0.925), "Vend_Red")
    b.box((0.86, 0.04, 1.0), (0, -0.38, 1.2), "Vend_White")
    for r in range(3):                                                              # 商品の見本
        for c in range(6):
            b.box((0.1, 0.03, 0.2), (-0.33 + c * 0.13, -0.41, 0.85 + r * 0.32),
                  ("Tin_Blue", "Sign_Yellow", "Vend_Red", "Glass")[(r + c) % 4])
    b.box((0.7, 0.05, 0.18), (0, -0.39, 0.25), "Opening")                          # 取り出し口
    b.box((1.04, 0.79, 0.08), (0, 0, 1.89), "Vend_White")
    b.collider((1.0, 0.75, 1.85), (0, 0, 0.925))
    b.entrance((0, -1.0, 0))
    return b.finish()


def build_busstop():
    b = Builder("BusStop")
    W, D, H = 3.2, 1.4, 2.4
    b.box((W, D, 0.08), (0, 0, 0.04), "Stone")
    for x in (-W / 2 + 0.1, W / 2 - 0.1):
        for y in (-D / 2 + 0.1, D / 2 - 0.1):
            b.box((0.1, 0.1, H), (x, y, H / 2), "Metal")
    b.box((W, 0.06, H - 0.4), (0, D / 2 - 0.05, H / 2 + 0.1), "Tin_Blue")           # 背面の板
    b.box((W + 0.4, D + 0.5, 0.06), (0, -0.05, H + 0.05), "Tin_Rust", (-6, 0, 0))   # 屋根
    b.box((W - 0.5, 0.4, 0.06), (0, 0.35, 0.45), "Wood_Light")                      # ベンチ
    for x in (-1.0, 1.0):
        b.box((0.06, 0.35, 0.42), (x, 0.35, 0.21), "Metal")
    # 丸い標識
    b.cyl(0.04, 2.3, (W / 2 + 0.6, -0.4, 1.15), "Metal")
    b.cyl(0.3, 0.04, (W / 2 + 0.6, -0.4, 2.2), "Sign_Yellow", (90, 0, 0), verts=16)
    b.box((0.5, 0.05, 0.3), (W / 2 + 0.6, -0.43, 1.8), "Vend_White")
    b.collider((W, 0.2, H), (0, D / 2 - 0.05, H / 2))
    b.entrance((0, -D / 2 - 1.0, 0))
    return b.finish()


# ---------------------------------------------------------------- 書き出し
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
    """確認用：斜め上から 1 枚撮る（Unity の見下ろしカメラに近い角度）。"""
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
    bpy.ops.object.camera_add(location=(-22, -30, 24))
    cam = bpy.context.active_object
    direction = Vector((objs_center[0], objs_center[1], 1.5)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40
    scene.camera = cam
    bpy.ops.object.light_add(type="SUN", rotation=(math.radians(50), 0, math.radians(30)))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
builds = [
    ("Minka", lambda: build_minka("Minka")),
    ("Minka_Ruined", lambda: build_minka("Minka_Ruined", ruined=True)),
    ("Kura", build_kura),
    ("Barn", build_barn),
    ("VendingMachine", build_vending),
    ("BusStop", build_busstop),
]
made = []
for name, fn in builds:
    obj = fn()
    export(obj, os.path.join(OUT_DIR, f"{name}.fbx"))
    made.append(obj)
    print("EXPORTED", name, "verts", len(obj.data.vertices), "dims", tuple(round(d, 2) for d in obj.dimensions))

if PREVIEW_DIR:
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    # 一列に並べて撮影
    x = 0.0
    for o in made:
        o.location.x = x + o.dimensions.x / 2
        x += o.dimensions.x + 3
    objs_center = (x / 2, 0)
    render_preview(made, os.path.join(PREVIEW_DIR, "buildings_preview.png"))
    print("PREVIEW", PREVIEW_DIR)
