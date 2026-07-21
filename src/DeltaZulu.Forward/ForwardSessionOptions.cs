namespace DeltaZulu.Forward;

/// <summary>Configures a <see cref="ForwardSession" />'s handshake offer and runtime behavior.</summary>
public sealed class ForwardSessionOptions
{
    /// <summary>Gets or sets the catalog version offered during the handshake.</summary>
    public string CatalogVersion { get; set; } = "0";

    /// <summary>Gets or sets the compression algorithm offered during the handshake.</summary>
    public ForwardCompression CompressionOffered { get; set; } = ForwardCompression.None;

    /// <summary>Gets or sets the dedup-window size (in batch count) offered during the handshake.</summary>
    public uint DedupWindowSize { get; set; } = 4096;

    /// <summary>Gets or sets the schema fingerprints this endpoint already has schema bytes for.</summary>
    public IReadOnlyList<ulong> KnownSchemaFingerprints { get; set; } = [];

    /// <summary>Gets or sets the maximum accepted complete frame length, in bytes.</summary>
    public int MaxFrameLength { get; set; } = ForwardParserOptions.DefaultMaxFrameLength;

    /// <summary>Gets or sets the in-flight window size requested during the handshake.</summary>
    public uint RequestedWindowSize { get; set; } = 64;

    /// <summary>Gets or sets a resolver invoked when the peer sends a <see cref="ForwardFrameType.SchemaRequest" /> for a fingerprint this session knows about.</summary>
    public Func<ulong, CancellationToken, Task<byte[]?>>? SchemaResolver { get; set; }

    /// <summary>Gets or sets the resume token presented in the handshake offer; <see cref="Guid.Empty" /> requests a new session.</summary>
    public Guid SessionResumeToken { get; set; } = Guid.Empty;

    /// <summary>Gets or sets the handler invoked for inbound <see cref="ForwardFrameType.TypedBatch" /> and <see cref="ForwardFrameType.RawEnvelope" /> frames. If unset, inbound batches are rejected with a non-committed acknowledgement.</summary>
    public Func<ForwardFrameType, Guid, byte[], CancellationToken, Task<ForwardAckOutcome>>? BatchHandler { get; set; }

    /// <summary>Gets or sets the handler invoked for inbound <see cref="ForwardFrameType.DeadLetterForward" /> frames.</summary>
    public Action<ForwardDeadLetter>? DeadLetterHandler { get; set; }
}
