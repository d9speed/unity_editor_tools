using System;
using System.IO;
using MessagePack;
using MessagePack.Resolvers;

namespace UnityBlenderPoseSync.World
{
    [GeneratedMessagePackResolver]
    public partial class PoseSyncGeneratedResolver { }

    /// <summary>Uses the official NuGet serializer; no protocol encoder is implemented here.</summary>
    public static class PoseSyncMessagePack
    {
        // Keep options local to this tool, without changing other packages' defaults.
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(PoseSyncGeneratedResolver.Instance, StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData);

        public static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Options);

        public static PoseSyncCommand DeserializeCommand(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 1 || count > 4096 || offset > bytes.Length - count)
                throw new InvalidDataException("Invalid Pose Sync command size.");
            var reader = new MessagePackReader(new ReadOnlyMemory<byte>(bytes, offset, count));
            var result = MessagePackSerializer.Deserialize<PoseSyncCommand>(ref reader, Options);
            if (!reader.End || result == null || result.message_type != "command"
                || result.frames < 1 || result.frames > 120
                || (result.command != "pause" && result.command != "resume"
                    && result.command != "step" && result.command != "stop"))
                throw new InvalidDataException("Invalid Pose Sync command.");
            return result;
        }
    }
}
