bl_info = {
    "name": "Unity Blender Pose Receiver (World)",
    "author": "d9speed",
    "version": (0, 3, 1),
    "blender": (4, 5, 0),
    "location": "View3D > Sidebar > Pose Sync",
    "description": "Receive root-space world rotations from Unity (Humanoid + arbitrary bones).",
    "category": "Animation",
}

# World-space pose receiver.
#
# The companion sender (BlenderPoseSenderWorld) sends every bone as a ROOT-SPACE
# WORLD rotation plus its rest (bind) world rotation. Each bone is therefore
# independent: there is no FK reconstruction and no hips_world root offset to get
# wrong. This also lets arbitrary, non-Humanoid Transform bones (skirt / hair /
# tail / breast jiggle bones) be driven the same way.
#
# Per bone:
#     dUnity   = rotation * rest_rotation^-1          (root-space world delta)
#     dBlender = G * dUnity * G^T                      (G = (x,y,z)->(-x,-z,y))
#     target   = dBlender * blender_rest_world
# then convert target world orientation to the pose bone's local matrix_basis.

import array
import math
import select
import socket
import struct
import sys
import threading
import time

import bpy
from mathutils import Matrix, Quaternion, Vector

try:
    import msgpack
except Exception:
    msgpack = None


# Unity (left-handed, Y-up, Z-forward) -> Blender (right-handed, Z-up).
# (x, y, z) -> (-x, -z, y). Reflection (det -1), facing -Y.
UNITY_TO_BLENDER_BASIS = Matrix((
    (-1.0, 0.0, 0.0),
    (0.0, 0.0, -1.0),
    (0.0, 1.0, 0.0),
))
# Blender cameras look down local -Z with local +Y as up. Combined with the
# world reflection above this produces a proper camera rotation (det +1).
BLENDER_CAMERA_TO_UNITY_LOCAL = Matrix.Diagonal((1.0, 1.0, -1.0))

# Framing: 4-byte big-endian length, then one kind byte, then the MessagePack body.
# The kind byte lets the receiver drop a backlog of stale POSE packets without
# decoding them while never discarding a CONTROL packet (the avatar_defs handshake
# or a state change), which must always be applied.
PACKET_KIND_POSE = 0
PACKET_KIND_CONTROL = 1
# Blender -> Unity. The TCP link is full duplex, so Play Mode commands ride the
# same connection the poses arrive on; no second port and no extra handshake.
PACKET_KIND_COMMAND = 2

# --------------------------------------------------------------------------
# MessagePack (bundled minimal decoder; used when the `msgpack` module is absent)
# --------------------------------------------------------------------------

_UNPACK_FLOAT32 = struct.Struct(">f").unpack_from
_UNPACK_FLOAT64 = struct.Struct(">d").unpack_from


class MessagePackReader:
    """Minimal MessagePack decoder used when the `msgpack` module is unavailable.

    One pose frame for a ~270 bone rig is roughly 50 KB, and the receiver thread
    decodes it while holding the GIL, so this sits on Blender's critical path.
    The hot codes (fixstr, fixmap, float32) are therefore handled inline here to
    keep decoding to a single Python call per value.
    """

    __slots__ = ("data", "size", "offset")

    def __init__(self, data):
        self.data = data
        self.size = len(data)
        self.offset = 0

    def read(self):
        value = self._read_value()
        if self.offset != self.size:
            raise ValueError("MessagePack payload has trailing bytes.")
        return value

    def _read_bytes(self, size):
        end = self.offset + size
        if end > self.size:
            raise ValueError("Unexpected end of MessagePack payload.")
        value = self.data[self.offset:end]
        self.offset = end
        return value

    def _read_uint(self, size):
        return int.from_bytes(self._read_bytes(size), "big", signed=False)

    def _read_int(self, size):
        return int.from_bytes(self._read_bytes(size), "big", signed=True)

    def _read_value(self):
        data = self.data
        size = self.size
        offset = self.offset
        if offset >= size:
            raise ValueError("Unexpected end of MessagePack payload.")
        code = data[offset]
        offset += 1

        # Hot: bone names and map keys.
        if 0xA0 <= code <= 0xBF:
            end = offset + (code & 0x1F)
            if end > size:
                raise ValueError("Unexpected end of MessagePack payload.")
            self.offset = end
            return data[offset:end].decode("utf-8")

        # Hot: quaternion / vector maps.
        if 0x80 <= code <= 0x8F:
            self.offset = offset
            return self._read_map(code & 0x0F)

        # Hot: quaternion / vector components.
        if code == 0xCA:
            end = offset + 4
            if end > size:
                raise ValueError("Unexpected end of MessagePack payload.")
            self.offset = end
            return _UNPACK_FLOAT32(data, offset)[0]

        self.offset = offset

        if code <= 0x7F:
            return code
        if 0x90 <= code <= 0x9F:
            return self._read_array(code & 0x0F)
        if code >= 0xE0:
            return code - 0x100

        if code == 0xC0:
            return None
        if code == 0xC2:
            return False
        if code == 0xC3:
            return True
        # bin 8/16/32 - raw float32 blocks (schema v3 sends rotations this way).
        if code == 0xC4:
            return self._read_bytes(self._read_uint(1))
        if code == 0xC5:
            return self._read_bytes(self._read_uint(2))
        if code == 0xC6:
            return self._read_bytes(self._read_uint(4))
        if code == 0xCB:
            end = offset + 8
            if end > size:
                raise ValueError("Unexpected end of MessagePack payload.")
            self.offset = end
            return _UNPACK_FLOAT64(data, offset)[0]
        if code == 0xCC:
            return self._read_uint(1)
        if code == 0xCD:
            return self._read_uint(2)
        if code == 0xCE:
            return self._read_uint(4)
        if code == 0xCF:
            return self._read_uint(8)
        if code == 0xD0:
            return self._read_int(1)
        if code == 0xD1:
            return self._read_int(2)
        if code == 0xD2:
            return self._read_int(4)
        if code == 0xD3:
            return self._read_int(8)
        if code == 0xD9:
            return self._read_string(self._read_uint(1))
        if code == 0xDA:
            return self._read_string(self._read_uint(2))
        if code == 0xDB:
            return self._read_string(self._read_uint(4))
        if code == 0xDC:
            return self._read_array(self._read_uint(2))
        if code == 0xDD:
            return self._read_array(self._read_uint(4))
        if code == 0xDE:
            return self._read_map(self._read_uint(2))
        if code == 0xDF:
            return self._read_map(self._read_uint(4))

        raise ValueError(f"Unsupported MessagePack code: 0x{code:02X}")

    def _read_string(self, size):
        return self._read_bytes(size).decode("utf-8")

    def _read_array(self, size):
        return [self._read_value() for _ in range(size)]

    def _read_map(self, size):
        result = {}
        for _ in range(size):
            key = self._read_value()
            result[key] = self._read_value()
        return result


def _mp_encode_str(value):
    data = value.encode("utf-8")
    if len(data) < 32:
        return bytes([0xA0 | len(data)]) + data
    if len(data) < 256:
        return struct.pack(">BB", 0xD9, len(data)) + data
    return struct.pack(">BH", 0xDA, len(data)) + data


def _mp_encode_int(value):
    if 0 <= value < 128:
        return bytes([value])
    return b"\xd3" + struct.pack(">q", int(value))


def encode_command(command, frames=1):
    """Minimal MessagePack writer for the few outbound command messages.

    Only maps of short strings and ints are ever sent, so a full encoder would be
    dead weight next to the decoder we already bundle.
    """
    items = (
        ("message_type", "command"),
        ("command", command),
        ("frames", frames),
    )
    out = bytes([0x80 | len(items)])
    for key, value in items:
        out += _mp_encode_str(key)
        out += _mp_encode_str(value) if isinstance(value, str) else _mp_encode_int(value)
    return out


