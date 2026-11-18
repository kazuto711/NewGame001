# パワーアーマー「纏・参型（仮）」の部品を作る Blender スクリプト。
#   blender -b --factory-startup -P build_power_armor.py -- <出力.fbx> [<確認画像.png>]
#
# 作り方の約束（Unity 側 PowerArmorSetup が読む）:
#   - 部品ごとに 1 オブジェクト、名前は "PA_<部位>"（例: PA_Chest, PA_UpperArm_L）
#   - 各部品は「骨の向き」を Z 軸（上）にして、原点＝骨の中央付近に作る
#   - 子の空オブジェクト "Front" が部品の正面（キャラの正面側）を示す
#     （足だけは「足の甲」側を Front にする）
# 骨格の寸法（主人公）: 上腕 0.25m / 前腕 0.24m / 手 0.10m / 太もも 0.43m / すね 0.46m / 足 0.16m
import sys
import math
import bpy
import bmesh
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT = args[0]
PREVIEW = args[1] if len(args) > 1 else None

PALETTE = {  # 見た目の色（sRGB）。材質にはリニアへ変換して入れる
    "Main":   (0.40, 0.44, 0.34),   # くすんだオリーブグレー（自衛隊の装備のような色味）
    "Dark":   (0.13, 0.14, 0.13),
    "Trim":   (0.58, 0.53, 0.40),   # 縁の擦れた明るい部分
    "Rust":   (0.50, 0.26, 0.12),
    "Hazard": (0.86, 0.66, 0.12),
    "Metal":  (0.45, 0.46, 0.48),
    "Rubber": (0.08, 0.08, 0.08),
    "Visor":  (1.00, 0.55, 0.12),   # 目の発光（Unity で発光させる）
}
MATS = {}


def mat(name):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new("M_PA_" + name)
    c = tuple(v ** 2.2 for v in PALETTE[name])
    m.diffuse_color = (*c, 1)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*c, 1)
    b.inputs["Roughness"].default_value = 0.6
    b.inputs["Metallic"].default_value = 0.3 if name in ("Metal", "Main") else 0.0
    MATS[name] = m
    return m


class Piece:
    def __init__(self, name):
        self.name = name
        self.parts = []

    def _add(self, o, m, bevel):
        o.data.materials.append(mat(m))
        if bevel > 0:
            mod = o.modifiers.new("Bevel", "BEVEL")
            mod.width = bevel
            mod.segments = 1
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.modifier_apply(modifier=mod.name)
        self.parts.append(o)
        return o

    def box(self, size, loc, m="Main", rot=(0, 0, 0), bevel=0.012):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        o.scale = size
        bpy.ops.object.transform_apply(scale=True)
        return self._add(o, m, bevel)

    def cyl(self, r, h, loc, m="Main", rot=(0, 0, 0), verts=8, r2=None):
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r if r2 is None else r2, depth=h,
                                        location=loc, rotation=[math.radians(a) for a in rot])
        o = bpy.context.active_object
        return self._add(o, m, 0.0)

    def finish(self, front=(0, -0.4, 0)):
        bpy.ops.object.select_all(action="DESELECT")
        for p in self.parts:
            p.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        bpy.ops.object.join()
        o = bpy.context.active_object
        o.name = self.name
        bpy.ops.object.shade_flat()
        # 向きの目印（Unity 側は位置だけを見るので、取り込み時の軸変換に左右されない）
        for mark, loc in (("Front", front), ("Up", (0, 0, 0.4))):
            bpy.ops.object.empty_add(type="PLAIN_AXES", location=loc)
            e = bpy.context.active_object
            e.name = mark
            e.parent = o
        return o


# 正面は -Y。Z が骨の向き（上）。
def helmet():
    p = Piece("PA_Helmet")
    p.box((0.32, 0.36, 0.30), (0, 0.01, 0.0), bevel=0.05)                     # 頭部
    p.box((0.28, 0.10, 0.12), (0, -0.16, -0.10), "Dark", bevel=0.02)           # あご当て
    p.box((0.30, 0.04, 0.12), (0, -0.185, 0.035), "Dark", bevel=0.01)          # 目の溝
    p.box((0.24, 0.03, 0.035), (0, -0.205, 0.04), "Visor", bevel=0.0)          # 発光する目（横一文字）
    p.box((0.06, 0.34, 0.06), (0, 0.02, 0.165), "Trim", bevel=0.01)            # 頭頂の稜線
    for sx in (-1, 1):
        p.cyl(0.065, 0.06, (sx * 0.175, 0.02, -0.01), "Dark", (0, 90, 0))      # 耳の機械
    p.cyl(0.008, 0.28, (0.13, 0.12, 0.26), "Metal")                            # アンテナ
    return p.finish()


def chest():
    p = Piece("PA_Chest")
    p.box((0.62, 0.42, 0.40), (0, -0.01, 0.02), bevel=0.04)                    # 胸の装甲
    p.box((0.46, 0.06, 0.26), (0, -0.22, 0.04), "Trim", bevel=0.02)            # 胸当て
    for k in range(4):                                                           # 警告色の斜線
        p.box((0.05, 0.02, 0.14), (-0.18 + k * 0.05, -0.255, 0.12), "Hazard", (0, 35, 0), bevel=0.0)
    p.box((0.38, 0.36, 0.08), (0, 0.0, 0.25), "Dark", bevel=0.02)              # 首まわり
    # 背中の動力ユニット
    p.box((0.52, 0.28, 0.58), (0, 0.33, -0.02), "Dark", bevel=0.03)
    p.box((0.46, 0.04, 0.46), (0, 0.48, -0.02), "Main", bevel=0.015)
    for k in range(5):
        p.box((0.36, 0.02, 0.025), (0, 0.505, -0.18 + k * 0.08), "Rubber", bevel=0.0)  # 放熱スリット
    for sx in (-1, 1):
        p.cyl(0.045, 0.24, (sx * 0.18, 0.40, 0.36), "Metal")                    # 排気筒
        p.cyl(0.05, 0.04, (sx * 0.18, 0.40, 0.49), "Rust")
    return p.finish()


