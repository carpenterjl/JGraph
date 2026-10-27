using System.Buffers.Binary;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Protocol Buffers' wire format (compiler and SDK plan, stage K16), as much of it as MATLAB
/// Production Server's messages use: varints, fixed 32-bit and 64-bit fields, length-delimited
/// bytes, strings and nested messages, and packed repeated scalars. A reader takes a field in any
/// order, packed or not, and skips the fields it does not know; a writer puts fields in the order
/// it is given them. No generated code and no library: the schema is small and known
/// (<see cref="JgsMpsProtobuf"/>).
/// </summary>
internal static class JgsProtobufWire
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int Delimited = 2;
    public const int Fixed32 = 5;

    /// <summary>Bytes that are not a message: a tag or length cut short, a wire type no message has, a group.</summary>
    internal sealed class Malformed(string message) : Exception(message);

    /// <summary>Writes fields into a growing buffer; a nested message is written into its own writer and appended with its length.</summary>
    internal sealed class Writer
    {
        private byte[] _bytes = new byte[256];
        private int _length;

        public int Length => _length;

        public ReadOnlySpan<byte> Span => _bytes.AsSpan(0, _length);

        public byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

        public void Tag(int field, int wireType) => WriteVarint(((ulong)field << 3) | (uint)wireType);

        public void VarintField(int field, ulong value)
        {
            Tag(field, Varint);
            WriteVarint(value);
        }

        public void VarintField(int field, long value) => VarintField(field, unchecked((ulong)value));

        public void BoolField(int field, bool value) => VarintField(field, value ? 1UL : 0UL);

        public void Fixed32Field(int field, uint value)
        {
            Tag(field, Fixed32);
            WriteFixed32(value);
        }

        public void Fixed64Field(int field, ulong value)
        {
            Tag(field, Fixed64);
            WriteFixed64(value);
        }

        public void DoubleField(int field, double value) => Fixed64Field(field, BitConverter.DoubleToUInt64Bits(value));

        public void FloatField(int field, float value) => Fixed32Field(field, BitConverter.SingleToUInt32Bits(value));

        public void BytesField(int field, ReadOnlySpan<byte> value)
        {
            Tag(field, Delimited);
            WriteVarint((ulong)value.Length);
            Append(value);
        }

        public void StringField(int field, string value) => BytesField(field, Encoding.UTF8.GetBytes(value));

        public void MessageField(int field, Writer message) => BytesField(field, message.Span);

        /// <summary>A packed repeated field: the scalars' bytes under one tag and length (written even when empty if <paramref name="always"/>).</summary>
        public void PackedField(int field, Writer scalars, bool always = false)
        {
            if (scalars.Length > 0 || always)
            {
                BytesField(field, scalars.Span);
            }
        }

        public void WriteVarint(ulong value)
        {
            while (value >= 0x80)
            {
                Byte((byte)(value | 0x80));
                value >>= 7;
            }

            Byte((byte)value);
        }

        public void WriteVarint(long value) => WriteVarint(unchecked((ulong)value));

        public void WriteBool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void WriteFixed32(uint value)
        {
            Ensure(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_length), value);
            _length += 4;
        }

        public void WriteFixed64(ulong value)
        {
            Ensure(8);
            BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(_length), value);
            _length += 8;
        }

        public void WriteDouble(double value) => WriteFixed64(BitConverter.DoubleToUInt64Bits(value));

        public void WriteFloat(float value) => WriteFixed32(BitConverter.SingleToUInt32Bits(value));

        public void Append(ReadOnlySpan<byte> bytes)
        {
            Ensure(bytes.Length);
            bytes.CopyTo(_bytes.AsSpan(_length));
            _length += bytes.Length;
        }

        private void Byte(byte b)
        {
            Ensure(1);
            _bytes[_length++] = b;
        }

        private void Ensure(int more)
        {
            if (_length + more > _bytes.Length)
            {
                Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _length + more));
            }
        }
    }

    /// <summary>Reads one message's fields in order; a nested message is read by a reader over its bytes.</summary>
    internal ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _at;

        public readonly bool AtEnd => _at >= _bytes.Length;

        /// <summary>The next field's number and wire type; false at the end.</summary>
        public bool Next(out int field, out int wireType)
        {
            if (AtEnd)
            {
                field = 0;
                wireType = 0;
                return false;
            }

            ulong tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 7);
            if (tag == 0)
            {
                // A zero byte where a tag should be ends the message, as muserve's reader takes it (k16_server: body.trailing).
                _at = _bytes.Length;
                return false;
            }

            if (field == 0 || wireType is 3 or 4 or 6 or 7)
            {
                throw new Malformed($"A tag of field {field} and wire type {wireType} is not one.");
            }

            return true;
        }

        public ulong ReadVarint()
        {
            ulong value = 0;
            int shift = 0;
            while (true)
            {
                if (_at >= _bytes.Length)
                {
                    throw new Malformed("The message ends inside a varint.");
                }

                byte b = _bytes[_at++];
                if (shift < 64)
                {
                    value |= (ulong)(b & 0x7F) << shift;
                }

                if ((b & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
                if (shift > 70)
                {
                    throw new Malformed("A varint runs past ten bytes.");
                }
            }
        }

        public uint ReadFixed32()
        {
            if (_at + 4 > _bytes.Length)
            {
                throw new Malformed("The message ends inside a fixed32.");
            }

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.Slice(_at));
            _at += 4;
            return value;
        }

        public ulong ReadFixed64()
        {
            if (_at + 8 > _bytes.Length)
            {
                throw new Malformed("The message ends inside a fixed64.");
            }

            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_bytes.Slice(_at));
            _at += 8;
            return value;
        }

        public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadFixed64());

        public float ReadFloat() => BitConverter.UInt32BitsToSingle(ReadFixed32());

        public bool ReadBool() => ReadVarint() != 0;

        public ReadOnlySpan<byte> ReadBytes()
        {
            ulong length = ReadVarint();
            if (length > (ulong)(_bytes.Length - _at))
            {
                throw new Malformed("A length runs past the message's end.");
            }

            ReadOnlySpan<byte> slice = _bytes.Slice(_at, (int)length);
            _at += (int)length;
            return slice;
        }

        public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

        /// <summary>Passes over a field's value by its wire type.</summary>
        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case Varint:
                    ReadVarint();
                    break;
                case Fixed64:
                    ReadFixed64();
                    break;
                case Delimited:
                    ReadBytes();
                    break;
                case Fixed32:
                    ReadFixed32();
                    break;
                default:
                    throw new Malformed($"Wire type {wireType} cannot be skipped.");
            }
        }

        // --- repeated scalars, packed (one length-delimited run) or not (one value per tag) ---

        public void Varints(int wireType, List<ulong> into)
        {
            if (wireType == Delimited)
            {
                var packed = new Reader(ReadBytes());
                while (!packed.AtEnd)
                {
                    into.Add(packed.ReadVarint());
                }
            }
            else if (wireType == Varint)
            {
                into.Add(ReadVarint());
            }
            else
            {
                throw new Malformed($"Wire type {wireType} is not a varint field's.");
            }
        }

        public void Fixed32s(int wireType, List<uint> into)
        {
            if (wireType == Delimited)
            {
                var packed = new Reader(ReadBytes());
                while (!packed.AtEnd)
                {
                    into.Add(packed.ReadFixed32());
                }
            }
            else if (wireType == Fixed32)
            {
                into.Add(ReadFixed32());
            }
            else
            {
                throw new Malformed($"Wire type {wireType} is not a fixed32 field's.");
            }
        }

        public void Fixed64s(int wireType, List<ulong> into)
        {
            if (wireType == Delimited)
            {
                var packed = new Reader(ReadBytes());
                while (!packed.AtEnd)
                {
                    into.Add(packed.ReadFixed64());
                }
            }
            else if (wireType == Fixed64)
            {
                into.Add(ReadFixed64());
            }
            else
            {
                throw new Malformed($"Wire type {wireType} is not a fixed64 field's.");
            }
        }
    }
}