def unpack_messagepack(payload):
    if msgpack is not None:
        return msgpack.unpackb(payload, raw=False, strict_map_key=False)
    return MessagePackReader(payload).read()


def add_candidate(candidates, seen, name):
    if not name or name in seen:
        return
    candidates.append(name)
    seen.add(name)


def resolve_pose_bone(armature, bone_frame, bone_prefix):
    """Schema v2 helper: resolve a bone carried as a dict inside the frame."""
    return resolve_pose_bone_by_names(
        armature,
        bone_frame.get("target") or bone_frame.get("name"),
        bone_frame.get("human_bone"),
        bone_prefix,
    )


def resolve_pose_bone_by_names(armature, target, human, bone_prefix):
    """Resolve an incoming bone to a pose bone.

    Order: direct target name, prefix+target, then the Humanoid bone name (only
    meaningful for Humanoid bones, which carry a human_bone field).
    """
    pose_bones = armature.pose.bones
    candidates = []
    seen = set()

    add_candidate(candidates, seen, target)
    if bone_prefix and target:
        add_candidate(candidates, seen, f"{bone_prefix}{target}")

    if human:
        add_candidate(candidates, seen, human)
        if bone_prefix:
            add_candidate(candidates, seen, f"{bone_prefix}{human}")

    for candidate in candidates:
        pose_bone = pose_bones.get(candidate)
        if pose_bone is not None:
            return pose_bone
    return None


# --------------------------------------------------------------------------
# Networking state
# --------------------------------------------------------------------------

class AvatarRuntime:
    """Per-avatar receive state.

    Schema v3 streams several avatars over one connection, so everything that
    used to be a single slot on WorldSyncState (virtual root, captured origin,
    resolved bone topology) lives here, one instance per avatar name. The v2
    single-avatar protocol simply uses the one runtime keyed by "".
    """

    def __init__(self, avatar_name=""):
        self.avatar_name = avatar_name
        self.origin_armature_name = ""
        self.origin_armature_matrix_world = None
        # Virtual root: an Empty inserted as the armature's parent during sync so
        # the Unity avatar-root transform can drive the whole rig without touching
        # the armature object's own (authored) transform. Removed on stop.
        self.vroot_name = ""
        self.vroot_child_name = ""
        self.vroot_prev_parent_name = ""
        self.vroot_prev_parent_type = "OBJECT"
        self.vroot_prev_parent_bone = ""
        self.vroot_prev_matrix_parent_inverse = None
        # Resolved topology, keyed so it survives across frames.
        self.topology = None
        self.topology_key = None
        # Scratch buffer reused by the foreach_set pose write.
        self.pose_buffer = None
        # v3: bone definitions received in the avatar_defs handshake.
        self.targets = None
        self.human_bones = None
        self.rest_matrices = None

    def capture_armature_origin(self, armature):
        if armature is None or armature.type != "ARMATURE":
            self.origin_armature_name = ""
            self.origin_armature_matrix_world = None
            return
        self.origin_armature_name = armature.name
        self.origin_armature_matrix_world = armature.matrix_world.copy()

    def clear_definitions(self):
        self.targets = None
        self.human_bones = None
        self.rest_matrices = None
        self.topology = None
        self.topology_key = None
        self.pose_buffer = None


