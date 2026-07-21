namespace DeltaZulu.Forward;

/// <summary>Payload compression algorithms negotiable in the DeltaZulu.Forward handshake.</summary>
public enum ForwardCompression : byte
{
    /// <summary>No payload compression.</summary>
    None = 0,

    /// <summary>Zstandard payload compression.</summary>
    Zstd = 1
}
