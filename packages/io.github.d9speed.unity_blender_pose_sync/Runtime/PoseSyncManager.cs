using MessagePack;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace UnityBlenderPoseSync.World
{
    /// <summary>
    /// Streams several avatars to Blender over ONE connection (schema v3).
    ///
    /// Avatars are named directly by their Animator - nothing has to be attached to
    /// the avatars themselves. In the normal workflow this component is not authored
    /// into the scene at all: the Pose Sync Manager window (D9speed/Animation) spawns
    /// a throwaway GameObject carrying it when Play Mode starts.
    ///
    /// One connection matters because the receiver services a single client at a
    /// time; a sender per avatar left the extras unread in the accept queue, filling
    /// their send buffer until they timed out and reconnected in a loop, and every
    /// one of those disconnects cost the receiver a full rig reset.
    ///
    /// The wire format carries only what changes. Bone names and rest rotations go
    /// out once in an "avatar_defs" handshake; each frame then holds float32 blobs of
    /// live rotations - roughly 16 KB per 1000 bones instead of 149 KB, decoded on
    /// the Blender side with array.array rather than a map walk per bone.
    /// </summary>
    [DisallowMultipleComponent]
    // Run after animation and after VRCPhysBone's own LateUpdate, so what we sample
    // is the final pose for the frame.
    [DefaultExecutionOrder(10000)]
    public sealed class PoseSyncManager : MonoBehaviour
    {
        private const string SchemaName = "unity_blender_pose_sync.world";
        private const int SchemaVersion = 3;

        /// <summary>
        /// A hierarchy the user picked by hand, sent in addition to whatever the
        /// automatic collection finds.
        /// </summary>
        [Serializable]
        public sealed class ExtraRoot
        {
            public Transform transform;

            [Tooltip("Send every descendant as well (the whole chain), not just this bone.")]
            public bool include_descendants = true;
        }

        [Serializable]
        public sealed class AvatarEntry
        {
            [Tooltip("Avatar to stream. Humanoid bones are read from this Animator. " +
                     "Leave empty for a clothing entry and set root_override instead.")]
            public Animator animator;

            [Tooltip("Stream this avatar. Turn off to keep it configured but silent.")]
            public bool send = true;

            [Tooltip("Name matched against the Blender panel's avatar slot. " +
                     "Empty means the GameObject name.")]
            public string avatar_name = "";

            [Tooltip("Also send every VRCPhysBone chain on this avatar " +
                     "(skirt / hair / tail / breast bones).")]
            public bool include_all_phys_bones = true;

            [Tooltip("Include the Humanoid bones from the Animator.")]
            public bool include_humanoid = true;

            [Tooltip("Skip transforms listed in each PhysBone's Ignore Transforms.")]
            public bool phys_bones_respect_ignore = true;

            [Tooltip("Send bone names with everything from '$' onwards removed. " +
                     "Modular Avatar's Merge Armature renames the objects it keeps to " +
                     "\"<original>$<guid>\", which would otherwise never match the Blender bone.")]
            public bool strip_mangled_names = true;

            [Tooltip("Extra hierarchy roots to send on top of the automatic collection.")]
            public ExtraRoot[] extra_roots = Array.Empty<ExtraRoot>();

            [Tooltip("Space the rotations are expressed relative to. " +
                     "Defaults to the Animator's Transform.")]
            public Transform root_override;

            /// <summary>Space the rotations are expressed in. Clothing entries share the
            /// avatar's root, since Merge Armature puts their bones in that space.</summary>
            public Transform ResolveRoot()
            {
                if (root_override != null)
                {
                    return root_override;
                }
                return animator != null ? animator.transform : null;
            }

            /// <summary>Where bones are searched from. Clothing entries scan the avatar
            /// they were merged into, because MA moved their bones there.</summary>
            public Transform ResolveSearchRoot()
            {
                if (animator != null)
                {
                    return animator.transform;
                }
                return root_override;
            }

            public string ResolveName()
            {
                if (!string.IsNullOrWhiteSpace(avatar_name))
                {
                    return avatar_name;
                }
                var root = ResolveSearchRoot();
                return root != null ? root.gameObject.name : "";
            }
        }

        [Header("Network")]
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 39541;
        [SerializeField] private bool connect_on_enable = true;
        [SerializeField, Min(1)] private int send_interval_frames = 1;

        [Header("Avatars")]
        [SerializeField] private List<AvatarEntry> avatars = new List<AvatarEntry>();

        [Header("Camera")]
        [SerializeField] private Camera camera_source;
        private string camera_name_override = "";
        private bool camera_is_scene_view;
        private Vector3 scene_view_pivot;
        private float scene_view_distance;
        private float scene_view_size;

        [Header("Misc")]
        [SerializeField] private bool log_warnings = true;

        private sealed class Binding
        {
            public string avatar_name;
            public Transform root;
            public Transform hips;
            public Transform[] bones;
            public string[] targets;
            public string[] human_bones;
            public float[] rest;      // 4 per bone
            public int bone_offset;
            public int BoneCount => bones.Length;
        }

        private readonly object packet_gate = new object();
        private readonly object client_gate = new object();
        private readonly AutoResetEvent packet_signal = new AutoResetEvent(false);
        private readonly byte[] length_buffer = new byte[4];
        private readonly byte[] kind_buffer = new byte[1];
        private readonly List<Binding> bindings = new List<Binding>();

        private PoseFrameV3 frame_message;
        private float[] rotation_buffer = Array.Empty<float>();
        private byte[] rotation_bytes = Array.Empty<byte>();
        private readonly float[] root_scratch = new float[7];
        private readonly float[] hips_scratch = new float[7];
        private readonly float[] camera_scratch = new float[7];
        private readonly float[] scene_view_pivot_scratch = new float[3];

        private TcpClient tcp_client;
        private NetworkStream network_stream;
        private Thread sender_thread;
        private byte[] latest_packet;
        // Definitions are queued separately: pose packets intentionally overwrite each
        // other (only the newest matters), and the handshake must never be the one
        // that gets dropped - without it the receiver has no bone order and ignores
        // every frame that follows.
        private byte[] pending_definitions;
        private bool has_latest_packet;
        private volatile bool sender_running;
        private volatile bool definitions_pending;
        private long frame_index;
        private bool bound;

        public int ActiveAvatarCount => bindings.Count;
        public int TotalBoneCount => rotation_buffer.Length / 4;
        public bool IsStreaming => sender_running;
        public string Host => host;
        public int Port => port;

        /// <summary>Per-avatar bone counts, for the window's status readout.</summary>
        public IEnumerable<KeyValuePair<string, int>> DescribeAvatars()
        {
            foreach (var binding in bindings)
            {
                yield return new KeyValuePair<string, int>(binding.avatar_name, binding.BoneCount);
            }
        }

        /// <summary>
        /// Inject the whole configuration at runtime. Used by the editor bootstrap,
        /// which spawns this component instead of it being authored into the scene.
        /// </summary>
        public void Configure(string host_value, int port_value, int interval_frames,
                              IEnumerable<AvatarEntry> entries, bool log)
        {
            host = host_value;
            port = port_value;
            send_interval_frames = Mathf.Max(1, interval_frames);
            log_warnings = log;
            avatars = new List<AvatarEntry>(entries ?? Array.Empty<AvatarEntry>());
            bound = false;
        }

        public void ConfigureCamera(Camera source, string name_override = "")
        {
            ConfigureCameraSource(source, name_override);
            camera_is_scene_view = false;
        }

        public void ConfigureSceneView(Camera source, Vector3 pivot, float distance, float size)
        {
            ConfigureCameraSource(source, "Unity Scene View");
            camera_is_scene_view = true;
            scene_view_pivot = pivot;
            scene_view_distance = Mathf.Max(0f, distance);
            scene_view_size = Mathf.Max(0.0001f, size);
        }

        private void ConfigureCameraSource(Camera source, string name_override)
        {
            var source_changed = camera_source != source;
            camera_source = source;
            camera_name_override = name_override ?? "";
            if (frame_message != null
                && (source == null || source_changed || frame_message.camera == null))
            {
                frame_message.camera = CreateCameraMessage(source);
            }
        }

        private void Start()
        {
            // Start is late enough that NDMF / avatar build steps have finished
            // restructuring the hierarchy, and still before the Animator applies its
            // first pose - so what we read here is the bind pose.
            if (Application.isPlaying)
            {
                Bind();
                if (connect_on_enable)
                {
                    StartSender();
                }
            }
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // Commands are drained from EditorApplication.update, not Update():
            // MonoBehaviour.Update stops running while Play Mode is paused, which is
            // exactly when Step needs to be delivered.
            UnityEditor.EditorApplication.update += DrainCommands;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= DrainCommands;
#endif
            StopSender();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= DrainCommands;
#endif
            StopSender();
        }

        private void OnApplicationQuit() => StopSender();

        /// <summary>
        /// Re-resolve every avatar's bones and re-capture the rest pose.
        /// The avatars must be AT REST when this runs, or the rest pose is wrong and
        /// every bone gets a constant offset.
        /// </summary>
        [ContextMenu("Rebind Avatars")]
        public void Bind()
        {
            bindings.Clear();
            var total_bones = 0;

            foreach (var entry in avatars)
            {
                if (entry == null || !entry.send)
                {
                    continue;
                }
                var search_root = entry.ResolveSearchRoot();
                if (search_root == null)
                {
                    continue;
                }
                if (!search_root.gameObject.activeInHierarchy)
                {
                    if (log_warnings)
                    {
                        Debug.LogWarning(
                            $"{nameof(PoseSyncManager)}: '{entry.ResolveName()}' is inactive; skipped.",
                            search_root);
                    }
                    continue;
                }

                var binding = BuildBinding(entry);
                if (binding == null || binding.BoneCount == 0)
                {
                    if (log_warnings)
                    {
                        Debug.LogWarning(
                            $"{nameof(PoseSyncManager)}: '{entry.ResolveName()}' produced no bones; skipped.",
                            search_root);
                    }
                    continue;
                }

                binding.bone_offset = total_bones;
                total_bones += binding.BoneCount;
                bindings.Add(binding);
            }

            rotation_buffer = new float[total_bones * 4];
            rotation_bytes = new byte[total_bones * 4 * sizeof(float)];
            BuildFrameMessage();
            bound = true;
            definitions_pending = true;
        }

        private Binding BuildBinding(AvatarEntry entry)
        {
            var animator = entry.animator;
            var search_root = entry.ResolveSearchRoot();
            var root = entry.ResolveRoot();
            if (search_root == null || root == null)
            {
                return null;
            }

            // The Animator has usually applied frame 0 of its controller before Start
            // runs. Capturing rest from that gives a POSED skeleton, and since every
            // bone is then sent as a delta from it, the whole rig arrives in Blender
            // with a constant offset baked in. Rebind() puts the rig back on its bind
            // pose so rest means what the receiver assumes it means.
            if (animator != null && animator.isHuman && animator.runtimeAnimatorController != null)
            {
                animator.Rebind();
            }

            var seen = new HashSet<Transform>();
            var bones = new List<Transform>();
            var targets = new List<string>();
            var humans = new List<string>();
            Transform hips = null;

            if (entry.include_humanoid && animator != null)
            {
                if (animator.isHuman)
                {
                    for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        var bone = (HumanBodyBones)i;
                        var t = animator.GetBoneTransform(bone);
                        if (t == null || !seen.Add(t))
                        {
                            continue;
                        }
                        if (bone == HumanBodyBones.Hips)
                        {
                            hips = t;
                        }
                        bones.Add(t);
                        targets.Add(entry.strip_mangled_names ? StripMangledName(t.name) : t.name);
                        humans.Add(bone.ToString());
                    }
                }
                else if (log_warnings)
                {
                    Debug.LogWarning(
                        $"{nameof(PoseSyncManager)}: '{entry.ResolveName()}' has no Humanoid Animator.",
                        animator);
                }
            }

            var extras = new List<Transform>();
            foreach (var extra in entry.extra_roots ?? Array.Empty<ExtraRoot>())
            {
                if (extra == null || extra.transform == null)
                {
                    continue;
                }
                if (extra.include_descendants)
                {
                    CollectChain(extra.transform, null, seen, extras);
                }
                else if (seen.Add(extra.transform))
                {
                    extras.Add(extra.transform);
                }
            }
            if (entry.include_all_phys_bones)
            {
                CollectPhysBones(search_root.gameObject, entry.phys_bones_respect_ignore, seen, extras);
            }
            foreach (var t in extras)
            {
                bones.Add(t);
                targets.Add(entry.strip_mangled_names ? StripMangledName(t.name) : t.name);
                humans.Add(null);
            }

            var rest = new float[bones.Count * 4];
            var inverse_root = Quaternion.Inverse(root.rotation);
            for (var i = 0; i < bones.Count; i++)
            {
                var q = inverse_root * bones[i].rotation;
                rest[i * 4] = q.x;
                rest[i * 4 + 1] = q.y;
                rest[i * 4 + 2] = q.z;
                rest[i * 4 + 3] = q.w;
            }

            return new Binding
            {
                avatar_name = entry.ResolveName(),
                root = root,
                hips = hips != null ? hips : root,
                bones = bones.ToArray(),
                targets = targets.ToArray(),
                human_bones = humans.ToArray(),
                rest = rest,
            };
        }

        /// <summary>
        /// Strip Modular Avatar's collision-avoidance suffix from a bone name.
        ///
        /// Merge Armature renames every object it keeps on the outfit side to
        /// "&lt;original&gt;$&lt;guid&gt;" (and Constraint helpers to
        /// "&lt;original&gt;$ConstraintRef &lt;guid&gt;"), so the part before '$' is always
        /// the authored name - which is what the Blender armature still uses. '$' does
        /// not occur in ordinary bone names, so cutting there is safe.
        /// </summary>
        public static string StripMangledName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }
            var index = name.IndexOf('$');
            return index > 0 ? name.Substring(0, index) : name;
        }

        // ---- VRCPhysBone discovery via reflection, so this compiles without the SDK ----

        private static void CollectPhysBones(GameObject avatar_root, bool respect_ignore,
                                             HashSet<Transform> seen, List<Transform> into)
        {
            foreach (var behaviour in avatar_root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "VRCPhysBone")
                {
                    continue;
                }
                var chain_root = GetPhysBoneRoot(behaviour);
                // Unity's overloaded == catches unassigned refs; ?? would not.
                if (chain_root == null)
                {
                    chain_root = behaviour.transform;
                }
                CollectChain(chain_root, respect_ignore ? GetPhysBoneIgnores(behaviour) : null, seen, into);
            }
        }

        private static Transform GetPhysBoneRoot(MonoBehaviour phys_bone)
        {
            return phys_bone.GetType().GetField("rootTransform")?.GetValue(phys_bone) as Transform;
        }

        private static HashSet<Transform> GetPhysBoneIgnores(MonoBehaviour phys_bone)
        {
            if (phys_bone.GetType().GetField("ignoreTransforms")?.GetValue(phys_bone)
                is System.Collections.IEnumerable list)
            {
                var ignores = new HashSet<Transform>();
                foreach (var item in list)
                {
                    if (item is Transform t && t != null)
                    {
                        ignores.Add(t);
                    }
                }
                return ignores;
            }
            return null;
        }

        private static void CollectChain(Transform chain_root, HashSet<Transform> ignores,
                                         HashSet<Transform> seen, List<Transform> into)
        {
            if (chain_root == null)
            {
                return;
            }
            foreach (var t in chain_root.GetComponentsInChildren<Transform>(true))
            {
                if (ignores != null && IsUnderIgnored(t, chain_root, ignores))
                {
                    continue;
                }
                if (seen.Add(t))
                {
                    into.Add(t);
                }
            }
        }

        private static bool IsUnderIgnored(Transform t, Transform chain_root, HashSet<Transform> ignores)
        {
            var cur = t;
            while (cur != null)
            {
                if (ignores.Contains(cur))
                {
                    return true;
                }
                if (cur == chain_root)
                {
                    break;
                }
                cur = cur.parent;
            }
            return false;
        }

        // ---- frame assembly ----

        private void BuildFrameMessage()
        {
            frame_message = new PoseFrameV3
            {
                schema = SchemaName,
                version = SchemaVersion,
                message_type = "pose",
                avatars = new AvatarPoseEntry[bindings.Count],
                camera = CreateCameraMessage(camera_source),
            };
            for (var i = 0; i < bindings.Count; i++)
            {
                frame_message.avatars[i] = new AvatarPoseEntry
                {
                    avatar_name = bindings[i].avatar_name,
                    root = new byte[7 * sizeof(float)],
                    hips = new byte[7 * sizeof(float)],
                    rotations = new byte[bindings[i].BoneCount * 4 * sizeof(float)],
                };
            }
        }

        private AvatarDefsMessage BuildDefinitions()
        {
            var message = new AvatarDefsMessage
            {
                schema = SchemaName,
                version = SchemaVersion,
                message_type = "avatar_defs",
                avatars = new AvatarDefEntry[bindings.Count],
            };
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var rest_bytes = new byte[binding.rest.Length * sizeof(float)];
                Buffer.BlockCopy(binding.rest, 0, rest_bytes, 0, rest_bytes.Length);
                message.avatars[i] = new AvatarDefEntry
                {
                    avatar_name = binding.avatar_name,
                    targets = binding.targets,
                    human_bones = binding.human_bones,
                    rest_rotations = rest_bytes,
                };
            }
            return message;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !bound
                || (bindings.Count == 0 && camera_source == null))
            {
                return;
            }

            if (!sender_running && connect_on_enable)
            {
                StartSender();
            }
            if (!sender_running)
            {
                return;
            }
            if (send_interval_frames > 1 && Time.frameCount % send_interval_frames != 0)
            {
                return;
            }

            if (definitions_pending)
            {
                // Definitions must land before any pose: without them the receiver has
                // no bone order to match the blobs against and drops the frame.
                PublishDefinitions(PoseSyncMessagePack.Serialize(BuildDefinitions()));
                definitions_pending = false;
            }

            CaptureFrame();
            PublishPacket(PoseSyncMessagePack.Serialize(frame_message));
        }

        private void CaptureFrame()
        {
            frame_message.frame = frame_index++;
            frame_message.unity_time = Time.timeAsDouble;

            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var entry = frame_message.avatars[i];
                var root = binding.root;
                var inverse_root = Quaternion.Inverse(root.rotation);

                WriteTransform(root, root_scratch);
                WriteTransform(binding.hips, hips_scratch);
                Buffer.BlockCopy(root_scratch, 0, entry.root, 0, entry.root.Length);
                Buffer.BlockCopy(hips_scratch, 0, entry.hips, 0, entry.hips.Length);

                var bones = binding.bones;
                for (var b = 0; b < bones.Length; b++)
                {
                    var t = bones[b];
                    var o = (binding.bone_offset + b) * 4;
                    if (t == null)
                    {
                        rotation_buffer[o] = 0f;
                        rotation_buffer[o + 1] = 0f;
                        rotation_buffer[o + 2] = 0f;
                        rotation_buffer[o + 3] = 1f;
                        continue;
                    }
                    var q = inverse_root * t.rotation;
                    rotation_buffer[o] = q.x;
                    rotation_buffer[o + 1] = q.y;
                    rotation_buffer[o + 2] = q.z;
                    rotation_buffer[o + 3] = q.w;
                }
            }

            Buffer.BlockCopy(rotation_buffer, 0, rotation_bytes, 0, rotation_bytes.Length);
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                var entry = frame_message.avatars[i];
                Buffer.BlockCopy(rotation_bytes, binding.bone_offset * 4 * sizeof(float),
                                 entry.rotations, 0, entry.rotations.Length);
            }

            CaptureCamera();
        }

        private static CameraPoseV3 CreateCameraMessage(Camera source)
        {
            return source == null
                ? null
                : new CameraPoseV3 { transform = new byte[7 * sizeof(float)] };
        }

        private void CaptureCamera()
        {
            if (camera_source == null)
            {
                frame_message.camera = null;
                return;
            }
            if (frame_message.camera == null)
            {
                frame_message.camera = CreateCameraMessage(camera_source);
            }

            var message = frame_message.camera;
            WriteTransform(camera_source.transform, camera_scratch);
            Buffer.BlockCopy(camera_scratch, 0, message.transform, 0, message.transform.Length);
            message.camera_name = string.IsNullOrEmpty(camera_name_override)
                ? camera_source.name
                : camera_name_override;
            message.orthographic = camera_source.orthographic;
            message.field_of_view = camera_source.fieldOfView;
            message.orthographic_size = camera_source.orthographicSize;
            message.aspect = camera_source.aspect;
            message.near_clip = camera_source.nearClipPlane;
            message.far_clip = camera_source.farClipPlane;
            message.scene_view = camera_is_scene_view;
            if (camera_is_scene_view)
            {
                scene_view_pivot_scratch[0] = scene_view_pivot.x;
                scene_view_pivot_scratch[1] = scene_view_pivot.y;
                scene_view_pivot_scratch[2] = scene_view_pivot.z;
                Buffer.BlockCopy(scene_view_pivot_scratch, 0, message.view_pivot, 0,
                                 message.view_pivot.Length);
                message.view_distance = scene_view_distance;
                message.view_size = scene_view_size;
            }
        }

        private static void WriteTransform(Transform t, float[] dst)
        {
            var p = t.position;
            var q = t.rotation;
            dst[0] = p.x; dst[1] = p.y; dst[2] = p.z;
            dst[3] = q.x; dst[4] = q.y; dst[5] = q.z; dst[6] = q.w;
        }

        // ---- networking ----

        private void PublishPacket(byte[] packet)
        {
            lock (packet_gate)
            {
                latest_packet = packet;
                has_latest_packet = true;
            }
            packet_signal.Set();
        }

        private void PublishDefinitions(byte[] packet)
        {
            lock (packet_gate)
            {
                pending_definitions = packet;
            }
            packet_signal.Set();
        }

        [ContextMenu("Reconnect")]
        public void Reconnect()
        {
            StopSender(false);
            StartSender();
        }

        private void StartSender()
        {
            if (sender_running)
            {
                return;
            }
            sender_running = true;
            definitions_pending = true;
            sender_thread = new Thread(SenderLoop)
            {
                IsBackground = true,
                Name = "PoseSyncManager",
            };
            sender_thread.Start();
        }

        private void StopSender(bool send_stopped_message = true)
        {
            if (!sender_running)
            {
                CloseClient();
                return;
            }

            sender_running = false;
            lock (packet_gate)
            {
                latest_packet = null;
                pending_definitions = null;
                has_latest_packet = false;
            }
            packet_signal.Set();

            var thread = sender_thread;
            sender_thread = null;
            if (thread != null && thread.IsAlive && !thread.Join(500))
            {
                // Blocked in Connect() or Write(): closing the socket unblocks it.
                // Otherwise the thread can outlive Play Mode and open a connection
                // nobody closes, which then occupies the receiver's accept slot.
                CloseClient();
                thread.Join(500);
            }

            if (send_stopped_message)
            {
                TrySendStateMessage("stopped");
            }
            CloseClient();
        }

        private void TrySendStateMessage(string state)
        {
            try
            {
                WritePacket(PoseSyncMessagePack.Serialize(new PoseStateV3
                {
                    schema = SchemaName,
                    version = SchemaVersion,
                    message_type = "state",
                    state = state,
                    frame = frame_index,
                }), PacketKindControl);
            }
            catch
            {
                // Play Mode shutdown must not be blocked by a failed final packet.
            }
        }

        private void SenderLoop()
        {
            while (sender_running)
            {
                if (!EnsureConnected())
                {
                    packet_signal.WaitOne(250);
                    continue;
                }

                packet_signal.WaitOne(100);

                byte[] definitions = null;
                byte[] packet = null;
                lock (packet_gate)
                {
                    if (pending_definitions != null)
                    {
                        definitions = pending_definitions;
                        pending_definitions = null;
                    }
                    if (has_latest_packet)
                    {
                        packet = latest_packet;
                        has_latest_packet = false;
                    }
                }
                // Blender can push Play Mode commands back over the same socket.
                PollIncomingCommands();

                if (definitions == null && packet == null)
                {
                    continue;
                }

                try
                {
                    // Always the handshake first, then the newest pose.
                    if (definitions != null)
                    {
                        WritePacket(definitions, PacketKindControl);
                    }
                    if (packet != null)
                    {
                        WritePacket(packet, PacketKindPose);
                    }
                }
                catch
                {
                    CloseClient();
                    // The receiver never saw our handshake (or lost it): re-send it on
                    // the new connection before any pose.
                    definitions_pending = true;
                }
            }
        }

        private bool EnsureConnected()
        {
            lock (client_gate)
            {
                if (tcp_client != null && tcp_client.Connected && network_stream != null)
                {
                    return true;
                }
            }

            CloseClient();
            if (!sender_running)
            {
                return false;
            }

            try
            {
                var client = new TcpClient
                {
                    NoDelay = true,
                    SendTimeout = 1000,
                    ReceiveTimeout = 1000,
                };
                client.Connect(host, port);
                if (!sender_running)
                {
                    try { client.Close(); } catch { /* ignored */ }
                    return false;
                }
                lock (client_gate)
                {
                    tcp_client = client;
                    network_stream = client.GetStream();
                }
                definitions_pending = true;
                return true;
            }
            catch
            {
                CloseClient();
                if (sender_running)
                {
                    Thread.Sleep(200);
                }
                return false;
            }
        }

        // Framing: 4-byte big-endian length, one kind byte, then the MessagePack body.
        // The kind byte lets the receiver throw away a backlog of stale pose packets
        // without decoding them, while never discarding a control packet - dropping
        // the handshake would leave it with no bone order and it would ignore every
        // pose that followed.
        // Public: the manager window sends an edit-mode announce over its own
        // short-lived connection and must frame it identically.
        public const byte PacketKindPose = 0;
        public const byte PacketKindControl = 1;
        // Blender -> Unity Play Mode commands, riding the same connection.
        public const byte PacketKindCommand = 2;

        private void WritePacket(byte[] packet, byte kind = PacketKindPose)
        {
            var size = packet.Length + 1;
            length_buffer[0] = (byte)(size >> 24);
            length_buffer[1] = (byte)(size >> 16);
            length_buffer[2] = (byte)(size >> 8);
            length_buffer[3] = (byte)size;
            kind_buffer[0] = kind;

            lock (client_gate)
            {
                if (network_stream == null)
                {
                    throw new InvalidOperationException("Network stream is not connected.");
                }
                network_stream.Write(length_buffer, 0, length_buffer.Length);
                network_stream.Write(kind_buffer, 0, 1);
                network_stream.Write(packet, 0, packet.Length);
                network_stream.Flush();
            }
        }

        private void CloseClient()
        {
            lock (client_gate)
            {
                try { network_stream?.Close(); } catch { /* ignored */ }
                try { tcp_client?.Close(); } catch { /* ignored */ }
                network_stream = null;
                tcp_client = null;
            }
        }

        // ---- Blender -> Unity commands ----

        private readonly Queue<PoseSyncCommand> incoming_commands = new Queue<PoseSyncCommand>();
        private readonly byte[] command_header = new byte[4];

        /// <summary>
        /// Drain any command packets Blender has queued on the socket. Runs on the
        /// sender thread between writes, so the worst-case latency is one
        /// packet_signal wait (100 ms) - fine for Pause/Step, and it avoids a second
        /// thread contending for the same stream.
        /// </summary>
        private void PollIncomingCommands()
        {
            lock (client_gate)
            {
                var client = tcp_client;
                var stream = network_stream;
                if (client == null || stream == null)
                {
                    return;
                }

                try
                {
                    while (client.Available >= 4)
                    {
                        if (!ReadExact(stream, command_header, 4))
                        {
                            return;
                        }
                        var size = (command_header[0] << 24) | (command_header[1] << 16)
                                 | (command_header[2] << 8) | command_header[3];
                        if (size <= 1 || size > 4097)
                        {
                            CloseClientNoLock();
                            return;
                        }

                        var body = new byte[size];
                        if (!ReadExact(stream, body, size))
                        {
                            return;
                        }
                        if (body[0] != PacketKindCommand)
                        {
                            continue;
                        }

                        var command = PoseSyncMessagePack.DeserializeCommand(body, 1, size - 1);
                        lock (incoming_commands)
                        {
                            incoming_commands.Enqueue(command);
                        }
                    }
                }
                catch
                {
                    CloseClientNoLock();
                }
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            var read = 0;
            while (read < count)
            {
                var got = stream.Read(buffer, read, count - read);
                if (got <= 0)
                {
                    return false;
                }
                read += got;
            }
            return true;
        }

        private void CloseClientNoLock()
        {
            try { network_stream?.Close(); } catch { /* ignored */ }
            try { tcp_client?.Close(); } catch { /* ignored */ }
            network_stream = null;
            tcp_client = null;
        }

        private void DrainCommands()
        {
            while (true)
            {
                PoseSyncCommand command;
                lock (incoming_commands)
                {
                    if (incoming_commands.Count == 0)
                    {
                        return;
                    }
                    command = incoming_commands.Dequeue();
                }
                ExecuteCommand(command);
            }
        }

        private void ExecuteCommand(PoseSyncCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.command))
            {
                return;
            }
