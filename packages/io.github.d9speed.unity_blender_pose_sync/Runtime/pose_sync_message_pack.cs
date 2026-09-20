using System;
using System.IO;
using System.Text;

namespace UnityBlenderPoseSync.World
{
    /// <summary>
    /// MessagePack for Pose Sync's fixed v2/v3 wire schema, not a general serializer.
    /// Explicit writers keep existing Blender receivers compatible without DLLs,
    /// reflection, generated-at-runtime code, or global MessagePack configuration.
    /// </summary>
    public static partial class PoseSyncMessagePack
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static PoseSyncCommand DeserializeCommand(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 1 || count > 4096 || offset > bytes.Length - count)
                throw new InvalidDataException("Invalid Pose Sync command size.");
            var reader = new CommandReader(bytes, offset, count);
            var result = new PoseSyncCommand();
            var fields = reader.Map();
            if (fields > 16) throw new InvalidDataException("Too many command fields.");
            var keys = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < fields; i++)
            {
                var key = reader.String();
                if (!keys.Add(key)) throw new InvalidDataException("Duplicate command field.");
                switch (key)
                {
                    case "message_type": result.message_type = reader.String(); break;
                    case "command": result.command = reader.String(); break;
                    case "frames": result.frames = checked((int)reader.Integer()); break;
                    default: reader.Skip(0); break;
                }
            }
            if (!reader.End || result.message_type != "command"
                || result.frames < 1 || result.frames > 120
                || (result.command != "pause" && result.command != "resume"
                    && result.command != "step" && result.command != "stop"))
                throw new InvalidDataException("Invalid Pose Sync command.");
            return result;
        }

        private sealed partial class Writer
        {
            private readonly Stream stream;
            public Writer(Stream stream) { this.stream = stream; }
            private void Byte(byte value) => stream.WriteByte(value);
            private void Big(ulong value, int size)
            {
                for (var i = size - 1; i >= 0; i--) Byte((byte)(value >> (i * 8)));
            }
            public void Nil() => Byte(0xc0);
            public void Map(int count)
            {
                if (count < 16) Byte((byte)(0x80 | count));
                else { Byte(0xdf); Big((uint)count, 4); }
            }
            public void ArrayHeader(int count)
            {
                if (count < 16) Byte((byte)(0x90 | count));
                else { Byte(0xdd); Big((uint)count, 4); }
            }
            public void Value(string value)
            {
                if (value == null) { Nil(); return; }
                var bytes = Utf8.GetBytes(value);
                if (bytes.Length < 32) Byte((byte)(0xa0 | bytes.Length));
                else { Byte(0xdb); Big((uint)bytes.Length, 4); }
                stream.Write(bytes, 0, bytes.Length);
            }
            public void Value(byte[] value)
            {
                if (value == null) { Nil(); return; }
                Byte(0xc6); Big((uint)value.Length, 4);
                stream.Write(value, 0, value.Length);
            }
            public void Value(bool value) => Byte(value ? (byte)0xc3 : (byte)0xc2);
            public void Value(int value) => Value((long)value);
            public void Value(long value)
            {
                if (value >= -32 && value < 128) Byte(unchecked((byte)value));
                else { Byte(0xd3); Big(unchecked((ulong)value), 8); }
            }
            public void Value(float value)
            {
                Byte(0xca); Big(unchecked((uint)BitConverter.SingleToInt32Bits(value)), 4);
            }
            public void Value(double value)
            {
                Byte(0xcb); Big(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)), 8);
            }
        }

        // Commands are intentionally small. Bounds, nesting, UTF-8, and trailing
        // data are checked before the main thread sees a command.
        private sealed class CommandReader
        {
            private readonly byte[] bytes;
            private readonly int end;
            private int position;
            public CommandReader(byte[] bytes, int offset, int count)
            { this.bytes = bytes; position = offset; end = offset + count; }
            public bool End => position == end;
            private byte Byte()
            {
                if (position >= end) throw new InvalidDataException("Truncated MessagePack.");
                return bytes[position++];
            }
            private ulong Big(int size)
            {
                ulong result = 0;
                for (var i = 0; i < size; i++) result = (result << 8) | Byte();
                return result;
            }
            private void Advance(ulong size)
            {
                if (size > (ulong)(end - position)) throw new InvalidDataException("Truncated MessagePack.");
                position += (int)size;
            }
            public int Map()
            {
                var code = Byte();
                if ((code & 0xf0) == 0x80) return code & 15;
                if (code == 0xde) return (int)Big(2);
                if (code == 0xdf) return checked((int)Big(4));
                throw new InvalidDataException("Expected command map.");
            }
            public string String()
            {
                var code = Byte();
                var size = (code & 0xe0) == 0xa0 ? (ulong)(code & 31)
                    : code == 0xd9 ? Big(1) : code == 0xda ? Big(2) : code == 0xdb ? Big(4)
                    : throw new InvalidDataException("Expected string.");
                var start = position;
                Advance(size);
                return Utf8.GetString(bytes, start, (int)size);
            }
            public long Integer()
            {
                var code = Byte();
                if (code < 0x80) return code;
                if (code >= 0xe0) return (sbyte)code;
                switch (code)
                {
                    case 0xcc: return (long)Big(1);
                    case 0xcd: return (long)Big(2);
                    case 0xce: return (long)Big(4);
                    case 0xcf: return checked((long)Big(8));
                    case 0xd0: return unchecked((sbyte)Big(1));
                    case 0xd1: return unchecked((short)Big(2));
                    case 0xd2: return unchecked((int)Big(4));
                    case 0xd3: return unchecked((long)Big(8));
                    default: throw new InvalidDataException("Expected integer.");
                }
            }
            public void Skip(int depth)
            {
                if (depth > 8) throw new InvalidDataException("MessagePack nesting limit.");
                var code = Byte();
                if (code < 0x80 || code >= 0xe0 || code == 0xc0 || code == 0xc2 || code == 0xc3) return;
                if ((code & 0xe0) == 0xa0) { Advance((uint)(code & 31)); return; }
                ulong children;
                if ((code & 0xf0) == 0x80) children = (uint)(code & 15) * 2u;
                else if ((code & 0xf0) == 0x90) children = (uint)(code & 15);
                else
                {
                    switch (code)
                    {
                        case 0xc4: case 0xd9: Advance(Big(1)); return;
                        case 0xc5: case 0xda: Advance(Big(2)); return;
                        case 0xc6: case 0xdb: Advance(Big(4)); return;
                        case 0xcc: case 0xd0: Advance(1); return;
                        case 0xcd: case 0xd1: Advance(2); return;
                        case 0xce: case 0xd2: case 0xca: Advance(4); return;
                        case 0xcf: case 0xd3: case 0xcb: Advance(8); return;
                        case 0xdc: children = Big(2); break;
                        case 0xdd: children = Big(4); break;
                        case 0xde: children = Big(2) * 2; break;
                        case 0xdf: children = Big(4) * 2; break;
                        default: throw new InvalidDataException("Unsupported command field encoding.");
                    }
                }
                if (children > (ulong)(end - position)) throw new InvalidDataException("Invalid container size.");
                for (ulong i = 0; i < children; i++) Skip(depth + 1);
            }
        }
    }
}
