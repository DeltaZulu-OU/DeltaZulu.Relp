namespace DeltaZulu.Forward;

/// <summary>A received DeltaZulu.Forward frame, already validated against its header's CRC-32 checksum.</summary>
public sealed class ForwardFrameRx
{
    private readonly byte[] _payload;

    /// <summary>Initializes a received frame.</summary>
    public ForwardFrameRx(uint transactionNumber, ForwardFrameType frameType, ForwardFrameFlags flags, ushort protocolVersion, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (transactionNumber < TxNr.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionNumber), $"Transaction number must be at least {TxNr.MinValue}.");
        }

        TransactionNumber = transactionNumber;
        FrameType = frameType;
        Flags = flags;
        ProtocolVersion = protocolVersion;
        _payload = payload.ToArray();
    }

    /// <summary>Gets the frame type.</summary>
    public ForwardFrameType FrameType { get; }

    /// <summary>Gets the frame flags.</summary>
    public ForwardFrameFlags Flags { get; }

    /// <summary>Gets a copy of the frame payload.</summary>
    public byte[] Payload => _payload.ToArray();

    /// <summary>Gets the wire protocol version the frame was written with.</summary>
    public ushort ProtocolVersion { get; }

    /// <summary>Gets the frame's transaction number.</summary>
    public uint TransactionNumber { get; }
}