def abdomen():
    p = Piece("PA_Abdomen")
    for k in range(3):
        p.box((0.50 - k * 0.02, 0.36, 0.075), (0, -0.01, -0.08 + k * 0.08), "Main" if k % 2 == 0 else "Dark", bevel=0.015)
    return p.finish()


def pelvis():
    p = Piece("PA_Pelvis")
    p.box((0.52, 0.38, 0.13), (0, 0, 0.0), "Dark", bevel=0.02)                  # ベルト
    p.box((0.30, 0.06, 0.24), (0, -0.21, -0.12), "Main", bevel=0.02)           # 前の垂れ
    for sx in (-1, 1):
        p.box((0.06, 0.24, 0.26), (sx * 0.27, 0, -0.12), "Main", bevel=0.02)   # 横の垂れ
        p.box((0.05, 0.10, 0.08), (sx * 0.27, -0.06, 0.0), "Hazard", bevel=0.0)
    return p.finish()


def upper_arm(name):
    p = Piece(name)
    p.cyl(0.115, 0.22, (0, 0, 0.0), "Main", verts=8)                             # 上腕の筒
    p.cyl(0.165, 0.10, (0, 0, -0.10), "Trim", verts=8, r2=0.14)                  # 肩当て（縁）
    p.cyl(0.155, 0.08, (0, 0, -0.16), "Main", verts=8, r2=0.12)
    p.box((0.04, 0.24, 0.06), (0, 0, 0.09), "Dark", bevel=0.0)                   # ひじ側の帯
    return p.finish()


def lower_arm(name):
    p = Piece(name)
    p.box((0.19, 0.21, 0.24), (0, 0, 0.01), "Main", bevel=0.03)                 # 籠手
    p.box((0.21, 0.23, 0.05), (0, 0, 0.12), "Dark", bevel=0.01)
    p.box((0.08, 0.04, 0.14), (0, -0.115, 0.0), "Hazard", bevel=0.0)            # 前面の印
    p.cyl(0.10, 0.06, (0, 0, -0.12), "Dark", verts=8)                            # ひじ
    return p.finish()


def hand(name):
    p = Piece(name)
    p.box((0.12, 0.13, 0.12), (0, 0, 0.0), "Dark", bevel=0.02)
    p.box((0.13, 0.05, 0.06), (0, -0.06, 0.04), "Main", bevel=0.01)             # 拳の甲
    return p.finish()


def upper_leg(name):
    p = Piece(name)
    p.cyl(0.14, 0.38, (0, 0, 0.0), "Dark", verts=8)
    p.box((0.22, 0.08, 0.32), (0, -0.12, 0.02), "Main", bevel=0.025)            # 前の板
    p.box((0.08, 0.20, 0.28), (0, 0.0, 0.0), "Main", bevel=0.02)
    return p.finish()


def lower_leg(name):
    p = Piece(name)
    p.box((0.22, 0.24, 0.42), (0, 0.0, -0.02), "Main", bevel=0.035)            # すね当て
    p.box((0.18, 0.10, 0.14), (0, -0.13, 0.20), "Trim", bevel=0.03)            # ひざ
    p.box((0.24, 0.26, 0.05), (0, 0.0, -0.20), "Dark", bevel=0.01)
    p.cyl(0.035, 0.30, (0.0, 0.14, -0.02), "Metal")                              # 後ろの油圧シリンダー
    return p.finish()


def foot(name):
    # 足は Z＝つま先の向き、-Y＝足の甲（上）。底は +Y 側
    p = Piece(name)
    p.box((0.21, 0.16, 0.32), (0, 0.0, 0.0), "Main", bevel=0.03)
    p.box((0.23, 0.06, 0.34), (0, 0.10, 0.0), "Rubber", bevel=0.01)             # 靴底
    p.box((0.18, 0.06, 0.10), (0, -0.09, 0.08), "Hazard", bevel=0.0)
    return p.finish()


bpy.ops.wm.read_factory_settings(use_empty=True)
pieces = [helmet(), chest(), abdomen(), pelvis()]
for side in ("L", "R"):
    pieces += [upper_arm(f"PA_UpperArm_{side}"), lower_arm(f"PA_LowerArm_{side}"), hand(f"PA_Hand_{side}"),
               upper_leg(f"PA_UpperLeg_{side}"), lower_leg(f"PA_LowerLeg_{side}"), foot(f"PA_Foot_{side}")]

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={"MESH", "EMPTY"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE")
print("EXPORTED", OUT, len(pieces), "pieces", sum(len(p.data.vertices) for p in pieces), "verts")

if PREVIEW:
    # 確認用：部品を格子状に並べて撮る
    for i, o in enumerate(pieces):
        o.location = ((i % 8) * 0.8, (i // 8) * 1.0, 0)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 800
    bpy.ops.object.camera_add(location=(2.8, -5.5, 3.0))
    cam = bpy.context.active_object
    cam.rotation_euler = (Vector((2.8, 0.5, 0)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
