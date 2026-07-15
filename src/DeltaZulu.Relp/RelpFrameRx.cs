using System.Text;

namespace DeltaZulu.Relp;

/// <summary>A received RELP frame.</summary>
public sealed class RelpFrameRx
{
    private readonly byte[] _buffer;

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameRx(int transactionId, RelpCommand command, int length, byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "RELP payload length must not be negative.");
        }

        if (buffer.Length != length)
        {
            throw new ArgumentException("Buffer length must match RELP payload length.", nameof(buffer));
        }

        TransactionId = transactionId;
        Command = command;
        Length = length;
        _buffer = buffer.ToArray();
    }

    /// <summary>Provides a RELP API operation.</summary>
    public byte[] Buffer => _buffer.ToArray();

    /// <summary>Gets a RELP API value.</summary>
    public RelpCommand Command { get; }

    /// <summary>Gets a RELP API value.</summary>
    public int Length { get; }

    /// <summary>Gets a RELP API value.</summary>
    public int TransactionId { get; }

    /// <summary>Provides a RELP API operation.</summary>
    public string GetData() => Encoding.UTF8.GetString(_buffer);

    /// <summary>Provides a RELP API operation.</summary>
    public int GetResponseCode()
    {
        var span = TrimAsciiSpaces(_buffer);
        var firstSpace = span.IndexOf((byte)' ');
        var firstToken = firstSpace < 0 ? span : span[..firstSpace];
        if (firstToken.Length != 3 ||
            !IsAsciiDigit(firstToken[0]) ||
            !IsAsciiDigit(firstToken[1]) ||
            !IsAsciiDigit(firstToken[2]))
        {
            throw new FormatException("Invalid RELP response code.");
        }

        var code = ((firstToken[0] - (byte)'0') * 100) + ((firstToken[1] - (byte)'0') * 10) + (firstToken[2] - (byte)'0');
        if (code is < 100 or > 999)
        {
            throw new FormatException("Invalid RELP response code.");
        }
        return code;
    }

    private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static ReadOnlySpan<byte> TrimAsciiSpaces(byte[] value)
    {
        var span = value.AsSpan();
        var start = 0;
        var end = span.Length - 1;
        while (start < span.Length && span[start] == (byte)' ')
        {
            start++;
        }

        while (end >= start && span[end] == (byte)' ')
        {
            end--;
        }

        return span.Slice(start, end - start + 1);
    }
}
