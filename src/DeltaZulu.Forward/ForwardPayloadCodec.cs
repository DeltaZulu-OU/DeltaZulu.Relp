using System.Buffers.Binary;
using System.Text;

namespace DeltaZulu.Forward;

/// <summary>A small append-only binary writer used to encode DeltaZulu.Forward frame payloads.</summary>
internal sealed class ForwardPayloadWriter
{
    private readonly List<byte> _buffer = [];

    public void WriteByte(byte value) => _buffer.Add(value);

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Span<byte> span = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        _buffer.AddRange(span.ToArray());
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        _buffer.AddRange(span.ToArray());
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        _buffer.AddRange(span.ToArray());
    }

    public void WriteGuid(Guid value) => _buffer.AddRange(value.ToByteArray());

    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Encoded string must not exceed 65535 bytes.", nameof(value));
        }

        WriteUInt16((ushort)bytes.Length);
        _buffer.AddRange(bytes);
    }

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32((uint)value.Length);
        _buffer.AddRange(value.ToArray());
    }

    public void WriteRawBytes(ReadOnlySpan<byte> value) => _buffer.AddRange(value.ToArray());

    public byte[] ToArray() => [.. _buffer];
}

/// <summary>A small forward-only binary reader used to decode DeltaZulu.Forward frame payloads.</summary>
internal ref struct ForwardPayloadReader(ReadOnlySpan<byte> source)
{
    private readonly ReadOnlySpan<byte> _source = source;
    private int _position;

    public readonly int Remaining => _source.Length - _position;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _source[_position++];
    }

    public bool ReadBool() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(_source[_position..]);
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadUInt32BigEndian(_source[_position..]);
        _position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadUInt64BigEndian(_source[_position..]);
        _position += 8;
        return value;
    }

    public Guid ReadGuid()
    {
        EnsureAvailable(16);
        var value = new Guid(_source.Slice(_position, 16));
        _position += 16;
        return value;
    }

    public string ReadString()
    {
        var length = ReadUInt16();
        EnsureAvailable(length);
        var value = Encoding.UTF8.GetString(_source.Slice(_position, length));
        _position += length;
        return value;
    }

    public byte[] ReadLengthPrefixedBytes()
    {
        var length = ReadUInt32();
        if (length > int.MaxValue)
        {
            throw new FormatException("Length-prefixed byte field exceeds the supported size.");
        }

        EnsureAvailable((int)length);
        var value = _source.Slice(_position, (int)length).ToArray();
        _position += (int)length;
        return value;
    }

    public byte[] ReadRemainingBytes()
    {
        var value = _source[_position..].ToArray();
        _position = _source.Length;
        return value;
    }

    private readonly void EnsureAvailable(int count)
    {
        if (Remaining < count)
        {
            throw new FormatException("DeltaZulu.Forward payload ended before an expected field.");
        }
    }
}
