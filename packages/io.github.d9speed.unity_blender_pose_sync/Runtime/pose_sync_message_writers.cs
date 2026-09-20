using System;
using System.IO;

namespace UnityBlenderPoseSync.World
{
    public static partial class PoseSyncMessagePack
    {
        public static byte[] Serialize(PoseSyncCommand value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(AvatarListMessage value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(AvatarDefsMessage value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(AvatarDefEntry value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(PoseFrameV3 value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(CameraPoseV3 value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(AvatarPoseEntry value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(PoseStateV3 value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldPoseFrameMessage value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldPoseStateMessage value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldBoneMessage value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldTransformMsg value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldVec3 value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        public static byte[] Serialize(WorldQuat value)
        {
            using (var stream = new MemoryStream())
            {
                var writer = new Writer(stream);
                writer.Value(value);
                return stream.ToArray();
            }
        }
        private sealed partial class Writer
        {
            public void Value(PoseSyncCommand value)
            {
                if (value == null) { Nil(); return; }
                Map(3);
                Value("message_type"); Value(value.message_type);
                Value("command"); Value(value.command);
                Value("frames"); Value(value.frames);
            }
            public void Value(AvatarListMessage value)
            {
                if (value == null) { Nil(); return; }
                Map(4);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("avatars"); Value(value.avatars);
            }
            public void Value(AvatarDefsMessage value)
            {
                if (value == null) { Nil(); return; }
                Map(4);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("avatars"); Value(value.avatars);
            }
            public void Value(AvatarDefEntry value)
            {
                if (value == null) { Nil(); return; }
                Map(4);
                Value("avatar_name"); Value(value.avatar_name);
                Value("targets"); Value(value.targets);
                Value("human_bones"); Value(value.human_bones);
                Value("rest_rotations"); Value(value.rest_rotations);
            }
            public void Value(PoseFrameV3 value)
            {
                if (value == null) { Nil(); return; }
                Map(7);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("frame"); Value(value.frame);
                Value("unity_time"); Value(value.unity_time);
                Value("avatars"); Value(value.avatars);
                Value("camera"); Value(value.camera);
            }
            public void Value(CameraPoseV3 value)
            {
                if (value == null) { Nil(); return; }
                Map(12);
                Value("camera_name"); Value(value.camera_name);
                Value("transform"); Value(value.transform);
                Value("orthographic"); Value(value.orthographic);
                Value("field_of_view"); Value(value.field_of_view);
                Value("orthographic_size"); Value(value.orthographic_size);
                Value("aspect"); Value(value.aspect);
                Value("near_clip"); Value(value.near_clip);
                Value("far_clip"); Value(value.far_clip);
                Value("scene_view"); Value(value.scene_view);
                Value("view_pivot"); Value(value.view_pivot);
                Value("view_distance"); Value(value.view_distance);
                Value("view_size"); Value(value.view_size);
            }
            public void Value(AvatarPoseEntry value)
            {
                if (value == null) { Nil(); return; }
                Map(4);
                Value("avatar_name"); Value(value.avatar_name);
                Value("root"); Value(value.root);
                Value("hips"); Value(value.hips);
                Value("rotations"); Value(value.rotations);
            }
            public void Value(PoseStateV3 value)
            {
                if (value == null) { Nil(); return; }
                Map(5);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("state"); Value(value.state);
                Value("frame"); Value(value.frame);
            }
            public void Value(WorldPoseFrameMessage value)
            {
                if (value == null) { Nil(); return; }
                Map(10);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("avatar_name"); Value(value.avatar_name);
                Value("frame"); Value(value.frame);
                Value("unity_time"); Value(value.unity_time);
                Value("send_position"); Value(value.send_position);
                Value("root_world"); Value(value.root_world);
                Value("hips_world"); Value(value.hips_world);
                Value("bones"); Value(value.bones);
            }
            public void Value(WorldPoseStateMessage value)
            {
                if (value == null) { Nil(); return; }
                Map(7);
                Value("schema"); Value(value.schema);
                Value("version"); Value(value.version);
                Value("message_type"); Value(value.message_type);
                Value("state"); Value(value.state);
                Value("avatar_name"); Value(value.avatar_name);
                Value("frame"); Value(value.frame);
                Value("unity_time"); Value(value.unity_time);
            }
            public void Value(WorldBoneMessage value)
            {
                if (value == null) { Nil(); return; }
                Map(7);
                Value("key"); Value(value.key);
                Value("target"); Value(value.target);
                Value("human_bone"); Value(value.human_bone);
                Value("rest_rotation"); Value(value.rest_rotation);
                Value("rotation"); Value(value.rotation);
                Value("rest_position"); Value(value.rest_position);
                Value("position"); Value(value.position);
            }
            public void Value(WorldTransformMsg value)
            {
                if (value == null) { Nil(); return; }
                Map(2);
                Value("position"); Value(value.position);
                Value("rotation"); Value(value.rotation);
            }
            public void Value(WorldVec3 value)
            {
                if (value == null) { Nil(); return; }
                Map(3);
                Value("x"); Value(value.x);
                Value("y"); Value(value.y);
                Value("z"); Value(value.z);
            }
            public void Value(WorldQuat value)
            {
                if (value == null) { Nil(); return; }
                Map(4);
                Value("x"); Value(value.x);
                Value("y"); Value(value.y);
                Value("z"); Value(value.z);
                Value("w"); Value(value.w);
            }
            public void Value(AvatarDefEntry[] values)
            {
                if (values == null) { Nil(); return; }
                ArrayHeader(values.Length);
                foreach (var value in values) Value(value);
            }
            public void Value(AvatarPoseEntry[] values)
            {
                if (values == null) { Nil(); return; }
                ArrayHeader(values.Length);
                foreach (var value in values) Value(value);
            }
            public void Value(WorldBoneMessage[] values)
            {
                if (values == null) { Nil(); return; }
                ArrayHeader(values.Length);
                foreach (var value in values) Value(value);
            }
            public void Value(string[] values)
            {
                if (values == null) { Nil(); return; }
                ArrayHeader(values.Length);
                foreach (var value in values) Value(value);
            }
        }
    }
}
