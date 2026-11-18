# 採集・探索で使う小物を作る Blender スクリプト（トゥーン向けのローポリ、テクスチャ無しの色分け）。
#   blender -b --factory-startup -P build_field_props.py -- <出力フォルダ> [<確認画像.png>]
#
# 作るもの
#   Item_*  … 地面に落ちているアイテム
#   Node_*  … 採集できる場所（薬草・廃材・鉄くず）
#   入れ物  … Crate、Tansu、ToolBox、Locker
#   背景    … Jizo、Well、KeiTruck、Tiller、RoadSign、BambooFence、StoneLantern、CrateStack、TireStack、
#              Mailbox、Hasa、Bicycle、Bench、ToriiSmall、Shelf、LowTable、RiceBags
#
# Unity 側との約束（build_wreck_props.py と同じ）: "COL_" で始まる箱は当たり判定。原点は地面の高さ・物の中心。
import math
import os
import sys
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR = args[0]
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT_DIR, exist_ok=True)

PALETTE = {
    "Wood":      (0.42, 0.29, 0.17), "WoodDark":  (0.22, 0.14, 0.09), "WoodGrey": (0.40, 0.36, 0.31),
    "Stone":     (0.50, 0.49, 0.45), "StoneDark": (0.33, 0.33, 0.31), "Moss":     (0.30, 0.36, 0.17),
    "Steel":     (0.42, 0.43, 0.44), "SteelDark": (0.20, 0.21, 0.22), "Rust":     (0.42, 0.21, 0.11),
    "Rubber":    (0.07, 0.07, 0.07), "White":     (0.80, 0.79, 0.72), "Cream":    (0.70, 0.66, 0.52),
    "Red":       (0.62, 0.13, 0.10), "Blue":      (0.20, 0.33, 0.45), "Olive":    (0.30, 0.33, 0.17),
    "Green":     (0.25, 0.46, 0.17), "GreenLight":(0.47, 0.62, 0.22), "Yellow":   (0.80, 0.63, 0.15),
    "Straw":     (0.70, 0.57, 0.30), "Bamboo":    (0.55, 0.53, 0.27), "Cloth":    (0.62, 0.56, 0.45),
    "Glass":     (0.14, 0.19, 0.21), "Copper":    (0.70, 0.43, 0.18), "Torii":    (0.62, 0.17, 0.10),
}
MATS = {}


def mat(name):
    if name not in MATS:
        m = bpy.data.materials.new("M_Prop_" + name)
        c = tuple(v ** 2.2 for v in PALETTE[name])
        m.diffuse_color = (*c, 1)
        m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*c, 1)
        b.inputs["Roughness"].default_value = 0.85
        MATS[name] = m
    return MATS[name]


class Builder:
    def __init__(self, name):
        self.name, self.parts, self.cols = name, [], []

    def box(self, size, loc, m, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.data.materials.append(mat(m))
        self.parts.append(o)

    def cyl(self, r, h, loc, m, rot=(0, 0, 0), verts=10, r2=None):
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r if r2 is None else r2, depth=h,
                                        location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.data.materials.append(mat(m))
        self.parts.append(o)

    def ball(self, r, loc, m, scale=(1, 1, 1)):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=loc)
        o = bpy.context.active_object
        o.scale = scale
        bpy.ops.object.transform_apply(scale=True)
        o.data.materials.append(mat(m))
        self.parts.append(o)

    def col(self, size, loc):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        o.name = f"COL_{self.name}_{len(self.cols)}"
        self.cols.append(o)

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


# ---------------------------------------------------------------- アイテム（見つけやすいよう実物より少し大きい）
def item_can():
    b = Builder("Item_Can")
    b.cyl(0.075, 0.13, (0, 0, 0.065), "Steel", verts=14)
    b.cyl(0.077, 0.08, (0, 0, 0.065), "Red", verts=14)
    return b.finish()