class WorldSyncState:
    def __init__(self):
        self.thread = None
        self.stop_event = threading.Event()
        self.server_socket = None
        self.client_socket = None
        self.latest_frame = None
        # Control messages (handshake, state) must never be dropped, so they queue
        # instead of overwriting each other the way pose frames do.
        self.pending_controls = []
        self.latest_lock = threading.Lock()
        self.status = "Stopped"
        self.timer_registered = False
        # Diagnostics: frames that arrived faster than the main thread could apply
        # them (dropped undecoded), and senders that were kicked off by a newer one.
        self.dropped_frames = 0
        self.superseded_count = 0
        # avatar_name -> AvatarRuntime. "" is the v2 / single-avatar slot.
        self.avatars = {}
        # Avatar names seen in the last handshake, for the panel's picker.
        self.known_avatar_names = []

    @property
    def is_running(self):
        return self.thread is not None and self.thread.is_alive()

    def get_avatar(self, avatar_name=""):
        runtime = self.avatars.get(avatar_name)
        if runtime is None:
            runtime = AvatarRuntime(avatar_name)
            self.avatars[avatar_name] = runtime
        return runtime

    def capture_armature_origin(self, armature, avatar_name=""):
        self.get_avatar(avatar_name).capture_armature_origin(armature)

    def start(self, host, port):
        # A previous run can still be winding down (the accept loop only wakes every
        # 0.5 s), and `is_running` reports True for it even though it is on its way
        # out. Without this join, pressing Start right after Stop was a silent no-op:
        # the old thread then exited and the receiver was dead while the panel still
        # said "Listening".
        thread = self.thread
        if thread is not None and thread.is_alive():
            if not self.stop_event.is_set():
                return
            thread.join(2.0)
            if thread.is_alive():
                self.status = "Previous receiver thread is still shutting down."
                return

        self.stop_event.clear()
        self.dropped_frames = 0
        self.superseded_count = 0
        # Names are only meaningful while a sender is connected. Keeping the last
        # run's list would let "Slots From Received Avatars" offer avatars that are
        # no longer in the Unity scene.
        self.known_avatar_names = []
        self.thread = threading.Thread(
            target=self._server_loop,
            args=(host, port),
            name="WorldPoseSyncReceiver",
            daemon=True,
        )
        self.thread.start()
        self.status = "Listening"

    def stop(self):
        self.stop_event.set()
        self._close_socket("client_socket")
        self._close_socket("server_socket")
        self.status = "Stopped"

    def set_latest(self, frame):
        with self.latest_lock:
            self.latest_frame = frame

    def take_latest(self):
        with self.latest_lock:
            frame = self.latest_frame
            self.latest_frame = None
            return frame

    def send_command(self, command, frames=1):
        """Send a Play Mode command to the connected Unity sender.

        Safe to call from the main thread while the receive thread reads: TCP is
        full duplex and the two directions do not share buffers.
        """
        sock = self.client_socket
        if sock is None:
            self.status = "No Unity connection"
            return False
        packet = bytes([PACKET_KIND_COMMAND]) + encode_command(command, frames)
        try:
            sock.sendall(struct.pack(">I", len(packet)) + packet)
            return True
        except Exception as exc:
            self.status = f"Command failed: {exc}"
            return False

    def push_control(self, frame):
        with self.latest_lock:
            # A queued pose must not be applied after the stopped reset. Clear it
            # under the same lock used by the consumer and control queue.
            if is_unity_stopped_message(frame):
                self.latest_frame = None
            self.pending_controls.append(frame)

    def take_controls(self):
        with self.latest_lock:
            controls = self.pending_controls
            self.pending_controls = []
            return controls

    def _server_loop(self, host, port):
        while not self.stop_event.is_set():
            try:
                server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                server.settimeout(0.5)
                server.bind((host, port))
                # Backlog > 1 so a newer sender is queued right away and
                # _client_loop can hand over to it instead of the stale connection
                # holding the only slot.
                server.listen(8)
                self.server_socket = server
                self.status = f"Listening {host}:{port}"

                while not self.stop_event.is_set():
                    try:
                        client, address = server.accept()
                    except socket.timeout:
                        continue
                    # _client_loop hands back the connection to switch to when a
                    # newer sender shows up, so servicing a client is a loop.
                    while client is not None and not self.stop_event.is_set():
                        self.client_socket = client
                        self.status = f"Connected {address[0]}:{address[1]}"
                        client, address = self._client_loop(server, client)
                        self._close_socket("client_socket")
                    if not self.stop_event.is_set():
                        self.status = f"Listening {host}:{port}"
            except OSError as exc:
                self.status = f"Socket error: {exc}"
                time.sleep(0.5)
            finally:
                self._close_socket("client_socket")
                self._close_socket("server_socket")

    def _client_loop(self, server, client):
        """Pump one client until it disconnects or a newer sender takes over.

        Returns the (socket, address) to switch to when a newer connection arrives,
        else (None, None). Handing over matters because the server services one
        client at a time: a stale connection - a sender thread that outlived Play
        Mode, or a second avatar's sender - would otherwise hold the slot forever
        and the live sender would never be read.
        """
        client.settimeout(0.5)
        keep_pose_on_disconnect = False
        saw_stopped = False
        superseded = False
        try:
            while not self.stop_event.is_set():
                try:
                    readable, _, _ = select.select([client, server], [], [], 0.1)
                except (OSError, ValueError) as exc:
                    self.status = f"Receive error: {exc}"
                    return None, None

                if server in readable:
                    try:
                        new_client, new_address = server.accept()
                    except (socket.timeout, BlockingIOError):
                        new_client = None
                    except OSError as exc:
                        self.status = f"Socket error: {exc}"
                        return None, None
                    if new_client is not None:
                        # A reconnect, not Unity stopping: hand over without
                        # resetting the rig.
                        superseded = True
                        self.superseded_count += 1
                        return new_client, new_address

                if client not in readable:
                    continue

                try:
                    payload, controls, dropped, closed = self._read_latest_payload(client)
                except Exception as exc:
                    self.status = f"Receive error: {exc}"
                    return None, None

                self.dropped_frames += dropped

                # Control messages first and in order: the handshake has to be applied
                # before the pose that follows it.
                for control in controls:
                    try:
                        frame = unpack_messagepack(control)
                    except Exception as exc:
                        self.status = f"Decode error: {exc}"
                        return None, None
                    if is_unity_stopped_message(frame):
                        saw_stopped = True
                    elif frame.get("message_type") == "avatar_list":
                        # Edit-mode announce from the manager window: a short-lived
                        # connection that only names the configured avatars. Its
                        # disconnect must not inject the stopped sentinel - that
                        # would reset the rig every time the user edits the list.
                        keep_pose_on_disconnect = True
                    self.push_control(frame)

                if payload is not None and not saw_stopped:
                    try:
                        frame = unpack_messagepack(payload)
                    except Exception as exc:
                        self.status = f"Decode error: {exc}"
                        return None, None
                    keep_pose_on_disconnect = frame.get("message_type") == "pose_snapshot"
                    # OR, not assignment: a pose that trails the stopped message must
                    # not re-arm the disconnect sentinel (that would reset the rig a
                    # second time for nothing).
                    saw_stopped = saw_stopped or is_unity_stopped_message(frame)
                    self.set_latest(frame)
                if closed:
                    return None, None
        finally:
            # Client disconnected (Unity stopped / exited Play Mode). Inject a
            # stopped sentinel so the rig resets even if the explicit "stopped"
            # message was dropped on an abrupt close. Skipped when we are the ones
            # stopping the receiver, when a newer sender took over, and when the
            # explicit message already arrived (resetting twice costs a full scene
            # re-evaluation for nothing).
            if (
                not self.stop_event.is_set()
                and not keep_pose_on_disconnect
                and not saw_stopped
                and not superseded
            ):
                self.push_control({"message_type": "state", "state": "stopped"})
        return None, None

    def _read_latest_payload(self, client):
        """Read framed payloads, keeping only the newest POSE plus every CONTROL.

        Pose payloads queued behind a newer one are discarded WITHOUT being decoded:
        only the newest frame is ever applied, so decoding a backlog is pure waste -
        and with the bundled MessagePack decoder (~4.5 ms for a 270 bone frame) a
        backlog is exactly what makes Blender feel like it froze.

        Control payloads (the avatar_defs handshake, state changes) must survive that
        culling: dropping the handshake leaves the receiver with no bone order, and
        every pose after it is then ignored. Hence the kind byte in the framing.

        Returns (pose_payload_or_None, control_payloads, dropped_count, closed).
        """
        payload = None
        controls = []
        dropped = 0
        while not self.stop_event.is_set():
            header = recv_exact(client, 4, self.stop_event)
            if header is None:
                return payload, controls, dropped, True
            payload_size = struct.unpack(">I", header)[0]
            if payload_size <= 0 or payload_size > 32 * 1024 * 1024:
                return payload, controls, dropped, True
            chunk = recv_exact(client, payload_size, self.stop_event)
            if chunk is None:
                return payload, controls, dropped, True

            # A MessagePack map always starts at 0x80 or above, so a leading 0 or 1
            # can only be the kind byte. That lets the legacy v2 component keep
            # working unchanged as well.
            kind = chunk[0]
            if kind == PACKET_KIND_CONTROL:
                controls.append(chunk[1:])
            elif kind == PACKET_KIND_POSE:
                if payload is not None:
                    dropped += 1
                payload = chunk[1:]
            else:
                if payload is not None:
                    dropped += 1
                payload = chunk

            try:
                ready, _, _ = select.select([client], [], [], 0.0)
            except (OSError, ValueError):
                return payload, controls, dropped, True
            if not ready:
                break
        return payload, controls, dropped, False

    def _close_socket(self, attr_name):
        sock = getattr(self, attr_name, None)
        setattr(self, attr_name, None)
        if sock is None:
            return
        try:
            sock.shutdown(socket.SHUT_RDWR)
        except Exception:
            pass
        try:
            sock.close()
        except Exception:
            pass


STATE = WorldSyncState()


def recv_exact(sock, size, stop_event, inactivity_timeout=2.0):
    chunks = bytearray()
    deadline = time.monotonic() + inactivity_timeout
    while len(chunks) < size and not stop_event.is_set():
        try:
            chunk = sock.recv(size - len(chunks))
        except socket.timeout:
            if time.monotonic() >= deadline:
                return None
            continue
        if not chunk:
            return None
        chunks.extend(chunk)
        deadline = time.monotonic() + inactivity_timeout
    if len(chunks) != size:
        return None
    return bytes(chunks)


# --------------------------------------------------------------------------
# Pose application (world-space, per-bone independent)
# --------------------------------------------------------------------------

def read_xyzw(value):
    if isinstance(value, dict):
        return (
            float(value.get("x", 0.0)),
            float(value.get("y", 0.0)),
            float(value.get("z", 0.0)),
            float(value.get("w", 1.0)),
        )
    if isinstance(value, (list, tuple)) and len(value) >= 4:
        return (float(value[0]), float(value[1]), float(value[2]), float(value[3]))
    return (0.0, 0.0, 0.0, 1.0)


def quat_matrix(value):
    x, y, z, w = read_xyzw(value)
    return Quaternion((w, x, y, z)).to_matrix()


def convert_world_quaternion(value):
    g = UNITY_TO_BLENDER_BASIS
    return (g @ quat_matrix(value) @ g.transposed()).to_quaternion()


def read_xyz(value):
    if isinstance(value, dict):
        return (
            float(value.get("x", 0.0)),
            float(value.get("y", 0.0)),
            float(value.get("z", 0.0)),
        )
    if isinstance(value, (list, tuple)) and len(value) >= 3:
        return (float(value[0]), float(value[1]), float(value[2]))
    return (0.0, 0.0, 0.0)


def convert_world_position(value, scale):
    x, y, z = read_xyz(value)
    # (x, y, z) -> (-x, -z, y), matching UNITY_TO_BLENDER_BASIS.
    return Vector((-x * scale, -z * scale, y * scale))


