namespace DeltaZulu.Forward;

/// <summary>Configures DeltaZulu.Forward frame parser limits.</summary>
public readonly record struct ForwardParserOptions
{
    /// <summary>The default maximum complete frame length (header plus payload), in bytes.</summary>
    public const int DefaultMaxFrameLength = 1024 * 1024;

    private readonly int _maxFrameLength;

    /// <summary>Initializes parser options with a bounded frame size.</summary>
    public ForwardParserOptions(int maxFrameLength = DefaultMaxFrameLength)
    {
        if (maxFrameLength < ForwardFrameHeader.EncodedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameLength), $"Maximum frame length must be at least the header length of {ForwardFrameHeader.EncodedLength} bytes.");
        }

        _maxFrameLength = maxFrameLength;
    }

    /// <summary>Gets the default parser options.</summary>
    public static ForwardParserOptions Default { get; } = new(DefaultMaxFrameLength);

    /// <summary>Gets the maximum accepted complete frame length (header plus payload), in bytes.</summary>
    public int MaxFrameLength => _maxFrameLength == 0 ? DefaultMaxFrameLength : _maxFrameLength;
}