def item_bottle():
    b = Builder("Item_Bottle")
    b.cyl(0.06, 0.2, (0, 0, 0.1), "Blue", verts=12)
    b.cyl(0.06, 0.07, (0, 0, 0.235), "Blue", verts=12, r2=0.024)
    b.cyl(0.027, 0.04, (0, 0, 0.285), "White", verts=10)
    return b.finish()


def item_bandage():
    b = Builder("Item_Bandage")
    b.cyl(0.09, 0.11, (0, 0, 0.09), "White", rot=(90, 0, 0), verts=14)
    b.cyl(0.035, 0.115, (0, 0, 0.09), "Cream", rot=(90, 0, 0), verts=10)
    b.box((0.05, 0.1, 0.012), (0.1, 0, 0.008), "White")
    return b.finish()


def item_medkit():
    b = Builder("Item_Medkit")
    b.box((0.34, 0.24, 0.13), (0, 0, 0.065), "White")
    b.box((0.13, 0.04, 0.012), (0, 0, 0.135), "Red")
    b.box((0.04, 0.13, 0.012), (0, 0, 0.135), "Red")
    b.box((0.1, 0.03, 0.03), (0, -0.13, 0.11), "SteelDark")
    return b.finish()


def item_ammo():
    b = Builder("Item_Ammo")
    b.box((0.3, 0.16, 0.16), (0, 0, 0.08), "Olive")
    b.box((0.31, 0.17, 0.03), (0, 0, 0.165), "SteelDark")
    b.box((0.12, 0.03, 0.02), (0, 0, 0.19), "Steel")
    b.box((0.12, 0.005, 0.05), (0, -0.082, 0.08), "Yellow")
    return b.finish()


def item_herb():
    b = Builder("Item_Herb")
    for i in range(5):
        a = math.radians(i * 72)
        b.ball(0.07, (math.cos(a) * 0.07, math.sin(a) * 0.07, 0.05), "Green", (1.3, 0.6, 0.35))
    b.cyl(0.012, 0.12, (0, 0, 0.06), "GreenLight", verts=6)
    return b.finish()


def item_wood():
    b = Builder("Item_Wood")
    b.box((0.42, 0.09, 0.05), (0, -0.05, 0.03), "Wood", (0, 0, 8))
    b.box((0.42, 0.09, 0.05), (0, 0.06, 0.03), "WoodGrey", (0, 0, -6))
    b.box((0.4, 0.09, 0.05), (0.01, 0, 0.08), "Wood", (0, 0, 20))
    return b.finish()


def item_scrap():
    b = Builder("Item_Scrap")
    b.box((0.26, 0.14, 0.02), (0, 0, 0.03), "Rust", (8, 12, 20))
    b.box((0.16, 0.16, 0.02), (0.06, 0.05, 0.05), "Steel", (-14, 20, 40))
    b.cyl(0.02, 0.28, (-0.04, -0.05, 0.03), "SteelDark", (84, 0, 30), verts=8)
    return b.finish()


def item_cloth():
    b = Builder("Item_Cloth")
    b.box((0.3, 0.22, 0.05), (0, 0, 0.025), "Cloth")
    b.box((0.26, 0.18, 0.04), (0.01, 0.01, 0.065), "Blue", (0, 0, 8))
    return b.finish()


def item_parts():
    b = Builder("Item_Parts")
    b.cyl(0.09, 0.04, (0, 0, 0.03), "Steel", verts=12)
    for i in range(8):
        a = i * 45
        r = math.radians(a)
        b.box((0.04, 0.03, 0.04), (math.cos(r) * 0.105, math.sin(r) * 0.105, 0.03), "Steel", (0, 0, a))
    b.cyl(0.03, 0.05, (0, 0, 0.03), "SteelDark", verts=8)
    b.box((0.2, 0.05, 0.05), (0.14, 0.12, 0.03), "Copper", (0, 0, 30))
    return b.finish()


def item_charm():
    b = Builder("Item_Charm")
    b.box((0.11, 0.03, 0.17), (0, 0, 0.1), "Red")
    b.box((0.07, 0.033, 0.07), (0, 0, 0.1), "Yellow")
    b.cyl(0.01, 0.05, (0, 0, 0.21), "White", verts=6)
    return b.finish()


