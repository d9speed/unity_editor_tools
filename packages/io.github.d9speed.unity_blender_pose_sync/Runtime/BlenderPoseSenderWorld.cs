using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace UnityBlenderPoseSync.World
{
    /// <summary>
    /// Unity -> Blender pose sender. Every bone is expressed as a ROOT-SPACE WORLD rotation:
    ///
    ///     rotation = Inverse(root.rotation) * bone.rotation
    ///
    /// Each bone is then independent. The Blender side just does:
    ///     dUnity   = rotation * rest_rotation^-1
    ///     dBlender = G * dUnity * G^T        (G = (x,y,z)->(-x,-z,y))
    ///     target   = dBlender * blender_rest
    /// No FK reconstruction, no root-offset correction. This also generalises to arbitrary
    /// (non-Humanoid) Transform bones such as skirt / hair / tail / breast jiggle bones.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlenderPoseSenderWorld : MonoBehaviour
    {
        private const string SchemaName = "unity_blender_pose_sync.world";
        private const int SchemaVersion = 2;

        [Header("Network")]
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 39541;
        [SerializeField] private bool connect_on_enable = true;
        [SerializeField, Min(1)] private int send_interval_frames = 1;

        [Header("Bones")]
        [Tooltip("Model root the bone rotations are expressed relative to. Defaults to this Transform.")]
        [SerializeField] private Transform root_override;
        [Tooltip("Include the Humanoid bones from an Animator on this GameObject.")]
        [SerializeField] private bool include_humanoid = true;
        [Tooltip("Extra (non-Humanoid) roots to send, e.g. skirt / hair / tail chain roots.")]
        [SerializeField] private Transform[] extra_roots = Array.Empty<Transform>();
        [Tooltip("Include every descendant of each extra root (the whole jiggle chain).")]
        [SerializeField] private bool extra_include_descendants = true;
        [Tooltip("Objects to send as PhysBone chains. For each, if it has a VRCPhysBone its Root " +
                 "Transform is used (or the object itself when Root Transform is None); settings may " +
                 "be consolidated on another object. Without a PhysBone the object is used as a plain chain root.")]
        [SerializeField] private Transform[] phys_bone_objects = Array.Empty<Transform>();
        [Tooltip("Also auto-collect every VRCPhysBone chain found anywhere on this avatar.")]
        [SerializeField] private bool include_all_phys_bones = false;
        [Tooltip("Skip transforms in each PhysBone's Ignore Transforms (and their descendants).")]
        [SerializeField] private bool phys_bones_respect_ignore = true;
        // Baked bone references (original name -> final object), filled by the optional NDMF
        // build pass. When non-empty these replace runtime extra/PhysBone discovery, so bones
        // track correctly through NDMF hierarchy changes and the original name is sent.
        // Hidden from the Inspector: it is machine-written, not meant to be edited by hand.
        [HideInInspector] [SerializeField] private List<BakedBone> baked_bones = new List<BakedBone>();
        [Tooltip("Also send root-space positions (for bones that translate, not only rotate).")]
        [SerializeField] private bool send_position = false;

        [Header("Misc")]
        [SerializeField] private string avatar_name_override = "";
        [SerializeField] private bool log_warnings = true;

        // The Blender receiver services one connection at a time. A second sender
        // therefore sits unread in the accept queue, fills its send buffer, times
        // out after SendTimeout, reconnects, and churns - and every one of those
        // disconnects costs the receiver a full rig reset. So only one sender
        // streams at a time; the first to start wins and the rest stay idle.
        private static BlenderPoseSenderWorld active_sender;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveSender()
        {
            // Static state survives "Enter Play Mode Options" with domain reload off.
            active_sender = null;
        }

        private readonly object packet_gate = new object();
        private readonly object client_gate = new object();
        private readonly AutoResetEvent packet_signal = new AutoResetEvent(false);
        private readonly byte[] length_buffer = new byte[4];

        private BoneCache[] bone_caches = Array.Empty<BoneCache>();
        private Transform hips_transform;
        private WorldPoseFrameMessage frame_message = new WorldPoseFrameMessage();
        private TcpClient tcp_client;
        private NetworkStream network_stream;
        private Thread sender_thread;
        private byte[] latest_packet;
        private bool has_latest_packet;
        private volatile bool sender_running;
        private bool warned_not_owner;
        private long frame_index;

        private Transform Root => root_override != null ? root_override : transform;

        private void Reset()
        {
            root_override = transform;
        }

        private void Awake()
        {
            RebuildCache();
        }

        private void OnEnable()
        {
            // Note: rest is captured here. The avatar must be in its bind/rest pose at this
            // moment (true at Awake / before physics+animation drive the bones).
            RebuildCache();
            if (Application.isPlaying && connect_on_enable && !ManagedExternally)
            {
                StartSender();
            }
        }

        private void Start()
        {
            // NDMF / avatar build steps can restructure the hierarchy (move, reparent,
            // recreate bones) during Awake-time hooks. Start runs after all of those, and
            // still before the Animator applies a pose, so rebuilding here captures the
            // final post-build bones with a correct bind-pose rest. If your build finishes
            // even later, call "Rebuild Cache" from the component context menu while the
            // avatar is at rest.
            if (Application.isPlaying)
            {
                RebuildCache();
            }
        }

        // ---- schema v3: driven by PoseSyncManager over a single shared connection ----

        /// <summary>Bones this sender streams, in the order the receiver expects them.</summary>
        public int BoneCount => bone_caches.Length;

        /// <summary>Name this avatar is announced under. Must match the Blender slot.</summary>
        public string AvatarName => GetAvatarName();

        /// <summary>
        /// Set by <see cref="PoseSyncManager"/> while it owns the connection. The sender
        /// then never opens a socket of its own - the manager pulls bone data from it
        /// and sends every avatar in one packet.
        /// </summary>
        public bool ManagedExternally { get; set; }

        /// <summary>Fill the handshake data: bone names and their Unity rest rotations.</summary>
        public void FillDefinitions(string[] targets, string[] human_bones, float[] rest_rotations, int offset)
        {
            for (var i = 0; i < bone_caches.Length; i++)
            {
                var cache = bone_caches[i];
                targets[offset + i] = cache.target_name;
                human_bones[offset + i] = cache.human_bone;
                var r = cache.rest_rotation;
                var b = (offset + i) * 4;
                rest_rotations[b] = r.x;
                rest_rotations[b + 1] = r.y;
                rest_rotations[b + 2] = r.z;
                rest_rotations[b + 3] = r.w;
            }
        }

        /// <summary>
        /// Write this frame's root-space world rotations into a shared buffer.
        /// `root7` / `hips7` take position(3) + rotation(4).
        /// </summary>
        public void CaptureInto(float[] rotations, int offset, float[] root7, float[] hips7)
        {
            var root = Root;
            var inverse_root = Quaternion.Inverse(root.rotation);

            if (root7 != null && root7.Length >= 7)
            {
                var p = root.position;
                var q = root.rotation;
                root7[0] = p.x; root7[1] = p.y; root7[2] = p.z;
                root7[3] = q.x; root7[4] = q.y; root7[5] = q.z; root7[6] = q.w;
            }
            if (hips7 != null && hips7.Length >= 7)
            {
                var hips = hips_transform != null ? hips_transform : root;
                var p = hips.position;
                var q = hips.rotation;
                hips7[0] = p.x; hips7[1] = p.y; hips7[2] = p.z;
                hips7[3] = q.x; hips7[4] = q.y; hips7[5] = q.z; hips7[6] = q.w;
            }

            for (var i = 0; i < bone_caches.Length; i++)
            {
                var t = bone_caches[i].transform;
                var b = (offset + i) * 4;
                if (t == null)
                {
                    // Bone destroyed (e.g. by an NDMF step). Send identity rather than
                    // stale data; call Rebuild Cache to re-resolve.
                    rotations[b] = 0f; rotations[b + 1] = 0f; rotations[b + 2] = 0f; rotations[b + 3] = 1f;
                    continue;
                }
                var q = inverse_root * t.rotation;
                rotations[b] = q.x;
                rotations[b + 1] = q.y;
                rotations[b + 2] = q.z;
                rotations[b + 3] = q.w;
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || bone_caches.Length == 0)
            {
                return;
            }

            // The manager reads our bones directly and owns the socket.
            if (ManagedExternally)
            {
                return;
            }

            if (!sender_running && connect_on_enable)
            {
                StartSender();
            }

            // Another sender owns the connection (or this one is disconnected):
            // skip capture and serialisation entirely. Building a ~35 KB packet
            // every frame for a socket nobody reads is pure overhead.
            if (!sender_running)
            {
                return;
            }

            if (send_interval_frames > 1 && Time.frameCount % send_interval_frames != 0)
            {
                return;
            }

            CaptureFrame();
            PublishPacket(PoseSyncMessagePack.Serialize(frame_message));
        }

        private void OnDisable() => StopSender();
        private void OnDestroy() => StopSender();
        private void OnApplicationQuit() => StopSender();

        [ContextMenu("Rebuild Cache / Capture Rest Pose")]
        public void RebuildCache()
        {
            var root = Root;
            var seen = new HashSet<Transform>();
            var caches = new List<BoneCache>();
            hips_transform = null;

            if (include_humanoid)
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        var bone = (HumanBodyBones)i;
                        var t = animator.GetBoneTransform(bone);
                        if (t != null && seen.Add(t))
                        {
                            if (bone == HumanBodyBones.Hips)
                            {
                                hips_transform = t;
                            }
                            caches.Add(new BoneCache(t, bone.ToString(), root));
                        }
                    }
                }
                else if (log_warnings)
                {
                    Debug.LogWarning($"{nameof(BlenderPoseSenderWorld)}: include_humanoid is on but no Humanoid Animator was found.", this);
                }
            }

            if (baked_bones != null && baked_bones.Count > 0)
            {
                // NDMF build pass already resolved the extra/PhysBone bones to their final
                // objects and original names; use those directly.
                foreach (var baked in baked_bones)
                {
                    if (baked == null || baked.transform == null || !seen.Add(baked.transform))
                    {
                        continue;
                    }
                    caches.Add(new BoneCache(baked.transform, baked.human_bone, root, baked.target_name));
                }
            }
            else
            {
                var extras = new List<Transform>();
                CollectExtraTransforms(seen, extras);
                foreach (var t in extras)
                {
                    caches.Add(new BoneCache(t, null, root));
                }
            }

            bone_caches = caches.ToArray();

            frame_message.schema = SchemaName;
            frame_message.version = SchemaVersion;
            frame_message.message_type = "pose";
            frame_message.avatar_name = GetAvatarName();
            frame_message.send_position = send_position;
            frame_message.bones = new WorldBoneMessage[bone_caches.Length];

            for (var i = 0; i < bone_caches.Length; i++)
            {
                var cache = bone_caches[i];
                var bone = new WorldBoneMessage
                {
                    key = cache.key,
                    target = cache.target_name,
                    human_bone = cache.human_bone,
                    rest_rotation = new WorldQuat(),
                    rotation = new WorldQuat(),
                };
                bone.rest_rotation.Set(cache.rest_rotation);
                if (send_position)
                {
                    bone.rest_position = new WorldVec3();
                    bone.rest_position.Set(cache.rest_position);
                    bone.position = new WorldVec3();
                }
                frame_message.bones[i] = bone;
            }
        }

        [ContextMenu("Reconnect")]
        public void Reconnect()
        {
            StopSender(false);
            StartSender();
        }

        private void CaptureFrame()
        {
            var root = Root;
            var inverse_root = Quaternion.Inverse(root.rotation);

            frame_message.avatar_name = GetAvatarName();
            frame_message.frame = frame_index++;
            frame_message.unity_time = Time.timeAsDouble;
            frame_message.send_position = send_position;

            frame_message.root_world.rotation.Set(root.rotation);
            frame_message.root_world.position.Set(root.position);

            // Hips world transform: this moves with both root motion and in-place
            // animation, so the receiver can translate the rig like the original
            // local-rotation receiver's "Apply Hips Location" did.
            var hips = hips_transform != null ? hips_transform : root;
            frame_message.hips_world.rotation.Set(hips.rotation);
            frame_message.hips_world.position.Set(hips.position);

            for (var i = 0; i < bone_caches.Length; i++)
            {
                var t = bone_caches[i].transform;
                if (t == null)
                {
                    // The bone Transform was destroyed (e.g. by an NDMF build step).
                    // Skip it; call Rebuild Cache to re-resolve after the build settles.
                    continue;
                }
                var bone = frame_message.bones[i];
                bone.rotation.Set(inverse_root * t.rotation);
                if (send_position)
                {
                    if (bone.position == null)
                    {
                        bone.position = new WorldVec3();
                    }
                    bone.position.Set(root.InverseTransformPoint(t.position));
                }
            }
        }

        private string GetAvatarName()
        {
            return string.IsNullOrWhiteSpace(avatar_name_override) ? gameObject.name : avatar_name_override;
        }

        // ---- networking (mirrors the proven local-rotation sender) ----

        private void PublishPacket(byte[] packet)
        {
            lock (packet_gate)
            {
                latest_packet = packet;
                has_latest_packet = true;
            }

            packet_signal.Set();
        }

        private void StartSender()
        {
            if (sender_running)
            {
                return;
            }

            var owner = active_sender;
            if (owner != null && owner != this)
            {
                if (owner.isActiveAndEnabled && owner.sender_running)
                {
                    if (log_warnings && !warned_not_owner)
                    {
                        warned_not_owner = true;
                        Debug.LogWarning(
                            $"{nameof(BlenderPoseSenderWorld)}: '{owner.name}' is already streaming to Blender, " +
                            $"so '{name}' stays idle. The receiver only serves one sender at a time; " +
                            "disable the other sender (or its GameObject) to hand over.", this);
                    }
                    return;
                }
                // The previous owner was destroyed or stopped: take over.
            }

            active_sender = this;
            warned_not_owner = false;

            sender_running = true;
            sender_thread = new Thread(SenderLoop)
            {
                IsBackground = true,
                Name = "BlenderPoseSenderWorld",
            };
            sender_thread.Start();
        }

        private void StopSender(bool send_stopped_message = true)
        {
            if (active_sender == this)
            {
                active_sender = null;
            }
            warned_not_owner = false;

            if (!sender_running)
            {
                CloseClient();
                return;
            }

            sender_running = false;
            ClearPendingPacket();
            packet_signal.Set();

            var thread = sender_thread;
            sender_thread = null;
            if (thread != null && thread.IsAlive && !thread.Join(500))
            {
                // Blocked in Connect() or Write(). Closing the socket unblocks it.
                // Without this the thread can outlive Play Mode and then open a
                // fresh connection nobody ever closes, which goes on to occupy the
                // receiver's single accept slot and starve the next real sender.
                CloseClient();
                thread.Join(500);
            }

            if (send_stopped_message)
            {
                TrySendStateMessage("stopped");
            }
            CloseClient();
        }

        private void ClearPendingPacket()
        {
            lock (packet_gate)
            {
                latest_packet = null;
                has_latest_packet = false;
            }
        }

        private void TrySendStateMessage(string state)
        {
            try
            {
                var state_message = new WorldPoseStateMessage
                {
                    schema = SchemaName,
                    version = SchemaVersion,
                    message_type = "state",
                    state = state,
                    avatar_name = GetAvatarName(),
                    frame = frame_index,
                    unity_time = Time.timeAsDouble,
                };
                WritePacket(PoseSyncMessagePack.Serialize(state_message));
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

                byte[] packet = null;
                lock (packet_gate)
                {
                    if (has_latest_packet)
                    {
                        packet = latest_packet;
                        has_latest_packet = false;
                    }
                }

                if (packet == null)
                {
                    continue;
                }

                try
                {
                    WritePacket(packet);
                }
                catch
                {
                    CloseClient();
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
                    // Stopped while connecting. Drop it rather than hand Blender a
                    // connection this thread is about to walk away from.
                    try { client.Close(); } catch { /* ignored */ }
                    return false;
                }
                lock (client_gate)
                {
                    tcp_client = client;
                    network_stream = client.GetStream();
                }
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

        private void WritePacket(byte[] packet)
        {
            var size = packet.Length;
            length_buffer[0] = (byte)(size >> 24);
            length_buffer[1] = (byte)(size >> 16);
            length_buffer[2] = (byte)(size >> 8);
            length_buffer[3] = (byte)size;

            lock (client_gate)
            {
                if (network_stream == null)
                {
                    throw new InvalidOperationException("Network stream is not connected.");
                }

                network_stream.Write(length_buffer, 0, length_buffer.Length);
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

        /// <summary>
        /// Resolve the extra (non-Humanoid) bone transforms from the current configuration:
        /// the manual extra roots and the PhysBone chains. The NDMF build pass calls
        /// <see cref="CollectExtraBoneTransformsForBake"/> which wraps this.
        /// </summary>
        private void CollectExtraTransforms(HashSet<Transform> seen, List<Transform> into)
        {
            foreach (var extra_root in extra_roots)
            {
                if (extra_root == null)
                {
                    continue;
                }
                if (extra_include_descendants)
                {
                    foreach (var t in extra_root.GetComponentsInChildren<Transform>(true))
                    {
                        if (seen.Add(t))
                        {
                            into.Add(t);
                        }
                    }
                }
                else if (seen.Add(extra_root))
                {
                    into.Add(extra_root);
                }
            }

            CollectPhysBones(seen, into);
        }

        /// <summary>
        /// Extra bone transforms to bake (PhysBone + extra-root chains), excluding Humanoid bones.
        /// Used by the optional NDMF build pass to bake stable references with original names.
        /// </summary>
        public List<Transform> CollectExtraBoneTransformsForBake()
        {
            var seen = new HashSet<Transform>();
            if (include_humanoid)
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                {
                    for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                    {
                        var t = animator.GetBoneTransform((HumanBodyBones)i);
                        if (t != null)
                        {
                            seen.Add(t);
                        }
                    }
                }
            }
            var list = new List<Transform>();
            CollectExtraTransforms(seen, list);
            return list;
        }

        /// <summary>Called by the NDMF build pass to inject resolved (original name -> final object) bones.</summary>
        public void SetBakedBones(List<BakedBone> bones)
        {
            baked_bones = bones ?? new List<BakedBone>();
        }

        // VRChat PhysBone collection via reflection, so the component does not take a
        // hard dependency on the VRChat SDK (compiles fine in projects without it).
        private void CollectPhysBones(HashSet<Transform> seen, List<Transform> into)
        {
            // Manual selection: each object holds a VRCPhysBone (or is a plain chain root).
            foreach (var holder in phys_bone_objects)
            {
                if (holder == null)
                {
                    continue;
                }
                var phys_bone = FindPhysBoneOn(holder.gameObject);
                Transform chain_root;
                HashSet<Transform> ignores = null;
                if (phys_bone != null)
                {
                    // Root Transform if set (settings may live on a different object), else the object itself.
                    chain_root = GetPhysBoneRoot(phys_bone);
                    if (chain_root == null)
                    {
                        chain_root = holder;
                    }
                    ignores = GetPhysBoneIgnores(phys_bone);
                }
                else
                {
                    chain_root = holder;
                }
                CollectChain(chain_root, ignores, seen, into);
            }

            // Optional: auto-collect every VRCPhysBone on the avatar.
            if (include_all_phys_bones)
            {
                var found = 0;
                foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Name != "VRCPhysBone")
                    {
                        continue;
                    }
                    found++;
                    var chain_root = GetPhysBoneRoot(behaviour);
                    if (chain_root == null)
                    {
                        chain_root = behaviour.transform;
                    }
                    CollectChain(chain_root, GetPhysBoneIgnores(behaviour), seen, into);
                }
                if (log_warnings && found == 0)
                {
                    Debug.Log($"{nameof(BlenderPoseSenderWorld)}: include_all_phys_bones is on but no VRCPhysBone components were found.", this);
                }
            }
        }

        private void CollectChain(Transform chain_root, HashSet<Transform> ignores, HashSet<Transform> seen, List<Transform> into)
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

        private static MonoBehaviour FindPhysBoneOn(GameObject go)
        {
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "VRCPhysBone")
                {
                    return behaviour;
                }
            }
            return null;
        }

        private static Transform GetPhysBoneRoot(MonoBehaviour phys_bone)
        {
            return phys_bone.GetType().GetField("rootTransform")?.GetValue(phys_bone) as Transform;
        }

        private HashSet<Transform> GetPhysBoneIgnores(MonoBehaviour phys_bone)
        {
            if (!phys_bones_respect_ignore)
            {
                return null;
            }
            if (phys_bone.GetType().GetField("ignoreTransforms")?.GetValue(phys_bone) is System.Collections.IEnumerable list)
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

        private static string BuildKey(Transform t, Transform root)
        {
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private readonly struct BoneCache
        {
            public BoneCache(Transform transform, string human_bone, Transform root, string target_name = null)
            {
                this.transform = transform;
                this.human_bone = human_bone;
                this.target_name = string.IsNullOrEmpty(target_name) ? transform.name : target_name;
                key = BuildKey(transform, root);
                rest_rotation = Quaternion.Inverse(root.rotation) * transform.rotation;
                rest_position = root.InverseTransformPoint(transform.position);
            }

            public readonly Transform transform;
            public readonly string human_bone;
            public readonly string target_name;
            public readonly string key;
            public readonly Quaternion rest_rotation;
            public readonly Vector3 rest_position;
        }
    }

    [Serializable]
    public sealed class BakedBone
    {
        [Tooltip("Bone name to send as target (the original pre-build name; matches the Blender armature).")]
        public string target_name;
        [Tooltip("Humanoid bone name, or empty/null for ordinary (PhysBone/extra) bones.")]
        public string human_bone;
        [Tooltip("The final (post-build) Transform whose live world rotation is read each frame.")]
        public Transform transform;
    }
    public sealed class WorldPoseFrameMessage
    {
        public string schema = "unity_blender_pose_sync.world";
        public int version = 2;
        public string message_type = "pose";
        public string avatar_name = "";
        public long frame;
        public double unity_time;
        public bool send_position;
        public WorldTransformMsg root_world = new WorldTransformMsg();
        public WorldTransformMsg hips_world = new WorldTransformMsg();
        public WorldBoneMessage[] bones = Array.Empty<WorldBoneMessage>();
    }
    public sealed class WorldPoseStateMessage
    {
        public string schema = "unity_blender_pose_sync.world";
        public int version = 2;
        public string message_type = "state";
        public string state = "";
        public string avatar_name = "";
        public long frame;
        public double unity_time;
    }
    public sealed class WorldBoneMessage
    {
        public string key = "";
        public string target = "";
        public string human_bone;            // null for non-Humanoid bones
        public WorldQuat rest_rotation = new WorldQuat();
        public WorldQuat rotation = new WorldQuat();
        public WorldVec3 rest_position;   // null unless send_position
        public WorldVec3 position;             // null unless send_position
    }
    public sealed class WorldTransformMsg
    {
        public WorldVec3 position = new WorldVec3();
        public WorldQuat rotation = new WorldQuat();
    }
    public sealed class WorldVec3
    {
        public float x;
        public float y;
        public float z;

        public void Set(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }
    public sealed class WorldQuat
    {
        public float x;
        public float y;
        public float z;
        public float w = 1.0f;

        public void Set(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }
}
