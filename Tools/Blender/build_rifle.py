# 持ち込まれたライフル（AK47 FBX）を、ゲームで使える形に整える Blender スクリプト。
#   blender -b --factory-startup -P build_rifle.py -- <元.fbx> <出力.fbx>
#  - 原点をグリップ（右手で握る位置）に移動
#  - 銃口に「Muzzle」、左手で支える位置に「SupportHand」という目印（空オブジェクト）を追加
#  - カメラなど不要なものを削除
# 銃口は Blender の -Y 方向（Unity では +Z）。
import sys
import bpy
from mathutils import Vector

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for o in list(bpy.data.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o, do_unlink=True)
gun = next(o for o in bpy.data.objects if o.type == "MESH")
gun.name = "AK47"
bpy.context.view_layer.objects.active = gun
gun.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

vs = [v.co.copy() for v in gun.data.vertices]
min_y = min(v.y for v in vs)

# グリップ：後ろ寄り（y>0）で下に伸びている部分の中心
grip = [v for v in vs if 0.0 < v.y < 0.16 and -0.11 < v.z < -0.05]
grip_y = sum(v.y for v in grip) / len(grip)
grip_top = Vector((0.0, grip_y, -0.03))

# 銃身の中心の高さ：銃口付近の断面の下側から推定
muzzle_slice = [v for v in vs if v.y < min_y + 0.02]
barrel_z = (min(v.z for v in muzzle_slice) + 0.053) * 0.5 if muzzle_slice else 0.035
muzzle = Vector((0.0, min_y - 0.005, barrel_z))

# 左手：ハンドガード（銃身の下の木の部分）の下面
guard = [v for v in vs if -0.33 < v.y < -0.25]
support = Vector((0.0, -0.29, min(v.z for v in guard) if guard else 0.0))

# 原点をグリップへ
for v in gun.data.vertices:
    v.co -= grip_top
gun.data.update()

for name, p in (("Muzzle", muzzle - grip_top), ("SupportHand", support - grip_top)):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=p)
    e = bpy.context.active_object
    e.name = name
    e.parent = gun

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=out, use_selection=True, object_types={"MESH", "EMPTY"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", bake_anim=False, path_mode="STRIP",
)
print("EXPORTED", out, "grip_y=%.3f muzzle=%s support=%s" % (grip_y, tuple(round(x, 3) for x in (muzzle - grip_top)),
                                                            tuple(round(x, 3) for x in (support - grip_top))))