# ---------------------------------------------------------------- 採集できる場所
def node_herb():
    b = Builder("Node_Herb")
    for i, (x, y) in enumerate(((0, 0), (0.28, 0.12), (-0.25, 0.16), (0.08, -0.28), (-0.2, -0.2))):
        h = 0.24 + (i % 3) * 0.07
        b.cyl(0.012, h, (x, y, h / 2), "GreenLight", verts=6)
        for k in range(5):
            a = math.radians(k * 72 + i * 30)
            b.ball(0.1, (x + math.cos(a) * 0.09, y + math.sin(a) * 0.09, h * 0.65), "Green", (1.4, 0.55, 0.3))
        b.ball(0.035, (x, y, h + 0.02), "Yellow")
    return b.finish()


def node_woodpile():
    b = Builder("Node_WoodPile")
    for row, n in enumerate((4, 3, 2)):
        for i in range(n):
            y = (i - (n - 1) / 2) * 0.3
            b.cyl(0.14, 1.3, ((row % 2) * 0.1, y, 0.14 + row * 0.25), "Wood" if (i + row) % 2 else "WoodGrey", (0, 90, 0), verts=8)
    return b.finish()


def node_scrappile():
    b = Builder("Node_ScrapPile")
    b.ball(0.55, (0, 0, 0.05), "SteelDark", (1.3, 1.0, 0.55))
    for i in range(9):
        a = math.radians(i * 40)
        r = 0.2 + (i % 3) * 0.16
        b.box((0.42, 0.26, 0.03), (math.cos(a) * r, math.sin(a) * r, 0.24 + (i % 2) * 0.1),
              "Rust" if i % 3 else "Steel", (i * 17 - 30, i * 23 - 40, i * 41))
    b.cyl(0.02, 0.9, (0.1, -0.1, 0.42), "Steel", (60, 10, 40), verts=8)
    b.cyl(0.22, 0.1, (-0.4, 0.3, 0.2), "Rubber", (70, 0, 20), verts=12)
    return b.finish()


# ---------------------------------------------------------------- 入れ物
def crate():
    b = Builder("Crate")
    S = 0.8
    b.box((S, S, S * 0.72), (0, 0, S * 0.36), "Wood")
    for x in (-1, 1):
        for y in (-1, 1):
            b.box((0.08, 0.08, S * 0.74), (x * (S / 2 - 0.02), y * (S / 2 - 0.02), S * 0.37), "WoodDark")
    for z in (0.1, S * 0.62):
        b.box((S + 0.04, S + 0.04, 0.08), (0, 0, z), "WoodDark")
    b.col((S, S, S * 0.72), (0, 0, S * 0.36))
    return b.finish()


def tansu():
    b = Builder("Tansu")
    W, D, H = 1.1, 0.45, 1.2
    b.box((W, D, H), (0, 0, H / 2), "WoodDark")
    for r in range(4):
        z = 0.18 + r * 0.28
        b.box((W - 0.1, 0.02, 0.24), (0, -D / 2 - 0.005, z), "Wood")
        for sx in (-1, 1):
            b.box((0.12, 0.03, 0.03), (sx * 0.28, -D / 2 - 0.02, z), "Copper")
    b.col((W, D, H), (0, 0, H / 2))
    return b.finish()


def toolbox():
    b = Builder("ToolBox")
    b.box((0.7, 0.32, 0.26), (0, 0, 0.13), "Red")
    b.box((0.72, 0.34, 0.06), (0, 0, 0.29), "Rust")
    b.box((0.3, 0.04, 0.04), (0, 0, 0.38), "SteelDark")
    for sx in (-1, 1):
        b.box((0.04, 0.04, 0.09), (sx * 0.15, 0, 0.34), "SteelDark")
    b.col((0.7, 0.32, 0.34), (0, 0, 0.17))
    return b.finish()


