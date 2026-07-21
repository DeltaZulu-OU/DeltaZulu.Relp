using System.Buffers;
using System.IO.Pipelines;

namespace DeltaZulu.Forward;

/// <summary>Reads DeltaZulu.Forward frames from buffered byte sequences.</summary>
public static class ForwardFrameReader
{
    /// <summary>Reads one complete frame from a pipe, advancing consumed bytes after a complete frame.</summary>
    public static async ValueTask<ForwardFrameRx> ReadFrameAsync(
        PipeReader reader,
        ForwardParserOptions options,
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
                throw new IOException("Unable to read from the DeltaZulu.Forward connection.", ex);
            }

            var buffer = result.Buffer;
            var frameBuffer = buffer;

            if (TryReadFrame(ref frameBuffer, options, out var frame))
            {
                // Examined must stop at the consumed position, not the end of whatever the
                // underlying read happened to return: a peer that writes several frames
                // back-to-back (for example a proactive Control frame right after HelloAck)
                // can land more than one frame in a single socket read. Marking the unconsumed
                // remainder as "examined" would tell the pipe nothing new is available there,
                // so the next ReadAsync would block waiting for further I/O instead of
                // returning the already-buffered next frame.
                reader.AdvanceTo(frameBuffer.Start, frameBuffer.Start);
                return frame;
            }

            if (result.IsCompleted)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                throw new IOException("Connection closed before a complete DeltaZulu.Forward frame was received.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>Attempts to read one complete frame from the buffer.</summary>
    public static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, ForwardParserOptions options, out ForwardFrameRx frame)
    {
        frame = null!;

        if (buffer.Length < ForwardFrameHeader.EncodedLength)
        {
            ThrowIfFrameTooLong(buffer.Length, options.MaxFrameLength);
            return false;
        }

        Span<byte> headerBytes = stackalloc byte[ForwardFrameHeader.EncodedLength];
        buffer.Slice(0, ForwardFrameHeader.EncodedLength).CopyTo(headerBytes);
        var header = ForwardFrameHeader.Decode(headerBytes);

        if (header.PayloadLength > options.MaxFrameLength - ForwardFrameHeader.EncodedLength)
        {
            throw new FormatException($"DeltaZulu.Forward frame exceeds the configured maximum frame length of {options.MaxFrameLength} bytes.");
        }

        var totalFrameLength = (long)ForwardFrameHeader.EncodedLength + header.PayloadLength;
        if (buffer.Length < totalFrameLength)
        {
            ThrowIfFrameTooLong(buffer.Length, options.MaxFrameLength);
            return false;
        }

        if (header.TransactionNumber < TxNr.MinValue)
        {
            throw new FormatException("DeltaZulu.Forward transaction number must not be zero.");
        }

        var payload = header.PayloadLength == 0
            ? []
            : buffer.Slice(ForwardFrameHeader.EncodedLength, (long)header.PayloadLength).ToArray();

        var actualChecksum = ForwardFrameHeader.ComputeChecksum(payload);
        if (actualChecksum != header.PayloadChecksum)
        {
            throw new FormatException("DeltaZulu.Forward frame payload failed its CRC-32 integrity check.");
        }

        frame = new ForwardFrameRx(header.TransactionNumber, header.FrameType, header.Flags, header.ProtocolVersion, payload);
        buffer = buffer.Slice(totalFrameLength);
        return true;
    }

    private static void ThrowIfFrameTooLong(long bufferedLength, int maxFrameLength)
    {
        if (bufferedLength > maxFrameLength)
        {
            throw new FormatException($"DeltaZulu.Forward frame exceeds the configured maximum frame length of {maxFrameLength} bytes.");
        }
    }
}
