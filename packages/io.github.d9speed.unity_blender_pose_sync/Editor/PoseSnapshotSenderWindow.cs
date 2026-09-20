using System;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityBlenderPoseSync.World;
using Object = UnityEngine.Object;

namespace UnityBlenderPoseSync.World.Editor
{
    public sealed class PoseSnapshotSenderWindow : EditorWindow
    {
        private const string MenuPath = "D9speed/Animation/Send Pose Snapshot to Blender";
        private const string HostPref = "D9speed.PoseSnapshot.Host";
        private const string PortPref = "D9speed.PoseSnapshot.Port";

        private ObjectField animator_field;
        private ObjectField clip_field;
        private IntegerField frame_field;
        private TextField host_field;
        private IntegerField port_field;
        private Label status_label;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            GetWindow<PoseSnapshotSenderWindow>("Pose Snapshot");
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            animator_field = new ObjectField("Animator")
            {
                objectType = typeof(Animator),
                allowSceneObjects = true,
            };
            clip_field = new ObjectField("Animation Clip")
            {
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
            };
            frame_field = new IntegerField("Frame") { value = 0 };
            host_field = new TextField("Host") { value = EditorPrefs.GetString(HostPref, "127.0.0.1") };
            port_field = new IntegerField("Port") { value = EditorPrefs.GetInt(PortPref, 39541) };

            var send_button = new Button(SendSnapshot) { text = "Send 1 Frame Pose" };
            send_button.style.marginTop = 8;
            status_label = new Label("Blender側でPose Receiverを開始してから送信してください。");
            status_label.style.whiteSpace = WhiteSpace.Normal;
            status_label.style.marginTop = 6;

            root.Add(animator_field);
            root.Add(clip_field);
            root.Add(frame_field);
            root.Add(host_field);
            root.Add(port_field);
            root.Add(send_button);
            root.Add(status_label);
        }