def camera_world_matrix_from_blob(data, scale):
    """Convert Unity camera pos(3)+quat(4) into a Blender camera world matrix."""
    position, rotation = transform_from_blob(data)
    if position is None or rotation is None:
        return None
    converted_rotation = (
        UNITY_TO_BLENDER_BASIS
        @ quat_matrix(rotation)
        @ BLENDER_CAMERA_TO_UNITY_LOCAL
    )
    matrix = converted_rotation.to_4x4()
    matrix.translation = convert_world_position(position, scale)
    return matrix


def camera_lens_from_vertical_fov(field_of_view, sensor_height=32.0):
    half_angle = max(0.01, min(179.0, float(field_of_view))) * 0.5
    return sensor_height / (2.0 * math.tan(math.radians(half_angle)))


def apply_camera_to_viewports(scene, target_camera):
    """Show a Unity Camera source through the selected Blender Camera."""
    scene.camera = target_camera
    for window in bpy.context.window_manager.windows:
        screen = window.screen
        if screen is None:
            continue
        for area in screen.areas:
            if area.type != "VIEW_3D":
                continue
            area.spaces.active.region_3d.view_perspective = "CAMERA"


def apply_scene_view_to_viewports(scene, camera_matrix, camera_state):
    """Apply a Unity Scene View to every open Blender 3D View directly."""
    pivot_data = unpack_float_blob(camera_state.get("view_pivot"))
    if pivot_data is None or len(pivot_data) < 3:
        return False

    scale = scene.pose_sync_world_position_scale
    pivot = convert_world_position(
        (pivot_data[0], pivot_data[1], pivot_data[2]), scale)
    orthographic = bool(camera_state.get("orthographic", False))
    distance_key = "view_size" if orthographic else "view_distance"
    view_distance = max(0.001, float(camera_state.get(distance_key, 10.0)) * scale)
    view_rotation = camera_matrix.to_quaternion()
    near_clip = max(0.0001, float(camera_state.get("near_clip", 0.01)) * scale)
    far_clip = max(near_clip + 0.001, float(camera_state.get("far_clip", 1000.0)) * scale)

    applied = False
    for window in bpy.context.window_manager.windows:
        screen = window.screen
        if screen is None:
            continue
        for area in screen.areas:
            if area.type != "VIEW_3D":
                continue
            space = area.spaces.active
            region_3d = space.region_3d
            region_3d.view_location = pivot
            region_3d.view_rotation = view_rotation
            region_3d.view_distance = view_distance
            region_3d.view_perspective = "ORTHO" if orthographic else "PERSP"
            space.clip_start = near_clip
            space.clip_end = far_clip

            if not orthographic:
                aspect = max(0.001, float(area.width) / max(1.0, float(area.height)))
                sensor_height = 32.0 / aspect
                space.lens = camera_lens_from_vertical_fov(
                    camera_state.get("field_of_view", 60.0), sensor_height)
            area.tag_redraw()
            applied = True
    return applied


def apply_camera_state(scene, camera_state):
    if not camera_state:
        return False
    transform_data = unpack_float_blob(camera_state.get("transform"))
    camera_matrix = camera_world_matrix_from_blob(
        transform_data, scene.pose_sync_world_position_scale)
    if camera_matrix is None:
        return False

    target = scene.pose_sync_world_camera
    if (scene.pose_sync_world_apply_camera_object
            and target is not None and target.type == "CAMERA"):
        target.matrix_world = camera_matrix
        camera_data = target.data
        camera_data.clip_start = max(0.0001, float(camera_state.get("near_clip", 0.01)))
        camera_data.clip_end = max(
            camera_data.clip_start + 0.001,
            float(camera_state.get("far_clip", 1000.0)),
        )
        if bool(camera_state.get("orthographic", False)):
            camera_data.type = "ORTHO"
            camera_data.ortho_scale = max(
                0.001, float(camera_state.get("orthographic_size", 5.0)) * 2.0)
        else:
            camera_data.type = "PERSP"
            camera_data.sensor_fit = "VERTICAL"
            camera_data.lens = camera_lens_from_vertical_fov(
                camera_state.get("field_of_view", 60.0), camera_data.sensor_height)

    if scene.pose_sync_world_apply_scene_view:
        if bool(camera_state.get("scene_view", False)):
            apply_scene_view_to_viewports(scene, camera_matrix, camera_state)
        elif target is not None and target.type == "CAMERA":
            apply_camera_to_viewports(scene, target)
    return True


def ensure_virtual_root(runtime, armature):
    """Create (once) an Empty parented above the armature, keeping the armature's
    world transform. The Empty becomes the driven virtual root."""
    empty = bpy.data.objects.get(runtime.vroot_name) if runtime.vroot_name else None
    if empty is not None:
        return empty

    suffix = f"_{runtime.avatar_name}" if runtime.avatar_name else ""
    empty = bpy.data.objects.new(f"PoseSync_VRoot{suffix}", None)
    empty.empty_display_type = "ARROWS"
    empty.empty_display_size = 0.25
    empty.rotation_mode = "QUATERNION"

    collection = armature.users_collection[0] if armature.users_collection else bpy.context.scene.collection
    collection.objects.link(empty)

    previous_world = armature.matrix_world.copy()
    runtime.vroot_prev_parent_name = armature.parent.name if armature.parent is not None else ""
    runtime.vroot_prev_parent_type = armature.parent_type
    runtime.vroot_prev_parent_bone = armature.parent_bone
    runtime.vroot_prev_matrix_parent_inverse = armature.matrix_parent_inverse.copy()
    armature.parent = empty
    armature.matrix_parent_inverse = empty.matrix_world.inverted()
    armature.matrix_world = previous_world

    runtime.vroot_name = empty.name
    runtime.vroot_child_name = armature.name
    return empty


def remove_virtual_root(runtime):
    """Unparent the armature (restoring its captured origin) and delete the Empty."""
    name = runtime.vroot_name
    child_name = runtime.vroot_child_name
    prev_parent_name = runtime.vroot_prev_parent_name
    prev_parent_type = runtime.vroot_prev_parent_type
    prev_parent_bone = runtime.vroot_prev_parent_bone
    prev_parent_inverse = runtime.vroot_prev_matrix_parent_inverse
    runtime.vroot_name = ""
    runtime.vroot_child_name = ""
    runtime.vroot_prev_parent_name = ""
    runtime.vroot_prev_parent_type = "OBJECT"
    runtime.vroot_prev_parent_bone = ""
    runtime.vroot_prev_matrix_parent_inverse = None
    if not name:
        return

    empty = bpy.data.objects.get(name)
    armature = bpy.data.objects.get(child_name)
    if armature is not None and armature.parent == empty:
        previous_parent = bpy.data.objects.get(prev_parent_name) if prev_parent_name else None
        armature.parent = previous_parent
        armature.parent_type = prev_parent_type
        armature.parent_bone = prev_parent_bone
        armature.matrix_parent_inverse = (
            prev_parent_inverse.copy() if prev_parent_inverse is not None else Matrix.Identity(4)
        )
        if (
            runtime.origin_armature_matrix_world is not None
            and runtime.origin_armature_name == armature.name
        ):
            armature.matrix_world = runtime.origin_armature_matrix_world.copy()

    if empty is not None:
        bpy.data.objects.remove(empty, do_unlink=True)


def remove_all_virtual_roots():
    for runtime in list(STATE.avatars.values()):
        remove_virtual_root(runtime)


