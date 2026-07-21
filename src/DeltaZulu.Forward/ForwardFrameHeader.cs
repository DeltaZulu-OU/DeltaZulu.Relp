using System.Buffers.Binary;
using System.IO.Hashing;

namespace DeltaZulu.Forward;

/// <summary>
/// The fixed 16-byte binary header that precedes every DeltaZulu.Forward frame payload:
/// frame type, flags, protocol version, transaction number, payload length, and a CRC-32
/// integrity checksum of the payload. Replaces RELP's space-separated ASCII header grammar
/// with a binary-safe, fixed-width layout that carries no text command verbs.
/// </summary>
public readonly struct ForwardFrameHeader : IEquatable<ForwardFrameHeader>
{
    /// <summary>The fixed encoded length of a DeltaZulu.Forward frame header, in bytes.</summary>
    public const int EncodedLength = 16;

    /// <summary>The DeltaZulu.Forward protocol version implemented by this library.</summary>
    public const ushort CurrentProtocolVersion = 1;

    /// <summary>Initializes a new frame header.</summary>
    public ForwardFrameHeader(ForwardFrameType frameType, ForwardFrameFlags flags, ushort protocolVersion, uint transactionNumber, uint payloadLength, uint payloadChecksum)
    {
        FrameType = frameType;
        Flags = flags;
        ProtocolVersion = protocolVersion;
        TransactionNumber = transactionNumber;
        PayloadLength = payloadLength;
        PayloadChecksum = payloadChecksum;
    }

    /// <summary>Gets the frame type.</summary>
    public ForwardFrameType FrameType { get; }

    /// <summary>Gets the frame flags.</summary>
    public ForwardFrameFlags Flags { get; }

    /// <summary>Gets the wire protocol version the frame was written with.</summary>
    public ushort ProtocolVersion { get; }

    /// <summary>Gets the per-frame transaction number.</summary>
    public uint TransactionNumber { get; }

    /// <summary>Gets the declared payload length, in bytes.</summary>
    public uint PayloadLength { get; }

    /// <summary>Gets the CRC-32 checksum of the payload (zero when <see cref="PayloadLength" /> is zero).</summary>
    public uint PayloadChecksum { get; }

    /// <summary>Computes the CRC-32 checksum DeltaZulu.Forward uses for frame-payload integrity.</summary>
    public static uint ComputeChecksum(ReadOnlySpan<byte> payload) =>
        payload.IsEmpty ? 0 : Crc32.HashToUInt32(payload);

    /// <summary>Encodes the header into a 16-byte destination span.</summary>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            throw new ArgumentException($"Destination must be at least {EncodedLength} bytes.", nameof(destination));
        }

        destination[0] = (byte)FrameType;
        destination[1] = (byte)Flags;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..4], ProtocolVersion);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..8], TransactionNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], PayloadLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..16], PayloadChecksum);
    }

    /// <summary>Decodes a header from a 16-byte source span.</summary>
    public static ForwardFrameHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < EncodedLength)
        {
            throw new ArgumentException($"Source must be at least {EncodedLength} bytes.", nameof(source));
        }

        var frameType = (ForwardFrameType)source[0];
        var flags = (ForwardFrameFlags)source[1];
        var protocolVersion = BinaryPrimitives.ReadUInt16BigEndian(source[2..4]);
        var transactionNumber = BinaryPrimitives.ReadUInt32BigEndian(source[4..8]);
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);
        var payloadChecksum = BinaryPrimitives.ReadUInt32BigEndian(source[12..16]);
        return new ForwardFrameHeader(frameType, flags, protocolVersion, transactionNumber, payloadLength, payloadChecksum);
    }

    /// <inheritdoc />
    public bool Equals(ForwardFrameHeader other) =>
        FrameType == other.FrameType &&
        Flags == other.Flags &&
        ProtocolVersion == other.ProtocolVersion &&
        TransactionNumber == other.TransactionNumber &&
        PayloadLength == other.PayloadLength &&
        PayloadChecksum == other.PayloadChecksum;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ForwardFrameHeader other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FrameType, Flags, ProtocolVersion, TransactionNumber, PayloadLength, PayloadChecksum);

    /// <summary>Determines whether two headers are equal.</summary>
    public static bool operator ==(ForwardFrameHeader left, ForwardFrameHeader right) => left.Equals(right);

    /// <summary>Determines whether two headers are not equal.</summary>
    public static bool operator !=(ForwardFrameHeader left, ForwardFrameHeader right) => !left.Equals(right);
}
