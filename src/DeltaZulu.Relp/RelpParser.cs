using System.Buffers;

namespace DeltaZulu.Relp;

/// <summary>Incremental parser for octet-counted RELP frames.</summary>
public sealed class RelpParser
{
    /// <summary>The default maximum complete RELP frame length, in bytes.</summary>
    public const int DefaultMaxFrameLength = RelpParserOptions.DefaultMaxFrameLength;

    /// <summary>The default maximum RELP header length, in bytes.</summary>
    public const int DefaultMaxHeaderLength = RelpParserOptions.DefaultMaxHeaderLength;

    private readonly RelpParserOptions _options;
    private byte[] _buffer = Array.Empty<byte>();
    private int _count;

    /// <summary>Initializes a parser with bounded header and frame sizes.</summary>
    public RelpParser(int maxFrameLength = DefaultMaxFrameLength, int maxHeaderLength = DefaultMaxHeaderLength)
        : this(new RelpParserOptions(maxFrameLength, maxHeaderLength))
    {
    }

    /// <summary>Initializes a parser with bounded header and frame sizes.</summary>
    public RelpParser(RelpParserOptions options)
    {
        _options = options;
    }

    /// <summary>Gets the parsed RELP command.</summary>
    public RelpCommand Command { get; private set; }

    /// <summary>Gets a copy of the parsed payload bytes.</summary>
    public byte[] Data { get; private set; } = Array.Empty<byte>();

    /// <summary>Gets a value indicating whether a complete RELP frame has been parsed.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the parsed payload length, in bytes.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the maximum accepted complete RELP frame length, in bytes.</summary>
    public int MaxFrameLength => _options.MaxFrameLength;

    /// <summary>Gets the maximum accepted RELP header length, in bytes.</summary>
    public int MaxHeaderLength => _options.MaxHeaderLength;

    /// <summary>Gets bytes received after the completed frame.</summary>
    public byte[] RemainingBytes { get; private set; } = Array.Empty<byte>();

    /// <summary>Gets the parsed transaction identifier.</summary>
    public int TransactionId { get; private set; }

    /// <summary>Appends one byte to the parser and attempts to complete a RELP frame.</summary>
    public void Parse(byte value)
    {
        Span<byte> single = stackalloc byte[1];
        single[0] = value;
        Parse(single);
    }

    /// <summary>Appends bytes to the parser and attempts to complete a RELP frame.</summary>
    public void Parse(ReadOnlySpan<byte> bytes)
    {
        if (IsComplete)
        {
            throw new InvalidOperationException("Parser has already completed a RELP frame. Create a new parser for additional frames and pass RemainingBytes first.");
        }

        if (bytes.Length > MaxFrameLength - _count)
        {
            throw new FormatException($"RELP frame exceeds the configured maximum frame length of {MaxFrameLength} bytes.");
        }

        EnsureCapacity(_count + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_count));
        _count += bytes.Length;

        var sequence = new ReadOnlySequence<byte>(_buffer, 0, _count);
        if (!RelpFrameReader.TryReadFrame(ref sequence, _options, out var frame))
        {
            return;
        }

        TransactionId = frame.TransactionId;
        Command = frame.Command;
        Length = frame.Length;
        Data = frame.Buffer;
        RemainingBytes = sequence.ToArray();
        IsComplete = true;
    }

    /// <summary>Creates a received frame from the completed parser state.</summary>
    public RelpFrameRx ToFrame()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException("Parser has not completed parsing a RELP frame.");
        }

        return new RelpFrameRx(TransactionId, Command, Length, Data);
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