def update_virtual_root(scene, runtime, armature, root_world, hips_world):
    # The slot was re-pointed at a different armature mid-run: the existing vroot
    # still parents (and would keep driving) the OLD armature. Tear it down so a
    # fresh one is built around the new target.
    if runtime.vroot_child_name and runtime.vroot_child_name != armature.name:
        remove_virtual_root(runtime)
        runtime.capture_armature_origin(armature)
    empty = ensure_virtual_root(runtime, armature)
    if empty is None:
        return
    scale = scene.pose_sync_world_position_scale
    empty.rotation_mode = "QUATERNION"
    if scene.pose_sync_world_apply_root_location:
        # Location follows the Hips world position (falls back to the avatar
        # root). The Hips moves with both root motion and in-place animation, so
        # the rig translates with the character.
        position = hips_world.get("position") if hips_world else None
        if position is None:
            position = (root_world or {}).get("position")
        empty.location = convert_world_position(position, scale)
    if scene.pose_sync_world_apply_root_rotation:
        # Rotation from the avatar root only (Hips rotation is already in the
        # bone pose, so using it here would double-apply).
        empty.rotation_quaternion = convert_world_quaternion((root_world or {}).get("rotation"))


# Bone topology (name resolution, ancestor chain, rest matrices) is expensive to
# rebuild - it touches every one of the ~270 incoming bones - and does not change
# while a sender streams, so it is cached and keyed on the incoming bone names.
# Cleared on Start, so edits to the armature's rest pose are picked up by a
# Stop -> Start cycle.
_topology_cache = {}


def clear_topology_cache():
    """Drop cached topology and v3 definitions so the next handshake rebuilds them."""
    _topology_cache.clear()
    for runtime in STATE.avatars.values():
        runtime.clear_definitions()


def build_bone_topology(armature, targets, human_bones, bone_prefix, runtime=None):
    """Resolve incoming bones to pose bones and derive the parent-first apply order.

    `targets` / `human_bones` are parallel lists (human_bones may be None). Returns
    (resolved_names, order, parent, blender_rest, rest_rel, rest_rel_inv) where
    `resolved_names` is parallel to `targets` and holds None for unmatched bones.

    Cached on the avatar runtime: for schema v3 this runs once per handshake
    instead of once per frame.
    """
    key = (armature.name, bone_prefix, tuple(targets))
    if runtime is not None and runtime.topology_key == key:
        return runtime.topology

    resolved_names = []
    seen = set()
    for i, target in enumerate(targets):
        human = human_bones[i] if human_bones is not None and i < len(human_bones) else None
        pose_bone = resolve_pose_bone_by_names(armature, target, human, bone_prefix)
        if pose_bone is None or pose_bone.name in seen:
            resolved_names.append(None)
            continue
        seen.add(pose_bone.name)
        resolved_names.append(pose_bone.name)

    rest_bones = armature.data.bones

    def matched_parent(name):
        bone = rest_bones[name].parent
        while bone is not None:
            if bone.name in seen:
                return bone.name
            bone = bone.parent
        return None

    parent = {name: matched_parent(name) for name in seen}

    order = []
    placed = set()

    def visit(name):
        if name in placed:
            return
        ancestor = parent[name]
        if ancestor is not None:
            visit(ancestor)
        placed.add(name)
        order.append(name)

    for name in seen:
        visit(name)

    blender_rest = {name: rest_bones[name].matrix_local.to_3x3() for name in seen}

    # Index of each driven bone inside armature.pose.bones. The pose is written with
    # foreach_set, which addresses the WHOLE collection in one C-level call, so we
    # need positions rather than names. Setting rotation_mode here (once) matters
    # too: foreach_set only fills rotation_quaternion, and a bone left on Euler
    # would silently ignore it.
    pose_bones = armature.pose.bones
    bone_index = {}
    for index, pose_bone in enumerate(pose_bones):
        if pose_bone.name in seen:
            bone_index[pose_bone.name] = index
            if pose_bone.rotation_mode != "QUATERNION":
                pose_bone.rotation_mode = "QUATERNION"

    # Rest-relative matrices depend only on the armature, so pre-invert them here
    # instead of doing two 3x3 inversions per bone per frame.
    identity = Matrix.Identity(3)
    rest_rel = {}
    rest_rel_inv = {}
    for name in order:
        ancestor = parent[name]
        parent_rest = blender_rest[ancestor] if ancestor is not None else identity
        rel = parent_rest.inverted() @ blender_rest[name]
        rest_rel[name] = rel
        rest_rel_inv[name] = rel.inverted()

    value = (resolved_names, order, parent, blender_rest, rest_rel, rest_rel_inv,
             bone_index, len(pose_bones))
    if runtime is not None:
        runtime.topology_key = key
        runtime.topology = value
    return value


def unpack_float_blob(value):
    """Decode a schema v3 float32 blob (little-endian) into an array('f')."""
    if value is None:
        return None
    data = array.array("f")
    if isinstance(value, (bytes, bytearray, memoryview)):
        data.frombytes(bytes(value))
    elif isinstance(value, (list, tuple)):
        data.fromlist([float(v) for v in value])
    else:
        return None
    if sys.byteorder != "little":
        data.byteswap()
    return data


def quats_from_blob(data):
    """array('f') of x,y,z,w * N -> list of (x, y, z, w) tuples."""
    if data is None:
        return []
    return [(data[i], data[i + 1], data[i + 2], data[i + 3]) for i in range(0, len(data) - 3, 4)]


def transform_from_blob(data):
    """array('f') of pos(3) + quat(4) -> ({"x","y","z"}, {"x","y","z","w"})."""
    if data is None or len(data) < 7:
        return None, None
    return (
        {"x": data[0], "y": data[1], "z": data[2]},
        {"x": data[3], "y": data[4], "z": data[5], "w": data[6]},
    )


def apply_avatar_pose(
    scene,
    runtime,
    armature,
    targets,
    human_bones,
    rest_inv,
    current,
    root_world,
    hips_world,
):
    """Drive one armature from root-space world rotations.

    `rest_inv` holds each bone's INVERTED Unity rest rotation as a 3x3 matrix, so
    the per-frame work is one multiply instead of an inversion. `current` is the
    parallel list of (x, y, z, w) live rotations.
    """
    if armature is None or armature.type != "ARMATURE":
        return 0
    armature.data.pose_position = "POSE"
    if (
        runtime.origin_armature_matrix_world is None
        or runtime.origin_armature_name != armature.name
    ):
        runtime.capture_armature_origin(armature)

    bone_prefix = scene.pose_sync_world_bone_prefix

    # Root transform: bones are sent in root space (root divided out). To place /
    # orient the whole rig we drive a virtual-root Empty parented above the
    # armature, instead of overwriting the armature object's own transform.
    # G-conjugation is a homomorphism, so final world = G(root) * G(bone-relative)
    # = G(root * bone-relative) matches Unity faithfully.
    if scene.pose_sync_world_apply_root_location or scene.pose_sync_world_apply_root_rotation:
        update_virtual_root(scene, runtime, armature, root_world, hips_world)
    else:
        remove_virtual_root(runtime)

    g = UNITY_TO_BLENDER_BASIS
    gt = g.transposed()

    (resolved_names, order, parent, blender_rest, rest_rel, rest_rel_inv,
     bone_index, pose_bone_count) = build_bone_topology(
        armature, targets, human_bones, bone_prefix, runtime
    )

    # PREREQUISITE: the armature object must have its rotation and scale applied
    # (Object > Apply > Rotation & Scale). The delta from Unity is a world-space
    # rotation while blender_rest and the pose we write are in armature space, and
    # those only coincide on an unrotated, unscaled object. An FBX left as imported
    # sits at X+90 deg with scale 0.01, and the rig then comes out scrambled.
    target = {}
    # Bound by rest_inv too: a frame paired with definitions from a different Bind
    # (possible for one frame right after Unity rebinds) must clamp, not IndexError.
    count = min(len(resolved_names), len(current), len(rest_inv))
    for i in range(count):
        name = resolved_names[i]
        if name is None:
            continue
        cur_u = quat_matrix(current[i])
        target[name] = (g @ (cur_u @ rest_inv[i]) @ gt) @ blender_rest[name]

    if not target:
        return 0

    # Convert target world orientation to local matrix_basis (parents first).
    #
    # The results go into one flat buffer and land via foreach_set. Writing
    # rotation_quaternion bone by bone was ~72% of the cost of applying a frame
    # (0.738 ms of 1.03 ms for 270 bones); one C-level call does the same work in
    # ~0.017 ms. foreach_set addresses the whole collection, so the buffer is
    # primed with foreach_get and bones we do not drive keep their current value.
    pose_bones = armature.pose.bones
    buffer = runtime.pose_buffer
    if buffer is None or len(buffer) != pose_bone_count * 4:
        buffer = array.array("f", [0.0]) * (pose_bone_count * 4)
        runtime.pose_buffer = buffer
    pose_bones.foreach_get("rotation_quaternion", buffer)

    identity = Matrix.Identity(3)
    pose_world = {}
    applied = 0
    for name in order:
        world_target = target.get(name)
        if world_target is None:
            continue
        ancestor = parent[name]
        parent_pose = pose_world[ancestor] if ancestor is not None else identity
        basis = rest_rel_inv[name] @ parent_pose.inverted() @ world_target
        pose_world[name] = parent_pose @ rest_rel[name] @ basis

        q = basis.to_quaternion()
        offset = bone_index[name] * 4
        buffer[offset] = q.w
        buffer[offset + 1] = q.x
        buffer[offset + 2] = q.y
        buffer[offset + 3] = q.z
        applied += 1

    pose_bones.foreach_set("rotation_quaternion", buffer)
    return applied