        private void SendSnapshot()
        {
            var animator = animator_field.value as Animator;
            var clip = clip_field.value as AnimationClip;
            if (animator == null || clip == null)
            {
                SetStatus("AnimatorとAnimation Clipを指定してください。", true);
                return;
            }
            if (!animator.isHuman)
            {
                SetStatus("Humanoid Animatorを指定してください。", true);
                return;
            }
            if (AnimationMode.InAnimationMode())
            {
                SetStatus("AnimationウィンドウのPreviewを停止してから送信してください。", true);
                return;
            }

            var port = Mathf.Clamp(port_field.value, 1, 65535);
            EditorPrefs.SetString(HostPref, host_field.value);
            EditorPrefs.SetInt(PortPref, port);

            try
            {
                var snapshot = CaptureSnapshot(animator, clip, Mathf.Max(0, frame_field.value));
                SendSnapshotMessages(host_field.value, port, snapshot);
                SetStatus($"送信完了: {clip.name} / frame {snapshot.frame.frame} / " +
                          $"{snapshot.bone_count} bones (schema v3)", false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus($"送信失敗: {exception.Message}", true);
            }
        }

        private static SnapshotPayload CaptureSnapshot(Animator animator, AnimationClip clip, int frame)
        {
            var root = animator.transform;
            var caches = CollectHumanoidBones(animator, root);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var sample_time = Mathf.Clamp(frame / Mathf.Max(1.0f, clip.frameRate), 0.0f, clip.length);
            var avatar_name = animator.gameObject.name;

            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, sample_time);
                AnimationMode.EndSampling();

                var targets = new string[caches.Count];
                var human_bones = new string[caches.Count];
                var rest_rotations = new float[caches.Count * 4];
                var current_rotations = new float[caches.Count * 4];
                var inverse_root = Quaternion.Inverse(root.rotation);
                for (var i = 0; i < caches.Count; i++)
                {
                    var cache = caches[i];
                    targets[i] = cache.transform.name;
                    human_bones[i] = cache.human_bone;
                    WriteQuaternion(rest_rotations, i * 4, cache.rest_rotation);
                    WriteQuaternion(current_rotations, i * 4, inverse_root * cache.transform.rotation);
                }

                return new SnapshotPayload
                {
                    definitions = new AvatarDefsMessage
                    {
                        schema = "unity_blender_pose_sync.world",
                        version = 3,
                        message_type = "avatar_defs",
                        avatars = new[]
                        {
                            new AvatarDefEntry
                            {
                                avatar_name = avatar_name,
                                targets = targets,
                                human_bones = human_bones,
                                rest_rotations = ToBytes(rest_rotations),
                            },
                        },
                    },
                    frame = new PoseFrameV3
                    {
                        schema = "unity_blender_pose_sync.world",
                        version = 3,
                        message_type = "pose_snapshot",
                        frame = frame,
                        unity_time = sample_time,
                        avatars = new[]
                        {
                            new AvatarPoseEntry
                            {
                                avatar_name = avatar_name,
                                root = TransformToBytes(root),
                                hips = TransformToBytes(hips != null ? hips : root),
                                rotations = ToBytes(current_rotations),
                            },
                        },
                        camera = CaptureConfiguredCamera(),
                    },
                    bone_count = caches.Count,
                };
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                {
                    AnimationMode.StopAnimationMode();
                }
            }
        }

        private static List<SnapshotBone> CollectHumanoidBones(Animator animator, Transform root)
        {
            var result = new List<SnapshotBone>();
            for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var human_bone = (HumanBodyBones)i;
                var transform = animator.GetBoneTransform(human_bone);
                if (transform == null)
                {
                    continue;
                }
                result.Add(new SnapshotBone(
                    transform,
                    human_bone.ToString(),
                    Quaternion.Inverse(root.rotation) * transform.rotation));
            }
            return result;
        }

        private static void WriteQuaternion(float[] values, int offset, Quaternion rotation)
        {
            values[offset] = rotation.x;
            values[offset + 1] = rotation.y;
            values[offset + 2] = rotation.z;
            values[offset + 3] = rotation.w;
        }

        private static byte[] TransformToBytes(Transform transform)
        {
            var position = transform.position;
            var rotation = transform.rotation;
            return ToBytes(new[]
            {
                position.x, position.y, position.z,
                rotation.x, rotation.y, rotation.z, rotation.w,
            });
        }

        private static CameraPoseV3 CaptureConfiguredCamera()
        {
            var settings = PoseSyncSettingsStore.Load();
            if (!settings.send_camera)
            {
                return null;
            }

            Camera camera;
            var camera_name = "";
            SceneView scene_view = null;
            if (settings.camera_source == PoseSyncCameraSource.SceneView)
            {
                scene_view = SceneView.lastActiveSceneView;
                camera = scene_view != null ? scene_view.camera : null;
                camera_name = "Unity Scene View";
            }
            else
            {
                camera = PoseSyncSettingsStore.ResolveCamera(settings);
            }
            if (camera == null)
            {
                return null;
            }

            var message = new CameraPoseV3
            {
                camera_name = string.IsNullOrEmpty(camera_name) ? camera.name : camera_name,
                transform = TransformToBytes(camera.transform),
                orthographic = camera.orthographic,
                field_of_view = camera.fieldOfView,
                orthographic_size = camera.orthographicSize,
                aspect = camera.aspect,
                near_clip = camera.nearClipPlane,
                far_clip = camera.farClipPlane,
            };
            if (scene_view != null)
            {
                var pivot = scene_view.pivot;
                message.scene_view = true;
                message.view_pivot = ToBytes(new[] { pivot.x, pivot.y, pivot.z });
                message.view_distance = Vector3.Distance(camera.transform.position, pivot);
                message.view_size = scene_view.size;
            }
            return message;
        }

        private static byte[] ToBytes(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static void SendSnapshotMessages(string host, int port, SnapshotPayload snapshot)
        {
            var definitions = PoseSyncMessagePack.Serialize(snapshot.definitions);
            var frame = PoseSyncMessagePack.Serialize(snapshot.frame);
            using (var client = new TcpClient { NoDelay = true, SendTimeout = 2000 })
            {
                client.Connect(host, port);
                using (var stream = client.GetStream())
                {
                    WritePacket(stream, definitions, PoseSyncManager.PacketKindControl);
                    WritePacket(stream, frame, PoseSyncManager.PacketKindPose);
                    stream.Flush();
                }
            }
        }

        private static void WritePacket(NetworkStream stream, byte[] payload, byte kind)
        {
            var packet_size = payload.Length + 1;
            var header = new[]
            {
                (byte)(packet_size >> 24),
                (byte)(packet_size >> 16),
                (byte)(packet_size >> 8),
                (byte)packet_size,
            };
            stream.Write(header, 0, header.Length);
            stream.WriteByte(kind);
            stream.Write(payload, 0, payload.Length);
        }

        private void SetStatus(string message, bool is_error)
        {
            status_label.text = message;
            status_label.style.color = is_error
                ? new StyleColor(new Color(1.0f, 0.45f, 0.4f))
                : new StyleColor(new Color(0.55f, 0.9f, 0.55f));
        }

        private readonly struct SnapshotBone
        {
            public SnapshotBone(Transform transform, string human_bone, Quaternion rest_rotation)
            {
                this.transform = transform;
                this.human_bone = human_bone;
                this.rest_rotation = rest_rotation;
            }

            public readonly Transform transform;
            public readonly string human_bone;
            public readonly Quaternion rest_rotation;
        }

        private sealed class SnapshotPayload
        {
            public AvatarDefsMessage definitions;
            public PoseFrameV3 frame;
            public int bone_count;
        }
    }
}
