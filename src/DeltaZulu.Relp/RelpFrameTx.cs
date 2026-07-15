using System.Buffers.Text;
using System.Text;

namespace DeltaZulu.Relp;

/// <summary>A RELP frame ready for transmission.</summary>
public sealed class RelpFrameTx
{
    private readonly byte[] _message;

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameTx(RelpCommand command, byte[]? message = null)
    {
        Command = command;
        _message = message?.ToArray() ?? Array.Empty<byte>();
    }

    /// <summary>Gets a RELP API value.</summary>
    public RelpCommand Command { get; }

    /// <summary>Provides a RELP API operation.</summary>
    public byte[] Message => _message.ToArray();

    /// <summary>Provides a RELP API operation.</summary>
    public static RelpFrameTx FromCommand(RelpCommand command) => new(command);

    /// <summary>Provides a RELP API operation.</summary>
    public static RelpFrameTx FromCommandAndMessage(RelpCommand command, byte[] message) => new(command, message);

    /// <summary>Provides a RELP API operation.</summary>
    public static RelpFrameTx FromMessage(byte[] message) => new(RelpCommand.Syslog, message);

    /// <summary>Provides a RELP API operation.</summary>
    public byte[] ToByteArray(int transactionId = TxId.MinValue)
    {
        if (transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), $"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        var command = Command.ToProtocolString();
        var transactionDigits = CountDigits(transactionId);
        var lengthDigits = CountDigits(_message.Length);
        var payloadSeparatorLength = _message.Length == 0 ? 0 : 1;
        var result = new byte[transactionDigits + 1 + command.Length + 1 + lengthDigits + payloadSeparatorLength + _message.Length + 1];
        var destination = result.AsSpan();
        var offset = 0;

        Utf8Formatter.TryFormat(transactionId, destination[offset..], out var written);
        offset += written;
        destination[offset++] = (byte)' ';
        offset += Encoding.ASCII.GetBytes(command, destination[offset..]);
        destination[offset++] = (byte)' ';
        Utf8Formatter.TryFormat(_message.Length, destination[offset..], out written);
        offset += written;

        if (_message.Length > 0)
        {
            destination[offset++] = (byte)' ';
            _message.CopyTo(destination[offset..]);
            offset += _message.Length;
        }

        destination[offset] = (byte)'\n';
        return result;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public string ToProtocolString(int transactionId = TxId.MinValue)
    {
        var frame = ToByteArray(transactionId);
        return Encoding.UTF8.GetString(frame, 0, frame.Length - 1);
    }

    private static int CountDigits(int value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }
}