def apply_world_frame(scene, frame):
    """Schema v2 entry point: one avatar, bones carried inline in the frame."""
    runtime = STATE.get_avatar("")
    armature = resolve_armature_for_avatar(scene, frame.get("avatar_name") or "")
    if armature is None:
        return 0

    frame_bones = frame.get("bones", [])
    if not frame_bones:
        return 0

    targets = [(bf.get("target") or bf.get("name")) for bf in frame_bones]
    human_bones = [bf.get("human_bone") for bf in frame_bones]
    # v2 sends the rest rotation on every bone every frame; invert once per frame.
    rest_inv = [quat_matrix(bf.get("rest_rotation")).inverted() for bf in frame_bones]
    current = [read_xyzw(bf.get("rotation")) for bf in frame_bones]

    return apply_avatar_pose(
        scene,
        runtime,
        armature,
        targets,
        human_bones,
        rest_inv,
        current,
        frame.get("root_world") or {},
        frame.get("hips_world") or {},
    )


def resolve_armature_for_avatar(scene, avatar_name):
    """Map an incoming avatar name to the armature the user assigned to it.

    Falls back to the single-armature field when no slots are configured, so the
    original one-avatar workflow keeps working untouched.
    """
    slots = scene.pose_sync_world_avatars
    if len(slots) == 0:
        return scene.pose_sync_world_armature

    fallback = None
    for slot in slots:
        if not slot.enabled or slot.armature is None:
            continue
        if slot.avatar_name == avatar_name:
            return slot.armature
        if not slot.avatar_name and fallback is None:
            # A slot with an empty name accepts whichever avatar is unclaimed.
            fallback = slot.armature
    return fallback


def apply_avatar_defs(scene, message):
    """Schema v3 handshake: cache bone names and rest rotations per avatar.

    Inverting the rest rotations here means the per-frame path is a single matrix
    multiply per bone; v2 had to invert on every bone of every frame.
    """
    names = []
    for entry in message.get("avatars", []) or []:
        avatar_name = entry.get("avatar_name") or ""
        names.append(avatar_name)
        runtime = STATE.get_avatar(avatar_name)
        runtime.targets = list(entry.get("targets") or [])
        runtime.human_bones = list(entry.get("human_bones") or [])
        rest_quats = quats_from_blob(unpack_float_blob(entry.get("rest_rotations")))
        runtime.rest_matrices = [quat_matrix(q).inverted() for q in rest_quats]
        # Bone set may have changed; force the topology to rebuild.
        runtime.topology = None
        runtime.topology_key = None

    # Drop avatars the sender no longer announces (a garment that was unequipped, or
    # an entry removed in the manager window). Their runtimes would otherwise linger
    # for the rest of the session and keep offering stale names to the slot picker -
    # and their virtual roots would stay in the scene.
    for stale in [n for n in STATE.avatars if n and n not in names]:
        remove_virtual_root(STATE.avatars.pop(stale))

    STATE.known_avatar_names = names
    return len(names)


def apply_world_frame_v3(scene, frame):
    """Schema v3 entry point: several avatars, rotations as float32 blobs."""
    apply_camera_state(scene, frame.get("camera"))
    applied = 0
    for entry in frame.get("avatars", []) or []:
        avatar_name = entry.get("avatar_name") or ""
        runtime = STATE.get_avatar(avatar_name)
        if not runtime.targets or runtime.rest_matrices is None:
            # Pose arrived before (or without) its definitions; wait for the
            # handshake rather than guess the bone order.
            continue
        armature = resolve_armature_for_avatar(scene, avatar_name)
        if armature is None:
            continue

        current = quats_from_blob(unpack_float_blob(entry.get("rotations")))
        root_pos, root_rot = transform_from_blob(unpack_float_blob(entry.get("root")))
        hips_pos, hips_rot = transform_from_blob(unpack_float_blob(entry.get("hips")))

        applied += apply_avatar_pose(
            scene,
            runtime,
            armature,
            runtime.targets,
            runtime.human_bones,
            runtime.rest_matrices,
            current,
            {"position": root_pos, "rotation": root_rot},
            {"position": hips_pos, "rotation": hips_rot},
        )
    return applied


def is_unity_stopped_message(frame):
    return (
        isinstance(frame, dict)
        and frame.get("message_type") == "state"
        and frame.get("state") == "stopped"
    )


def collect_target_armatures(scene):
    """Every armature the receiver may drive: assigned slots plus the single field."""
    armatures = []
    seen = set()
    for slot in scene.pose_sync_world_avatars:
        armature = slot.armature
        if armature is not None and armature.name not in seen:
            seen.add(armature.name)
            armatures.append(armature)
    single = scene.pose_sync_world_armature
    if single is not None and single.name not in seen:
        armatures.append(single)
    return armatures


def reset_armature_on_unity_stopped(scene):
    if not scene.pose_sync_world_reset_on_unity_stopped:
        # When freezing the last frame, keep the virtual roots in place too.
        return

    # Remove the virtual roots first (this also restores the captured origins).
    remove_all_virtual_roots()

    touched = False
    for armature in collect_target_armatures(scene):
        if armature.type != "ARMATURE":
            continue
        for pose_bone in armature.pose.bones:
            pose_bone.matrix_basis = Matrix.Identity(4)
        for runtime in STATE.avatars.values():
            if (
                runtime.origin_armature_matrix_world is not None
                and runtime.origin_armature_name == armature.name
            ):
                armature.matrix_world = runtime.origin_armature_matrix_world.copy()
                break
        touched = True

    if touched:
        # One scene re-evaluation covers every armature we just reset.
        scene.frame_set(scene.frame_current)


def tag_redraw_view3d():
    """Repaint the sidebar so slot-picker changes show without a mouse-over."""
    wm = bpy.context.window_manager
    if wm is None:
        return
    for window in wm.windows:
        for area in window.screen.areas:
            if area.type == "VIEW_3D":
                area.tag_redraw()


def register_timer():
    if bpy.app.timers.is_registered(pose_sync_world_timer):
        STATE.timer_registered = True
        return
    bpy.app.timers.register(pose_sync_world_timer, first_interval=0.02, persistent=True)
    STATE.timer_registered = True


def unregister_timer():
    # Keep the flag in step with the real timer state. Letting the callback clear
    # it on its own leaves a window where the flag says "registered" but the timer
    # is already gone (or the other way round), and Start then silently does nothing.
    STATE.timer_registered = False
    if not bpy.app.timers.is_registered(pose_sync_world_timer):
        return
    try:
        bpy.app.timers.unregister(pose_sync_world_timer)
    except ValueError:
        pass


