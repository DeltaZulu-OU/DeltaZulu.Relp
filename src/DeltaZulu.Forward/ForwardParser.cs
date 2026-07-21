using System.Buffers;

namespace DeltaZulu.Forward;

/// <summary>Incremental parser for octet-counted, binary-framed DeltaZulu.Forward frames.</summary>
public sealed class ForwardParser
{
    /// <summary>The default maximum complete frame length, in bytes.</summary>
    public const int DefaultMaxFrameLength = ForwardParserOptions.DefaultMaxFrameLength;

    private readonly ForwardParserOptions _options;
    private byte[] _buffer = [];
    private int _count;

    /// <summary>Initializes a parser with a bounded frame size.</summary>
    public ForwardParser(int maxFrameLength = DefaultMaxFrameLength)
        : this(new ForwardParserOptions(maxFrameLength))
    {
    }

    /// <summary>Initializes a parser with a bounded frame size.</summary>
    public ForwardParser(ForwardParserOptions options)
    {
        _options = options;
    }

    /// <summary>Gets a copy of the parsed payload bytes.</summary>
    public byte[] Data { get; private set; } = [];

    /// <summary>Gets the parsed frame flags.</summary>
    public ForwardFrameFlags Flags { get; private set; }

    /// <summary>Gets the parsed frame type.</summary>
    public ForwardFrameType FrameType { get; private set; }

    /// <summary>Gets a value indicating whether a complete frame has been parsed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the maximum accepted complete frame length, in bytes.</summary>
    public int MaxFrameLength => _options.MaxFrameLength;

    /// <summary>Gets the parsed wire protocol version.</summary>
    public ushort ProtocolVersion { get; private set; }

    /// <summary>Gets bytes received after the completed frame.</summary>
    public byte[] RemainingBytes { get; private set; } = [];

    /// <summary>Gets the parsed transaction number.</summary>
    public uint TransactionNumber { get; private set; }

    /// <summary>Appends one byte to the parser and attempts to complete a frame.</summary>
    public void Parse(byte value)
    {
        Span<byte> single = [value];
        Parse(single);
    }

    /// <summary>Appends bytes to the parser and attempts to complete a frame.</summary>
    public void Parse(ReadOnlySpan<byte> bytes)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Parser has already completed a frame. Create a new parser for additional frames and pass RemainingBytes first.");
        }

        if (bytes.Length > MaxFrameLength - _count)
        {
            throw new FormatException($"DeltaZulu.Forward frame exceeds the configured maximum frame length of {MaxFrameLength} bytes.");
        }

        EnsureCapacity(_count + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        var sequence = new ReadOnlySequence<byte>(_buffer, 0, _count);
        if (!ForwardFrameReader.TryReadFrame(ref sequence, _options, out var frame))
        {
            return;
        }

        TransactionNumber = frame.TransactionNumber;
        FrameType = frame.FrameType;
        Flags = frame.Flags;
        ProtocolVersion = frame.ProtocolVersion;
        Data = frame.Payload;
        RemainingBytes = sequence.ToArray();
        IsComplete = true;
    }

    /// <summary>Creates a received frame from the completed parser state.</summary>
    public ForwardFrameRx ToFrame()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("Parser has not completed parsing a frame.");
        }

        return new ForwardFrameRx(TransactionNumber, FrameType, Flags, ProtocolVersion, Data);
    }

    private void EnsureCapacity(int needed)
    {
        if (_buffer.Length >= needed)
        {
            return;
        }

        var newSize = Math.Max(needed, _buffer.Length == 0 ? 256 : _buffer.Length * 2);
        Array.Resize(ref _buffer, newSize);
    }
}
