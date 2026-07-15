using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;

namespace DeltaZulu.Relp;

/// <summary>Reads RELP frames from buffered byte sequences.</summary>
public static class RelpFrameReader
{
    /// <summary>Reads one RELP frame from a pipe, advancing consumed bytes after a complete frame.</summary>
    public static async ValueTask<RelpFrameRx> ReadFrameAsync(
        PipeReader reader,
        RelpParserOptions options,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            ReadResult result;
            try
            {
                result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new IOException("Unable to read from the RELP connection.", ex);
            }

            var buffer = result.Buffer;
            var frameBuffer = buffer;

            if (TryReadFrame(ref frameBuffer, options, out var frame))
            {
                reader.AdvanceTo(frameBuffer.Start, frameBuffer.End);
                return frame;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                throw new IOException("Connection closed before a complete RELP frame was received.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>Attempts to read one complete RELP frame from the buffer.</summary>
    public static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, RelpParserOptions options, out RelpFrameRx frame)
    {
        frame = null!;
        var reader = new SequenceReader<byte>(buffer);

        if (!TryReadToken(ref reader, (byte)' ', options.MaxHeaderLength, out var transactionToken, out _))
        {
            ThrowIfHeaderTooLong(buffer.Length, options.MaxHeaderLength);
            return false;
        }

        if (!TryReadToken(ref reader, (byte)' ', options.MaxHeaderLength, out var commandToken, out _))
        {
            ThrowIfHeaderTooLong(buffer.Length, options.MaxHeaderLength);
            return false;
        }

        if (!TryReadLengthToken(ref reader, options.MaxHeaderLength, out var lengthToken, out var hasPayloadSeparator))
        {
            ThrowIfHeaderTooLong(buffer.Length, options.MaxHeaderLength);
            return false;
        }

        var headerLength = reader.Consumed;
        if (headerLength > options.MaxHeaderLength)
        {
            throw new FormatException($"RELP header exceeds the configured maximum header length of {options.MaxHeaderLength} bytes.");
        }

        if (!TryParseInt32(transactionToken, out var transactionId) || transactionId is < TxId.MinValue or > TxId.MaxValue)
        {
            throw new FormatException($"RELP transaction id must be between {TxId.MinValue} and {TxId.MaxValue}.");
        }

        var commandParsed = commandToken.IsSingleSegment
            ? RelpCommandExtensions.TryParseProtocolSpan(commandToken.FirstSpan, out var command)
            : RelpCommandExtensions.TryParseProtocolSpan(commandToken.ToArray(), out command);
        if (!commandParsed)
        {
            throw new FormatException("Invalid RELP command.");
        }

        if (!TryParseInt32(lengthToken, out var length) || length < 0)
        {
            throw new FormatException("Negative or invalid payload length.");
        }

        if (!hasPayloadSeparator)
        {
            if (length != 0)
            {
                throw new FormatException("Non-empty RELP frame is missing the payload separator.");
            }

            frame = new RelpFrameRx(transactionId, command, length, []);
            buffer = buffer.Slice(reader.Position);
            return true;
        }

        if (length > options.MaxFrameLength - headerLength - 1)
        {
            throw new FormatException($"RELP frame exceeds the configured maximum frame length of {options.MaxFrameLength} bytes.");
        }

        if (buffer.Length < headerLength + length + 1)
        {
            if (buffer.Length > options.MaxFrameLength)
            {
                throw new FormatException($"RELP frame exceeds the configured maximum frame length of {options.MaxFrameLength} bytes.");
            }

            return false;
        }

        var payloadStart = reader.Position;
        var payloadEnd = buffer.GetPosition(length, payloadStart);
        var newlinePosition = payloadEnd;
        if (buffer.Slice(newlinePosition, 1).ToArray()[0] != (byte)'\n')
        {
            throw new FormatException("RELP frame is not terminated after the declared payload length.");
        }

        frame = new RelpFrameRx(transactionId, command, length, buffer.Slice(payloadStart, length).ToArray());
        buffer = buffer.Slice(buffer.GetPosition(1, newlinePosition));
        return true;
    }

    private static void ThrowIfHeaderTooLong(long bufferedLength, int maxHeaderLength)
    {
        if (bufferedLength > maxHeaderLength)
        {
            throw new FormatException($"RELP header exceeds the configured maximum header length of {maxHeaderLength} bytes.");
        }
    }

    private static bool TryParseInt32(ReadOnlySequence<byte> token, out int value)
    {
        if (token.IsSingleSegment)
        {
            var span = token.FirstSpan;
            return Utf8Parser.TryParse(span, out value, out var consumed) && consumed == span.Length;
        }

        var buffer = token.ToArray();
        return Utf8Parser.TryParse(buffer, out value, out var copiedConsumed) && copiedConsumed == buffer.Length;
    }

    private static bool TryReadLengthToken(
        ref SequenceReader<byte> reader,
        int maxHeaderLength,
        out ReadOnlySequence<byte> token,
        out bool hasPayloadSeparator)
    {
        var start = reader.Position;
        while (reader.TryRead(out var value))
        {
            if (reader.Consumed > maxHeaderLength)
            {
                throw new FormatException($"RELP header exceeds the configured maximum header length of {maxHeaderLength} bytes.");
            }

            if (value == (byte)' ' || value == (byte)'\n')
            {
                token = reader.Sequence.Slice(start, reader.Sequence.GetPosition(reader.Consumed - 1));
                hasPayloadSeparator = value == (byte)' ';
                return true;
            }
        }

        token = default;
        hasPayloadSeparator = false;
        ThrowIfHeaderTooLong(reader.Consumed, maxHeaderLength);
        return false;
    }

    private static bool TryReadToken(
                    ref SequenceReader<byte> reader,
        byte delimiter,
        int maxHeaderLength,
        out ReadOnlySequence<byte> token,
        out bool foundDelimiter)
    {
        if (reader.TryReadTo(out token, delimiter, advancePastDelimiter: true))
        {
            foundDelimiter = true;
            return true;
        }

        foundDelimiter = false;
        token = default;
        ThrowIfHeaderTooLong(reader.Consumed, maxHeaderLength);
        return false;
    }
}