def pose_sync_world_timer():
    if not STATE.is_running:
        STATE.timer_registered = False
        return None
    scene = bpy.context.scene

    # bpy removes a timer whose callback raises, which would kill the sync silently
    # while the panel still says Connected. A bad frame (malformed packet, a bone
    # deleted mid-run) must surface in the status line instead, and the next frame
    # gets a fresh chance.
    try:
        # Control messages first, in arrival order: the handshake must be applied
        # before the pose that follows it.
        for control in STATE.take_controls():
            if is_unity_stopped_message(control):
                reset_armature_on_unity_stopped(scene)
                STATE.status = "Unity stopped"
            elif control.get("message_type") == "avatar_defs":
                count = apply_avatar_defs(scene, control)
                STATE.status = f"Definitions received ({count} avatar(s))"
                tag_redraw_view3d()
            elif control.get("message_type") == "avatar_list":
                # Edit-mode announce: names only, straight from the manager window.
                # Deliberately does NOT touch the runtimes - definitions and virtual
                # roots belong to a real Play Mode handshake.
                names = [str(n) for n in (control.get("avatars") or []) if n]
                STATE.known_avatar_names = names
                STATE.status = f"Avatar list: {len(names)} avatar(s) (from editor)"
                tag_redraw_view3d()

        frame = STATE.take_latest()
        if frame is not None:
            message_type = frame.get("message_type")
            if is_unity_stopped_message(frame):
                reset_armature_on_unity_stopped(scene)
                STATE.status = "Unity stopped"
            elif message_type == "avatar_defs":
                count = apply_avatar_defs(scene, frame)
                STATE.status = f"Definitions received ({count} avatar(s))"
            elif frame.get("avatars") is not None:
                # Schema v3: several avatars, rotations as float32 blobs.
                apply_world_frame_v3(scene, frame)
                if message_type == "pose_snapshot":
                    STATE.status = "Pose snapshot applied"
            else:
                # Schema v2: single avatar with bones inline.
                apply_world_frame(scene, frame)
                if message_type == "pose_snapshot":
                    STATE.status = "Pose snapshot applied"
    except Exception as exc:
        STATE.status = f"Apply error: {exc}"
    return max(0.001, scene.pose_sync_world_timer_interval)


# --------------------------------------------------------------------------
# Operators / Panel
# --------------------------------------------------------------------------

class POSESYNC_OT_world_start(bpy.types.Operator):
    bl_idname = "pose_sync.world_start"
    bl_label = "Start Pose Receiver (World)"

    def execute(self, context):
        scene = context.scene
        # Rest matrices are cached per armature; drop them so rest-pose edits made
        # since the last run are picked up.
        clear_topology_cache()
        STATE.capture_armature_origin(scene.pose_sync_world_armature)
        STATE.start(scene.pose_sync_world_host, scene.pose_sync_world_port)
        if not STATE.is_running:
            self.report({"WARNING"}, STATE.status)
            return {"CANCELLED"}
        register_timer()
        return {"FINISHED"}


class POSESYNC_OT_world_stop(bpy.types.Operator):
    bl_idname = "pose_sync.world_stop"
    bl_label = "Stop Pose Receiver (World)"

    def execute(self, context):
        STATE.stop()
        unregister_timer()
        # Reset bones to rest (if enabled) and always clean up the virtual roots.
        reset_armature_on_unity_stopped(context.scene)
        remove_all_virtual_roots()
        return {"FINISHED"}


class POSESYNC_OT_unity_command(bpy.types.Operator):
    bl_idname = "pose_sync.unity_command"
    bl_label = "Unity Command"
    bl_description = "Send a Play Mode command to the connected Unity sender"

    command: bpy.props.StringProperty(default="pause")
    frames: bpy.props.IntProperty(default=1, min=1)

    @classmethod
    def poll(cls, context):
        # Only meaningful while a sender is actually connected.
        return STATE.client_socket is not None

    def execute(self, context):
        if not STATE.send_command(self.command, self.frames):
            self.report({"WARNING"}, STATE.status)
            return {"CANCELLED"}
        return {"FINISHED"}


class POSESYNC_OT_avatar_slot_add(bpy.types.Operator):
    bl_idname = "pose_sync.avatar_slot_add"
    bl_label = "Add Avatar Slot"
    bl_description = "Add a slot mapping a Unity avatar name to a Blender armature"

    def execute(self, context):
        scene = context.scene
        slot = scene.pose_sync_world_avatars.add()
        # Prefill with the first announced avatar that has no slot yet.
        assigned = {s.avatar_name for s in scene.pose_sync_world_avatars}
        for name in STATE.known_avatar_names:
            if name not in assigned:
                slot.avatar_name = name
                break
        scene.pose_sync_world_avatar_index = len(scene.pose_sync_world_avatars) - 1
        return {"FINISHED"}


class POSESYNC_OT_avatar_slot_remove(bpy.types.Operator):
    bl_idname = "pose_sync.avatar_slot_remove"
    bl_label = "Remove Avatar Slot"
    bl_description = "Remove the selected avatar slot"

    @classmethod
    def poll(cls, context):
        return len(context.scene.pose_sync_world_avatars) > 0

    def execute(self, context):
        scene = context.scene
        index = scene.pose_sync_world_avatar_index
        if 0 <= index < len(scene.pose_sync_world_avatars):
            scene.pose_sync_world_avatars.remove(index)
            scene.pose_sync_world_avatar_index = max(0, index - 1)
        return {"FINISHED"}


class POSESYNC_OT_avatar_slots_from_sender(bpy.types.Operator):
    bl_idname = "pose_sync.avatar_slots_from_sender"
    bl_label = "Slots From Received Avatars"
    bl_description = "Create a slot for every avatar announced by Unity"

    @classmethod
    def poll(cls, context):
        return bool(STATE.known_avatar_names)

    def execute(self, context):
        scene = context.scene
        existing = {s.avatar_name for s in scene.pose_sync_world_avatars}
        added = 0
        for name in STATE.known_avatar_names:
            if name in existing:
                continue
            slot = scene.pose_sync_world_avatars.add()
            slot.avatar_name = name
            added += 1
        self.report({"INFO"}, f"Added {added} slot(s)")
        return {"FINISHED"}


class POSESYNC_UL_avatar_slots(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_property, index):
        row = layout.row(align=True)
        row.prop(item, "enabled", text="")
        # Mark names Unity has actually announced, so typos are obvious.
        known = item.avatar_name in STATE.known_avatar_names
        row.prop(item, "avatar_name", text="", emboss=False,
                 icon="CHECKMARK" if known else "DOT")
        row.prop(item, "armature", text="")


