"""Run with Blender --background --factory-startup --python this_file -- <args>.
Use --prepare under plain Python to create command fixtures before the Unity checks.
No user .blend file, preferences, or installed addon is changed.
"""
import argparse
import ast
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import time
sys.dont_write_bytecode = True

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
parser = argparse.ArgumentParser()
parser.add_argument('--addon', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--prepare', action='store_true')
parser.add_argument('--listen', action='store_true')
options = parser.parse_args(args)
output = Path(options.output)
output.mkdir(parents=True, exist_ok=True)

if options.prepare:
    tree = ast.parse(Path(options.addon).read_text(encoding='utf-8-sig'))
    nodes = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in ('_mp_encode_str', '_mp_encode_int', 'encode_command')]
    scope = {'struct': struct}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), options.addon, 'exec'), scope)
    for command in ('pause', 'resume', 'step', 'stop'):
        (output / (command + '.command')).write_bytes(scope['encode_command'](command, 2))
    print('Prepared four commands using the actual Blender addon encoder.')
    sys.exit(0)

import bpy
from mathutils import Matrix
spec = importlib.util.spec_from_file_location('d9speed_pose_sync_validation', options.addon)
addon = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = addon
spec.loader.exec_module(addon)
addon.msgpack = None  # Verify the no-pip-install path too.
addon.register()
scene = bpy.context.scene
data = bpy.data.armatures.new('Synthetic Rig')
rig = bpy.data.objects.new('Synthetic Rig', data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bone = data.edit_bones.new('検証ボーン')
bone.head = (0, 0, 0)
bone.tail = (0, 0, 1)
bpy.ops.object.mode_set(mode='OBJECT')
scene.pose_sync_world_armature = rig
camera = bpy.data.objects.new('Synthetic Camera', bpy.data.cameras.new('Synthetic Camera'))
scene.collection.objects.link(camera)

# Resolve the public scene settings declared by the actual addon.
scene.pose_sync_world_camera = camera
scene.pose_sync_world_apply_camera_object = True
scene.pose_sync_world_apply_root_location = False
scene.pose_sync_world_apply_root_rotation = False

checks = []
def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)

def verify_pose():
    bpy.context.view_layer.update()
    angle = abs(math.degrees(rig.pose.bones['検証ボーン'].matrix_basis.to_quaternion().angle))
    check('Unity 30 degree bone rotation applies in Blender', abs(angle - 30) < .05)
    check('Camera coordinate conversion', (camera.location - addon.Vector((-1, -3, 2))).length < .001)
    check('Camera perspective received', camera.data.type == 'PERSP')

try:
    if options.listen:
        addon.STATE.start('127.0.0.1', 39549)
        deadline = time.monotonic() + 180
        while addon.STATE.server_socket is None and time.monotonic() < deadline:
            time.sleep(.01)
        check('TCP receiver listening', addon.STATE.server_socket is not None)
        (output / 'receiver_ready.txt').write_text('ready')
        got_pose = False
        got_stopped = False
        while time.monotonic() < deadline and not got_stopped:
            for control in addon.STATE.take_controls():
                if control.get('message_type') == 'avatar_defs':
                    addon.apply_avatar_defs(scene, control)
                elif addon.is_unity_stopped_message(control) and got_pose:
                    addon.reset_armature_on_unity_stopped(scene)
                    got_stopped = True
            frame = addon.STATE.take_latest()
            if frame:
                addon.apply_world_frame_v3(scene, frame)
                if not got_pose and frame.get('unity_time', 0) > 1.5:
                    verify_pose()
                    got_pose = True
            time.sleep(.01)
        check('Live pose received over TCP', got_pose)
        check('Stop message received', got_stopped)
        check('Stop restores rest pose', abs(rig.pose.bones['検証ボーン'].matrix_basis.to_quaternion().angle) < .001)
        name = 'blender_tcp_results.json'
    else:
        load = lambda name: addon.unpack_messagepack((output / (name + '.msgpack')).read_bytes())
        definitions, frame = load('definitions'), load('frame')
        check('Japanese name and binary blobs', definitions['avatars'][0]['avatar_name'] == '検証アバター' and len(definitions['avatars'][0]['rest_rotations']) == 16)
        addon.apply_avatar_defs(scene, definitions)
        check('V3 frame applies one bone', addon.apply_world_frame_v3(scene, frame) == 1)
        verify_pose()
        check('64 bit integer preserved', load('state')['frame'] == 9223372036854775807)
        legacy = load('legacy')
        check('V2 maps, nil, float and double preserved', legacy['version'] == 2 and legacy['bones'][0]['human_bone'] is None and abs(legacy['unity_time']-1234.5678) < 1e-8)
        check('UTF-8 and long strings', load('list')['avatars'] == ['日本語', 'x'*32, 'y'*256])
        wide = load('wide_arrays')['avatars']
        check('Array32 and Bin32 lengths', len(wide) == 17 and len(wide[0]['targets']) == 17 and len(wide[0]['rest_rotations']) == 70000)
        name = 'blender_codec_results.json'
    (output / name).write_text(json.dumps({'passed': True, 'blender': bpy.app.version_string, 'checks': checks}, indent=2))
    print('POSE_SYNC_VALIDATION_PASSED', len(checks))
finally:
    addon.STATE.stop()
    addon.unregister()
