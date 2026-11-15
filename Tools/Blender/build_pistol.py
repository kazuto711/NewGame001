# 仮の拳銃モデルを作る Blender スクリプト（ローポリ＋面取り）。
#   blender -b --factory-startup -P build_pistol.py -- <出力.fbx>
# 原点 = グリップの上端（手で握る位置）。銃口は Blender の -Y 方向（Unity では +Z 方向になる）。
# 銃口の位置に「Muzzle」という空のオブジェクトを置く（弾・マズルフラッシュの発生位置に使う）。
import sys
import math
import bpy

OUT = sys.argv[sys.argv.index("--") + 1:][0]
bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, color, metal, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = color
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = color
    return m


M_SLIDE = mat("Pistol_Slide", (0.10, 0.10, 0.11, 1), 0.7, 0.35)
M_FRAME = mat("Pistol_Frame", (0.22, 0.23, 0.18, 1), 0.0, 0.7)   # くすんだオリーブの樹脂フレーム
M_GRIP = mat("Pistol_Grip", (0.07, 0.07, 0.07, 1), 0.0, 0.85)

parts = []


def box(name, size, loc, rot=(0, 0, 0), material=M_FRAME, bevel=0.0025):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(material)
    if bevel > 0:
        m = o.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 2
        bpy.ops.object.modifier_apply(modifier=m.name)
    parts.append(o)
    return o


def cyl(name, r, depth, loc, rot, material):
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=r, depth=depth, location=loc,
                                        rotation=[math.radians(a) for a in rot])
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(material)
    parts.append(o)
    return o


# 寸法の単位は m。前方（銃口側）が -Y
box("Slide", (0.028, 0.190, 0.034), (0, -0.030, 0.037), material=M_SLIDE)
box("SlideSerrations", (0.030, 0.030, 0.022), (0, 0.045, 0.040), material=M_GRIP, bevel=0.001)
box("Frame", (0.026, 0.165, 0.020), (0, -0.030, 0.010))
box("Grip", (0.029, 0.046, 0.112), (0, 0.020, -0.050), rot=(-16, 0, 0), material=M_GRIP)
box("Magazine", (0.022, 0.034, 0.010), (0, 0.040, -0.107), rot=(-16, 0, 0), material=M_SLIDE, bevel=0.001)
# 用心鉄（トリガーガード）
box("Guard_Front", (0.010, 0.006, 0.026), (0, -0.040, -0.010), bevel=0.001)
box("Guard_Bottom", (0.010, 0.048, 0.006), (0, -0.018, -0.024), bevel=0.001)
box("Trigger", (0.006, 0.006, 0.018), (0, -0.012, -0.006), rot=(15, 0, 0), material=M_SLIDE, bevel=0.0)
# 照準
box("FrontSight", (0.004, 0.006, 0.006), (0, -0.118, 0.057), material=M_SLIDE, bevel=0.0)
box("RearSight", (0.020, 0.006, 0.006), (0, 0.058, 0.057), material=M_SLIDE, bevel=0.0)
# 銃身の先端
cyl("Barrel", 0.0065, 0.012, (0, -0.126, 0.037), (90, 0, 0), M_SLIDE)

# 1つのメッシュにまとめる
bpy.ops.object.select_all(action="DESELECT")
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
gun = bpy.context.active_object
gun.name = "Pistol"
bpy.ops.object.shade_flat()

# 銃口の目印
bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0, -0.134, 0.037))
muzzle = bpy.context.active_object
muzzle.name = "Muzzle"
muzzle.parent = gun

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={"MESH", "EMPTY"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", bake_anim=False, mesh_smooth_type="FACE",
)
print("EXPORTED", OUT, "verts", len(gun.data.vertices))