#if UNITY_EDITOR
            // Editor-only: Play Mode has no meaning in a build.
            switch (command.command)
            {
                case "pause":
                    UnityEditor.EditorApplication.isPaused = true;
                    break;
                case "resume":
                    UnityEditor.EditorApplication.isPaused = false;
                    break;
                case "step":
                    // Step only advances while paused; pause first so a single click
                    // from Blender does what it looks like it does.
                    UnityEditor.EditorApplication.isPaused = true;
                    for (var i = 0; i < Mathf.Max(1, command.frames); i++)
                    {
                        UnityEditor.EditorApplication.Step();
                    }
                    break;
                case "stop":
                    UnityEditor.EditorApplication.isPlaying = false;
                    break;
                default:
                    if (log_warnings)
                    {
                        Debug.LogWarning($"{nameof(PoseSyncManager)}: unknown command '{command.command}'.");
                    }
                    break;
            }
#else
            if (log_warnings)
            {
                Debug.LogWarning($"{nameof(PoseSyncManager)}: Play Mode commands are editor-only.");
            }
#endif
        }
    }
    [MessagePackObject]
    public sealed class PoseSyncCommand
    {
        [Key("message_type")] public string message_type = "command";
        [Key("command")] public string command = "";
        [Key("frames")] public int frames = 1;
    }

    // ---- schema v3 wire messages ----
    // Rotations travel as float32 blobs (MessagePack bin), little-endian, so the
    // Blender side decodes them with array.array in a single call.

    /// <summary>
    /// Edit-mode announce: just the avatar/garment names the manager window has
    /// configured. Lets the Blender panel build its slots before Play Mode ever runs.
    /// Carries no bone data - that only exists once Play Mode binds the rigs.
    /// </summary>
    [MessagePackObject]
    public sealed class AvatarListMessage
    {
        [Key("schema")] public string schema = "";
        [Key("version")] public int version = 3;
        [Key("message_type")] public string message_type = "avatar_list";
        [Key("avatars")] public string[] avatars = Array.Empty<string>();
    }
    [MessagePackObject]
    public sealed class AvatarDefsMessage
    {
        [Key("schema")] public string schema = "";
        [Key("version")] public int version = 3;
        [Key("message_type")] public string message_type = "avatar_defs";
        [Key("avatars")] public AvatarDefEntry[] avatars = Array.Empty<AvatarDefEntry>();
    }
    [MessagePackObject]
    public sealed class AvatarDefEntry
    {
        [Key("avatar_name")] public string avatar_name = "";
        [Key("targets")] public string[] targets = Array.Empty<string>();
        [Key("human_bones")] public string[] human_bones = Array.Empty<string>();
        [Key("rest_rotations")] public byte[] rest_rotations = Array.Empty<byte>();
    }
    [MessagePackObject]
    public sealed class PoseFrameV3
    {
        [Key("schema")] public string schema = "";
        [Key("version")] public int version = 3;
        [Key("message_type")] public string message_type = "pose";
        [Key("frame")] public long frame;
        [Key("unity_time")] public double unity_time;
        [Key("avatars")] public AvatarPoseEntry[] avatars = Array.Empty<AvatarPoseEntry>();
        [Key("camera")] public CameraPoseV3 camera;
    }
    [MessagePackObject]
    public sealed class CameraPoseV3
    {
        [Key("camera_name")] public string camera_name = "";
        [Key("transform")] public byte[] transform = Array.Empty<byte>();
        [Key("orthographic")] public bool orthographic;
        [Key("field_of_view")] public float field_of_view = 60f;
        [Key("orthographic_size")] public float orthographic_size = 5f;
        [Key("aspect")] public float aspect = 16f / 9f;
        [Key("near_clip")] public float near_clip = 0.01f;
        [Key("far_clip")] public float far_clip = 1000f;
        [Key("scene_view")] public bool scene_view;
        [Key("view_pivot")] public byte[] view_pivot = new byte[3 * sizeof(float)];
        [Key("view_distance")] public float view_distance;
        [Key("view_size")] public float view_size;
    }
    [MessagePackObject]
    public sealed class AvatarPoseEntry
    {
        [Key("avatar_name")] public string avatar_name = "";
        [Key("root")] public byte[] root = Array.Empty<byte>();
        [Key("hips")] public byte[] hips = Array.Empty<byte>();
        [Key("rotations")] public byte[] rotations = Array.Empty<byte>();
    }
    [MessagePackObject]
    public sealed class PoseStateV3
    {
        [Key("schema")] public string schema = "";
        [Key("version")] public int version = 3;
        [Key("message_type")] public string message_type = "state";
        [Key("state")] public string state = "";
        [Key("frame")] public long frame;
    }
}
