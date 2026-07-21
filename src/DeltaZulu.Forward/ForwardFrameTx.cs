namespace DeltaZulu.Forward;

/// <summary>A DeltaZulu.Forward frame ready for transmission.</summary>
public sealed class ForwardFrameTx
{
    private readonly byte[] _payload;

    /// <summary>Initializes a frame with the given type, flags, and payload.</summary>
    public ForwardFrameTx(ForwardFrameType frameType, ForwardFrameFlags flags = ForwardFrameFlags.None, byte[]? payload = null)
    {
        FrameType = frameType;
        Flags = flags;
        _payload = payload?.ToArray() ?? [];
    }

    /// <summary>Gets the frame type.</summary>
    public ForwardFrameType FrameType { get; }

    /// <summary>Gets the frame flags.</summary>
    public ForwardFrameFlags Flags { get; }

    /// <summary>Gets a copy of the frame payload.</summary>
    public byte[] Payload => _payload.ToArray();

    /// <summary>Creates a frame with no payload.</summary>
    public static ForwardFrameTx FromFrameType(ForwardFrameType frameType) => new(frameType);

    /// <summary>Creates a frame carrying the given payload.</summary>
    public static ForwardFrameTx FromPayload(ForwardFrameType frameType, byte[] payload) => new(frameType, ForwardFrameFlags.None, payload);

    /// <summary>Encodes the frame (fixed header followed by payload) for the given transaction number.</summary>
    public byte[] ToByteArray(uint transactionNumber = TxNr.MinValue)
    {
        if (transactionNumber < TxNr.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionNumber), $"Transaction number must be at least {TxNr.MinValue}.");
        }

        var header = new ForwardFrameHeader(
            FrameType,
            Flags,
            ForwardFrameHeader.CurrentProtocolVersion,
            transactionNumber,
            (uint)_payload.Length,
            ForwardFrameHeader.ComputeChecksum(_payload));

        var result = new byte[ForwardFrameHeader.EncodedLength + _payload.Length];
        header.Encode(result.AsSpan(0, ForwardFrameHeader.EncodedLength));
        _payload.CopyTo(result.AsSpan(ForwardFrameHeader.EncodedLength));
        return result;
    }
}
