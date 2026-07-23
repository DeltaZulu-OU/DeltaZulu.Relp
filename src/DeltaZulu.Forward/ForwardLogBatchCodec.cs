using MessagePack;

namespace DeltaZulu.Forward;

/// <summary>
/// The only public surface for converting a <see cref="ForwardLogBatch" /> to and from its
/// MessagePack wire representation (<c>FWD-CONTRACT-v1</c> §3). The encoded bytes are the
/// payload of a <see cref="ForwardFrameType.TypedBatch" /> frame, carried inside (not in
/// place of) the outer <see cref="ForwardBatchEnvelope" />.
/// </summary>
public static class ForwardLogBatchCodec
{
    /// <summary>Normalizes and encodes a batch to its MessagePack wire representation.</summary>
    public static byte[] Encode(ForwardLogBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var normalized = ForwardValueNormalizer.NormalizeBatch(batch);
        return MessagePackSerializer.Serialize(normalized, ForwardMessagePackOptions.Instance);
    }

    /// <summary>Decodes a batch from its MessagePack wire representation.</summary>
    public static ForwardLogBatch Decode(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<ForwardLogBatch>(payload, ForwardMessagePackOptions.Instance);
}