def locker():
    b = Builder("Locker")
    b.box((0.6, 0.5, 1.8), (0, 0, 0.9), "Blue")
    b.box((0.54, 0.02, 1.7), (0, -0.255, 0.9), "SteelDark")
    b.box((0.5, 0.025, 1.64), (0, -0.26, 0.9), "Blue")
    for z in (1.5, 1.56, 1.62):
        b.box((0.3, 0.03, 0.02), (0, -0.275, z), "SteelDark")
    b.box((0.03, 0.04, 0.14), (0.19, -0.28, 0.95), "Steel")
    b.box((0.2, 0.03, 0.5), (-0.1, -0.277, 0.5), "Rust")
    b.col((0.6, 0.5, 1.8), (0, 0, 0.9))
    return b.finish()


# ---------------------------------------------------------------- 背景
def jizo():
    b = Builder("Jizo")
    b.box((0.7, 0.6, 0.2), (0, 0, 0.1), "StoneDark")
    b.cyl(0.24, 0.62, (0, 0, 0.51), "Stone", verts=10, r2=0.17)
    b.ball(0.17, (0, 0, 0.94), "Stone")
    b.cyl(0.2, 0.2, (0, -0.02, 0.72), "Red", verts=10, r2=0.13)
    b.box((0.2, 0.1, 0.1), (0, 0.32, 0.25), "Moss")
    b.col((0.7, 0.6, 1.1), (0, 0, 0.55))
    return b.finish()


def well():
    b = Builder("Well")
    b.cyl(0.72, 0.7, (0, 0, 0.35), "Stone", verts=14)
    b.cyl(0.56, 0.72, (0, 0, 0.36), "SteelDark", verts=14)
    for sx in (-1, 1):
        b.box((0.12, 0.12, 2.1), (sx * 0.85, 0, 1.05), "WoodDark")
    b.box((2.0, 0.1, 0.1), (0, 0, 1.75), "WoodDark")
    b.box((2.2, 0.75, 0.05), (0, -0.33, 2.25), "Rust", (32, 0, 0))
    b.box((2.2, 0.75, 0.05), (0, 0.33, 2.25), "Rust", (-32, 0, 0))
    b.cyl(0.015, 0.8, (0, 0, 1.3), "Straw", verts=6)
    b.cyl(0.13, 0.2, (0, 0, 0.85), "Wood", verts=10)
    for sx in (-1, 1):
        b.col((0.14, 0.14, 2.1), (sx * 0.85, 0, 1.05))
    b.col((1.3, 1.3, 0.7), (0, 0, 0.35))
    return b.finish()


def kei_truck():
    b = Builder("KeiTruck")
    b.box((1.42, 1.25, 1.1), (0, 1.05, 0.98), "White")
    b.box((1.26, 0.03, 0.5), (0, 1.68, 1.2), "Glass")
    for sx in (-1, 1):
        b.box((0.03, 0.72, 0.42), (sx * 0.72, 1.1, 1.22), "Glass")
        b.box((0.14, 0.03, 0.12), (sx * 0.52, 1.68, 0.72), "Cream")
    b.box((1.45, 0.12, 0.2), (0, 1.7, 0.48), "SteelDark")
    b.box((1.42, 2.0, 0.08), (0, -0.62, 0.68), "SteelDark")
    b.box((1.42, 0.05, 0.36), (0, -1.6, 0.9), "White")
    for sx in (-1, 1):
        b.box((0.05, 2.0, 0.36), (sx * 0.69, -0.62, 0.9), "White")
    b.box((1.2, 0.05, 0.4), (0, 0.38, 1.2), "SteelDark")
    b.box((0.55, 0.45, 0.012), (0.2, 1.0, 1.54), "Rust")
    b.box((0.03, 1.1, 0.2), (0.70, -0.75, 0.85), "Rust")
    for x, y in ((-0.62, 1.15), (0.62, 1.15), (-0.62, -0.95), (0.62, -0.95)):
        b.cyl(0.26, 0.18, (x, y, 0.26), "Rubber", (0, 90, 0), verts=12)
    b.box((0.7, 0.6, 0.25), (-0.2, -0.7, 0.85), "Cloth", (0, 0, 12))
    b.col((1.45, 3.4, 1.0), (0, 0.05, 0.75))
    return b.finish()


