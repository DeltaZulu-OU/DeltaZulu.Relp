namespace DeltaZulu.Forward;

/// <summary>DeltaZulu.Forward frame type codes carried in the frame header.</summary>
public enum ForwardFrameType : byte
{
    /// <summary>Client-to-server handshake offer: protocol version, catalog version, schema fingerprints, compression, window sizing, session resumption.</summary>
    Hello = 0,

    /// <summary>Server-to-client handshake response: negotiated parameters, granted windows, and any unrecognized schema fingerprints.</summary>
    HelloAck = 1,

    /// <summary>Carries one MessagePack-encoded <c>ForwardLogBatch</c>, identified by a batch UUID for deduplication.</summary>
    TypedBatch = 2,

    /// <summary>Carries one raw-envelope batch (bytes plus source metadata) for a source parsed at the collector tier.</summary>
    RawEnvelope = 3,

    /// <summary>Requests the schema bytes for a schema fingerprint the receiver has not seen before.</summary>
    SchemaRequest = 4,

    /// <summary>Returns the schema bytes for a previously requested fingerprint.</summary>
    SchemaResponse = 5,

    /// <summary>Forwards a batch that failed parsing or validation, with its original bytes and an error reason.</summary>
    DeadLetterForward = 6,

    /// <summary>Application-level acknowledgement bound to durable commit of the batch identified by the frame header's transaction number.</summary>
    Ack = 7,

    /// <summary>Carries session control signaling: window adjustment or throttle (backpressure).</summary>
    Control = 8,

    /// <summary>Requests an orderly session shutdown.</summary>
    Close = 9,

    /// <summary>Acknowledges an orderly session shutdown.</summary>
    CloseAck = 10
}

/// <summary>Bit flags carried in the frame header.</summary>
[Flags]
public enum ForwardFrameFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>The payload is compressed with the session's negotiated compression algorithm.</summary>
    Compressed = 1 << 0
}
