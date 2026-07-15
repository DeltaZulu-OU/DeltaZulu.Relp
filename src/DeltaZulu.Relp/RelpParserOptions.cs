namespace DeltaZulu.Relp;

/// <summary>Configures RELP frame parser limits.</summary>
public readonly record struct RelpParserOptions
{
    /// <summary>The default maximum RELP header length, in bytes.</summary>
    public const int DefaultMaxHeaderLength = 4096;

    /// <summary>The default maximum complete RELP frame length, in bytes.</summary>
    public const int DefaultMaxFrameLength = 1024 * 1024;

    private readonly int _maxFrameLength;
    private readonly int _maxHeaderLength;

    /// <summary>Initializes parser options with bounded header and frame sizes.</summary>
    public RelpParserOptions(int maxFrameLength = DefaultMaxFrameLength, int maxHeaderLength = DefaultMaxHeaderLength)
    {
        if (maxFrameLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameLength), "Maximum RELP frame length must be positive.");
        }

        if (maxHeaderLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeaderLength), "Maximum RELP header length must be positive.");
        }

        if (maxHeaderLength > maxFrameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeaderLength), "Maximum RELP header length must not exceed the maximum frame length.");
        }

        _maxFrameLength = maxFrameLength;
        _maxHeaderLength = maxHeaderLength;
    }

    /// <summary>Gets the default parser options.</summary>
    public static RelpParserOptions Default { get; } = new(DefaultMaxFrameLength, DefaultMaxHeaderLength);

    /// <summary>Gets the maximum accepted complete RELP frame length, in bytes.</summary>
    public int MaxFrameLength => _maxFrameLength == 0 ? DefaultMaxFrameLength : _maxFrameLength;

    /// <summary>Gets the maximum accepted RELP header length, in bytes.</summary>
    public int MaxHeaderLength => _maxHeaderLength == 0 ? DefaultMaxHeaderLength : _maxHeaderLength;
}