def tiller():
    b = Builder("Tiller")
    b.box((0.42, 0.55, 0.4), (0, 0.15, 0.62), "Red")
    b.box((0.34, 0.3, 0.2), (0, 0.45, 0.88), "SteelDark")
    for sx in (-1, 1):
        b.cyl(0.3, 0.1, (sx * 0.34, 0.2, 0.3), "Rubber", (0, 90, 0), verts=12)
        b.cyl(0.018, 1.1, (sx * 0.2, -0.45, 0.95), "Steel", (55, 0, 0), verts=8)
    b.box((0.46, 0.04, 0.04), (0, -0.9, 1.26), "Rubber")
    b.box((0.2, 0.5, 0.28), (0, -0.25, 0.25), "Rust")
    b.col((0.75, 1.1, 1.0), (0, 0.1, 0.5))
    return b.finish()


def road_sign():
    b = Builder("RoadSign")
    b.cyl(0.04, 2.4, (0, 0, 1.2), "Steel", verts=8)
    b.box((0.72, 0.03, 0.72), (0, -0.05, 2.15), "Yellow", (0, 45, 0))
    b.box((0.54, 0.035, 0.54), (0, -0.05, 2.15), "SteelDark", (0, 45, 0))
    b.box((0.46, 0.04, 0.46), (0, -0.05, 2.15), "Yellow", (0, 45, 0))
    b.box((0.26, 0.012, 0.1), (0.12, -0.075, 2.05), "Rust", (0, 20, 0))
    b.col((0.1, 0.1, 2.4), (0, 0, 1.2))
    return b.finish()


def bamboo_fence():
    b = Builder("BambooFence")
    L = 3.0
    for i in range(int(L / 0.1)):
        x = -L / 2 + 0.05 + i * 0.1
        b.cyl(0.042, 1.3 + (i % 5) * 0.035, (x, 0, 0.65), "Bamboo", verts=6)
    for z in (0.35, 1.0):
        b.box((L + 0.06, 0.06, 0.05), (0, -0.05, z), "WoodDark")
    for x in (-L / 2, L / 2):
        b.cyl(0.07, 1.5, (x, 0, 0.75), "WoodDark", verts=8)
    b.col((L, 0.14, 1.4), (0, 0, 0.7))
    return b.finish()


def stone_lantern():
    b = Builder("StoneLantern")
    b.cyl(0.32, 0.14, (0, 0, 0.07), "StoneDark", verts=6)
    b.cyl(0.11, 0.95, (0, 0, 0.6), "Stone", verts=10)
    b.cyl(0.26, 0.12, (0, 0, 1.12), "Stone", verts=6)
    b.box((0.3, 0.3, 0.28), (0, 0, 1.32), "Stone")
    b.box((0.17, 0.32, 0.16), (0, 0, 1.33), "SteelDark")
    b.cyl(0.42, 0.26, (0, 0, 1.59), "StoneDark", verts=6, r2=0.08)
    b.ball(0.08, (0, 0, 1.76), "Stone")
    b.col((0.5, 0.5, 1.8), (0, 0, 0.9))
    return b.finish()


def crate_stack():
    b = Builder("CrateStack")
    for (x, y, z, s, r) in ((-0.5, 0, 0, 0.9, 6), (0.5, 0.1, 0, 0.8, -8), (-0.15, -0.05, 0.65, 0.65, 18)):
        h = s * 0.72
        b.box((s, s, h), (x, y, z + h / 2), "Wood", (0, 0, r))
        b.box((s + 0.03, s + 0.03, 0.07), (x, y, z + h - 0.05), "WoodDark", (0, 0, r))
        b.box((s + 0.03, s + 0.03, 0.07), (x, y, z + 0.05), "WoodDark", (0, 0, r))
    b.col((1.8, 1.0, 0.68), (0, 0, 0.34))
    b.col((0.7, 0.7, 0.5), (-0.15, -0.05, 0.9))
    return b.finish()


