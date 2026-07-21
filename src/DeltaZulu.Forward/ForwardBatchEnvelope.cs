namespace DeltaZulu.Forward;

/// <summary>
/// The payload of a <see cref="ForwardFrameType.TypedBatch" /> or
/// <see cref="ForwardFrameType.RawEnvelope" /> frame: a batch UUID (the unit of
/// deduplication and acknowledgement) followed by the opaque batch bytes. One Avro batch per
/// frame; the batch is never split across frames and a frame never carries more than one
/// independently committable batch.
/// </summary>
public sealed record ForwardBatchEnvelope(Guid BatchId, byte[] Payload)
{
    /// <summary>Encodes the batch envelope to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteGuid(BatchId);
        writer.WriteRawBytes(Payload);
        return writer.ToArray();
    }

    /// <summary>Decodes a batch envelope from its wire representation.</summary>
    public static ForwardBatchEnvelope Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var batchId = reader.ReadGuid();
        var body = reader.ReadRemainingBytes();
        return new ForwardBatchEnvelope(batchId, body);
    }
}

/// <summary>The payload of a <see cref="ForwardFrameType.SchemaRequest" /> frame.</summary>
public sealed record ForwardSchemaRequest(ulong SchemaFingerprint)
{
    /// <summary>Encodes the schema request to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteUInt64(SchemaFingerprint);
        return writer.ToArray();
    }

    /// <summary>Decodes a schema request from its wire representation.</summary>
    public static ForwardSchemaRequest Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        return new ForwardSchemaRequest(reader.ReadUInt64());
    }
}

/// <summary>The payload of a <see cref="ForwardFrameType.SchemaResponse" /> frame.</summary>
public sealed record ForwardSchemaResponse(ulong SchemaFingerprint, bool Found, byte[] SchemaBytes)
{
    /// <summary>Encodes the schema response to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteUInt64(SchemaFingerprint);
        writer.WriteBool(Found);
        writer.WriteRawBytes(SchemaBytes);
        return writer.ToArray();
    }

    /// <summary>Decodes a schema response from its wire representation.</summary>
    public static ForwardSchemaResponse Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var fingerprint = reader.ReadUInt64();
        var found = reader.ReadBool();
        var schemaBytes = reader.ReadRemainingBytes();
        return new ForwardSchemaResponse(fingerprint, found, schemaBytes);
    }
}

/// <summary>
/// The payload of a <see cref="ForwardFrameType.DeadLetterForward" /> frame: the original
/// batch identifier, the reason it failed parsing or validation, and its original bytes.
/// </summary>
public sealed record ForwardDeadLetter(Guid OriginalBatchId, string Reason, byte[] OriginalPayload)
{
    /// <summary>Encodes the dead-letter record to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteGuid(OriginalBatchId);
        writer.WriteString(Reason);
        writer.WriteRawBytes(OriginalPayload);
        return writer.ToArray();
    }

    /// <summary>Decodes a dead-letter record from its wire representation.</summary>
    public static ForwardDeadLetter Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var originalBatchId = reader.ReadGuid();
        var reason = reader.ReadString();
        var originalPayload = reader.ReadRemainingBytes();
        return new ForwardDeadLetter(originalBatchId, reason, originalPayload);
    }
}

/// <summary>Control frame subtypes: window adjustment and throttle signaling for backpressure.</summary>
public enum ForwardControlType : byte
{
    /// <summary>Sets a new negotiated in-flight window capacity.</summary>
    WindowAdjust = 0,

    /// <summary>Requests the peer pause (or resume, with a zero delay) sending for a duration.</summary>
    Throttle = 1
}

/// <summary>The payload of a <see cref="ForwardFrameType.Control" /> frame.</summary>
public sealed record ForwardControlMessage(ForwardControlType ControlType, uint Value)
{
    /// <summary>Encodes the control message to its wire representation.</summary>
    public byte[] Encode()
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteByte((byte)ControlType);
        writer.WriteUInt32(Value);
        return writer.ToArray();
    }

    /// <summary>Decodes a control message from its wire representation.</summary>
    public static ForwardControlMessage Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var controlType = (ForwardControlType)reader.ReadByte();
        var value = reader.ReadUInt32();
        return new ForwardControlMessage(controlType, value);
    }
}

/// <summary>Codec for the <see cref="ForwardFrameType.Ack" /> frame payload.</summary>
public static class ForwardAckCodec
{
    /// <summary>Encodes an acknowledgement outcome to its wire representation.</summary>
    public static byte[] Encode(ForwardAckOutcome outcome)
    {
        var writer = new ForwardPayloadWriter();
        writer.WriteByte(outcome.StatusCode);
        writer.WriteString(outcome.Detail ?? string.Empty);
        return writer.ToArray();
    }

    /// <summary>Decodes an acknowledgement outcome from its wire representation.</summary>
    public static ForwardAckOutcome Decode(ReadOnlySpan<byte> payload)
    {
        var reader = new ForwardPayloadReader(payload);
        var statusCode = reader.ReadByte();
        var detail = reader.ReadString();
        return new ForwardAckOutcome(statusCode, detail.Length == 0 ? null : detail);
    }
}