class POSESYNC_PT_world_panel(bpy.types.Panel):
    bl_label = "Unity Pose Sync (World)"
    bl_idname = "POSESYNC_PT_world_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Pose Sync"

    def draw(self, context):
        layout = self.layout
        scene = context.scene

        box = layout.box()
        box.label(text="Avatars", icon="OUTLINER_OB_ARMATURE")
        row = box.row()
        row.template_list(
            "POSESYNC_UL_avatar_slots", "",
            scene, "pose_sync_world_avatars",
            scene, "pose_sync_world_avatar_index",
            rows=3,
        )
        col = row.column(align=True)
        col.operator("pose_sync.avatar_slot_add", icon="ADD", text="")
        col.operator("pose_sync.avatar_slot_remove", icon="REMOVE", text="")
        box.operator("pose_sync.avatar_slots_from_sender", icon="IMPORT")
        if STATE.known_avatar_names:
            assigned = {s.avatar_name for s in scene.pose_sync_world_avatars if s.armature is not None}
            missing = [n for n in STATE.known_avatar_names if n not in assigned]
            if missing:
                box.label(text=f"Unassigned: {', '.join(missing)}", icon="ERROR")
        if len(scene.pose_sync_world_avatars) == 0:
            box.label(text="No slots: using the single Armature below.", icon="INFO")

        layout.prop(scene, "pose_sync_world_armature")
        layout.prop(scene, "pose_sync_world_host")
        layout.prop(scene, "pose_sync_world_port")
        layout.prop(scene, "pose_sync_world_bone_prefix")
        layout.prop(scene, "pose_sync_world_apply_root_location")
        layout.prop(scene, "pose_sync_world_apply_root_rotation")
        layout.prop(scene, "pose_sync_world_position_scale")
        layout.prop(scene, "pose_sync_world_reset_on_unity_stopped")
        layout.prop(scene, "pose_sync_world_timer_interval")

        camera_box = layout.box()
        camera_box.label(text="Camera Sync", icon="CAMERA_DATA")
        camera_box.prop(scene, "pose_sync_world_camera")
        camera_box.prop(scene, "pose_sync_world_apply_camera_object")
        camera_box.prop(scene, "pose_sync_world_apply_scene_view")
        if (scene.pose_sync_world_camera is None
                and scene.pose_sync_world_apply_camera_object):
            camera_box.label(text="Target Cameraを指定してください", icon="ERROR")

        row = layout.row(align=True)
        row.operator("pose_sync.world_start", icon="PLAY")
        row.operator("pose_sync.world_stop", icon="PAUSE")

        # Unity Play Mode control. Rides the existing pose connection, so it is only
        # available while a sender is connected - starting Play has to happen in
        # Unity itself, since nothing is connected before that.
        box = layout.box()
        box.label(text="Unity Play Mode", icon="SEQUENCE")
        connected = STATE.client_socket is not None
        control_row = box.row(align=True)
        control_row.enabled = connected

        op = control_row.operator("pose_sync.unity_command", text="Pause", icon="PAUSE")
        op.command = "pause"
        op = control_row.operator("pose_sync.unity_command", text="Resume", icon="PLAY")
        op.command = "resume"
        op = control_row.operator("pose_sync.unity_command", text="Step", icon="FRAME_NEXT")
        op.command = "step"
        op.frames = 1
        op = control_row.operator("pose_sync.unity_command", text="Stop", icon="SNAP_FACE")
        op.command = "stop"
        if not connected:
            box.label(text="Unity 未接続（Play は Unity 側で開始）", icon="INFO")

        layout.label(text=f"Status: {STATE.status}")
        if STATE.dropped_frames or STATE.superseded_count:
            layout.label(
                text=f"Skipped frames: {STATE.dropped_frames} / takeovers: {STATE.superseded_count}",
                icon="INFO",
            )


def poll_armature(self, obj):
    return obj is not None and obj.type == "ARMATURE"


def poll_camera(self, obj):
    return obj is not None and obj.type == "CAMERA"


class POSESYNC_avatar_slot(bpy.types.PropertyGroup):
    """One Unity avatar name mapped to one Blender armature (schema v3)."""

    avatar_name: bpy.props.StringProperty(
        name="Avatar",
        description="Avatar name as sent by Unity (matches the sender's Avatar Name Override, "
                    "or the GameObject name when that is empty)",
        default="",
    )
    armature: bpy.props.PointerProperty(
        name="Armature",
        type=bpy.types.Object,
        poll=poll_armature,
    )
    enabled: bpy.props.BoolProperty(
        name="Enabled",
        description="Drive this armature. Turn off to keep receiving without posing it",
        default=True,
    )


# POSESYNC_avatar_slot must be registered before the CollectionProperty using it.
CLASSES = (
    POSESYNC_avatar_slot,
    POSESYNC_OT_world_start,
    POSESYNC_OT_world_stop,
    POSESYNC_OT_unity_command,
    POSESYNC_OT_avatar_slot_add,
    POSESYNC_OT_avatar_slot_remove,
    POSESYNC_OT_avatar_slots_from_sender,
    POSESYNC_UL_avatar_slots,
    POSESYNC_PT_world_panel,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)

    bpy.types.Scene.pose_sync_world_avatars = bpy.props.CollectionProperty(
        type=POSESYNC_avatar_slot,
    )
    bpy.types.Scene.pose_sync_world_avatar_index = bpy.props.IntProperty(
        name="Active Avatar Slot",
        default=0,
        min=0,
    )
    bpy.types.Scene.pose_sync_world_armature = bpy.props.PointerProperty(
        name="Armature",
        type=bpy.types.Object,
        poll=poll_armature,
        description="Used when no avatar slots are configured (single-avatar mode)",
    )
    bpy.types.Scene.pose_sync_world_host = bpy.props.StringProperty(
        name="Host",
        default="127.0.0.1",
    )
    bpy.types.Scene.pose_sync_world_port = bpy.props.IntProperty(
        name="Port",
        default=39541,
        min=1,
        max=65535,
    )
    bpy.types.Scene.pose_sync_world_bone_prefix = bpy.props.StringProperty(
        name="Bone Prefix",
        default="",
    )
    bpy.types.Scene.pose_sync_world_apply_root_location = bpy.props.BoolProperty(
        name="Apply Root Location",
        default=False,
        description="Move the rig (via the virtual-root Empty) by the Unity Hips world position. Captures both root motion and in-place motion.",
    )
    bpy.types.Scene.pose_sync_world_apply_root_rotation = bpy.props.BoolProperty(
        name="Apply Root Rotation",
        default=False,
        description="Rotate the rig by the Unity avatar-root world rotation.",
    )
    bpy.types.Scene.pose_sync_world_position_scale = bpy.props.FloatProperty(
        name="Position Scale",
        default=1.0,
    )
    bpy.types.Scene.pose_sync_world_reset_on_unity_stopped = bpy.props.BoolProperty(
        name="Reset On Unity Stopped",
        default=True,
        description="Reset pose bones to rest pose and restore the armature transform captured when the receiver started.",
    )
    bpy.types.Scene.pose_sync_world_timer_interval = bpy.props.FloatProperty(
        name="Timer Interval",
        default=0.016,
        min=0.001,
        max=1.0,
    )
    bpy.types.Scene.pose_sync_world_camera = bpy.props.PointerProperty(
        name="Target Camera",
        type=bpy.types.Object,
        poll=poll_camera,
        description="Optional Blender camera object driven by the Unity camera",
    )
    bpy.types.Scene.pose_sync_world_apply_camera_object = bpy.props.BoolProperty(
        name="Apply to Camera Object",
        default=True,
        description="Apply the received transform and lens to Target Camera",
    )
    bpy.types.Scene.pose_sync_world_apply_scene_view = bpy.props.BoolProperty(
        name="Apply to 3D View",
        default=False,
        description="Align open Blender 3D Views to the received Unity camera or Scene View",
    )


def unregister():
    STATE.stop()
    unregister_timer()
    clear_topology_cache()
    remove_all_virtual_roots()
    for attr_name in (
        "pose_sync_world_avatars",
        "pose_sync_world_avatar_index",
        "pose_sync_world_armature",
        "pose_sync_world_host",
        "pose_sync_world_port",
        "pose_sync_world_bone_prefix",
        "pose_sync_world_apply_root_location",
        "pose_sync_world_apply_root_rotation",
        "pose_sync_world_position_scale",
        "pose_sync_world_reset_on_unity_stopped",
        "pose_sync_world_timer_interval",
        "pose_sync_world_camera",
        "pose_sync_world_apply_camera_object",
        "pose_sync_world_apply_scene_view",
    ):
        if hasattr(bpy.types.Scene, attr_name):
            delattr(bpy.types.Scene, attr_name)
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