def tire_stack():
    b = Builder("TireStack")
    for i in range(4):
        b.cyl(0.34, 0.19, (0.02 * (i % 2), -0.02 * (i % 3), 0.1 + i * 0.19), "Rubber", verts=14)
    b.cyl(0.17, 0.78, (0, 0, 0.39), "SteelDark", verts=10)
    b.cyl(0.34, 0.19, (0.72, 0.1, 0.34), "Rubber", (72, 0, 30), verts=14)
    b.col((0.7, 0.7, 0.78), (0, 0, 0.39))
    return b.finish()


def mailbox():
    b = Builder("Mailbox")
    b.cyl(0.04, 1.1, (0, 0, 0.55), "WoodDark", verts=8)
    b.box((0.36, 0.26, 0.3), (0, 0, 1.2), "Red")
    b.box((0.4, 0.3, 0.05), (0, 0, 1.37), "Rust")
    b.box((0.22, 0.012, 0.03), (0, -0.135, 1.27), "SteelDark")
    b.col((0.3, 0.3, 1.4), (0, 0, 0.7))
    return b.finish()


def hasa():
    b = Builder("Hasa")
    L = 4.0
    for x in (-L / 2, 0, L / 2):
        for y in (-1, 1):
            b.cyl(0.04, 2.0, (x, y * 0.3, 0.95), "Bamboo", (y * 17, 0, 0), verts=6)
    b.cyl(0.04, L + 0.5, (0, 0, 1.55), "Bamboo", (0, 90, 0), verts=6)
    for i in range(14):
        x = -L / 2 + 0.3 + i * 0.26
        b.box((0.2, 0.2, 0.85), (x, 0, 1.2), "Straw", (0, (i % 3 - 1) * 5, 0))
    for x in (-L / 2, 0, L / 2):
        b.col((0.2, 0.7, 1.8), (x, 0, 0.9))
    return b.finish()


def bicycle():
    b = Builder("Bicycle")
    for y in (-0.52, 0.52):
        b.cyl(0.33, 0.04, (0, y, 0.33), "Rubber", (0, 90, 0), verts=16)
        b.cyl(0.26, 0.05, (0, y, 0.33), "SteelDark", (0, 90, 0), verts=16)
    b.cyl(0.018, 0.9, (0, 0.05, 0.62), "Rust", (84, 0, 0), verts=6)
    b.cyl(0.018, 0.68, (0, -0.2, 0.55), "Rust", (22, 0, 0), verts=6)
    b.cyl(0.018, 0.78, (0, 0.2, 0.6), "Rust", (-44, 0, 0), verts=6)
    b.cyl(0.016, 0.62, (0, 0.46, 0.68), "Steel", (-14, 0, 0), verts=6)
    b.box((0.5, 0.03, 0.03), (0, 0.42, 0.98), "SteelDark")
    b.box((0.12, 0.24, 0.05), (0, -0.33, 0.87), "Rubber")
    b.box((0.3, 0.26, 0.2), (0, 0.66, 0.78), "Steel")
    b.col((0.3, 1.5, 0.9), (0, 0, 0.45))
    return b.finish()


def bench():
    b = Builder("Bench")
    for i in range(3):
        b.box((1.7, 0.13, 0.04), (0, -0.16 + i * 0.16, 0.45), "WoodGrey")
    for i in range(2):
        b.box((1.7, 0.03, 0.12), (0, 0.27, 0.68 + i * 0.17), "WoodGrey", (-8, 0, 0))
    for x in (-0.7, 0.7):
        b.box((0.06, 0.5, 0.45), (x, 0, 0.22), "SteelDark")
        b.box((0.05, 0.05, 0.5), (x, 0.26, 0.68), "SteelDark", (-8, 0, 0))
    b.col((1.7, 0.55, 0.5), (0, 0, 0.25))
    return b.finish()


def torii_small():
    b = Builder("ToriiSmall")
    for sx in (-1, 1):
        b.cyl(0.11, 2.3, (sx * 0.85, 0, 1.15), "Torii", verts=10)
        b.cyl(0.15, 0.22, (sx * 0.85, 0, 0.11), "StoneDark", verts=10)
    b.box((2.3, 0.12, 0.12), (0, 0, 1.85), "Torii")
    b.box((2.7, 0.2, 0.14), (0, 0, 2.3), "Torii")
    b.box((3.0, 0.26, 0.1), (0, 0, 2.42), "SteelDark")
    for sx in (-1, 1):
        b.col((0.24, 0.24, 2.3), (sx * 0.85, 0, 1.15))
    return b.finish()


def shelf():
    b = Builder("Shelf")
    W, D, H = 1.5, 0.45, 1.9
    for sx in (-1, 1):
        b.box((0.05, D, H), (sx * W / 2, 0, H / 2), "WoodDark")
    for k in range(5):
        b.box((W, D, 0.04), (0, 0, 0.1 + k * 0.42), "Wood")
    b.box((W, 0.03, H), (0, D / 2, H / 2), "WoodDark")
    for i, (x, k, m) in enumerate(((-0.45, 1, "Red"), (-0.1, 1, "Steel"), (0.4, 2, "Blue"), (-0.3, 3, "Cream"), (0.25, 3, "Copper"))):
        b.cyl(0.09, 0.22, (x, 0, 0.23 + k * 0.42), m, verts=10)
    b.col((W, D, H), (0, 0, H / 2))
    return b.finish()


def low_table():
    b = Builder("LowTable")
    b.cyl(0.5, 0.05, (0, 0, 0.3), "WoodDark", verts=16)
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        b.box((0.06, 0.06, 0.28), (math.cos(r) * 0.3, math.sin(r) * 0.3, 0.14), "WoodDark")
    b.cyl(0.05, 0.07, (0.14, 0.05, 0.36), "Cream", verts=10)
    b.col((0.8, 0.8, 0.33), (0, 0, 0.165))
    return b.finish()


def rice_bags():
    b = Builder("RiceBags")
    for (x, y, z, r) in ((-0.42, 0, 0.16, 4), (0.42, 0.05, 0.16, -6), (0, 0, 0.45, 12)):
        b.ball(0.45, (x, y, z), "Straw", (0.95, 1.35, 0.42))
        b.cyl(0.46, 0.03, (x, y - 0.3, z), "WoodDark", (90, 0, 0), verts=10, r2=0.46)
    b.col((1.6, 1.1, 0.7), (0, 0, 0.35))
    return b.finish()


BUILDS = [item_can, item_bottle, item_bandage, item_medkit, item_ammo, item_herb, item_wood, item_scrap, item_cloth,
          item_parts, item_charm, node_herb, node_woodpile, node_scrappile, crate, tansu, toolbox, locker, jizo, well,
          kei_truck, tiller, road_sign, bamboo_fence, stone_lantern, crate_stack, tire_stack, mailbox, hasa, bicycle,
          bench, torii_small, shelf, low_table, rice_bags]


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


bpy.ops.wm.read_factory_settings(use_empty=True)
made = []
for fn in BUILDS:
    o = fn()
    export(o, os.path.join(OUT_DIR, o.name + ".fbx"))
    made.append(o)
    print(f"[props] {o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")

if PREVIEW:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = 1800, 1100
    for o in bpy.data.objects:
        if o.name.startswith("COL_"):
            o.hide_render = True
    # アイテムは手前に大きく、背景は奥に並べる
    for i, o in enumerate(made[:14]):
        s = 2.5 if o.name.startswith("Item_") else 1.0
        o.scale = (s, s, s)
        o.location = (-9 + i * 1.4, -6, 0)
    rest = made[14:]
    for i, o in enumerate(rest):
        o.location = (-11 + (i % 7) * 3.8, -1 + (i // 7) * 5, 0)
    bpy.ops.object.camera_add(location=(0, -21, 13))
    cam = bpy.context.active_object
    cam.rotation_euler = (Vector((0, 1.5, 0.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 34
    scene.camera = cam
    scene.world = bpy.data.worlds.new("Preview")
    scene.world.color = (0.5, 0.52, 0.55)
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
print("[props] done", len(made))
