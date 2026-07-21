using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace DeltaZulu.Forward;

/// <summary>TCP/TLS transport for DeltaZulu.Forward. TLS, when enabled, is layered as a plain stream transport beneath the binary framing, not woven into the protocol's own handshake.</summary>
public sealed class ForwardConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private bool _disposed;
    private PipeReader? _reader;
    private Stream? _stream;
    private PipeWriter? _writer;

    /// <summary>Initializes a connection to the given DeltaZulu.Forward endpoint.</summary>
    public ForwardConnection(string host, int port, bool useTls = false, X509CertificateCollection? clientCertificates = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("DeltaZulu.Forward host must not be empty.", nameof(host));
        }

        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "DeltaZulu.Forward port must be between 1 and 65535.");
        }

        Host = host;
        Port = port;
        UseTls = useTls;
        ClientCertificates = clientCertificates;
    }

    /// <summary>Gets the client certificates presented during TLS authentication, if any.</summary>
    public X509CertificateCollection? ClientCertificates { get; }

    /// <summary>Gets the remote host.</summary>
    public string Host { get; }

    /// <summary>Gets the remote port.</summary>
    public int Port { get; }

    /// <summary>Gets a value indicating whether the connection is layered under TLS.</summary>
    public bool UseTls { get; }

    /// <summary>Opens the underlying TCP (optionally TLS) connection.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is not null)
            {
                throw new InvalidOperationException("Connection is already open.");
            }
            ObjectDisposedException.ThrowIf(_disposed, this);

            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(Host, Port, cancellationToken).ConfigureAwait(false);
                Stream stream = client.GetStream();

                if (UseTls)
                {
                    var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                        TargetHost = Host,
                        ClientCertificates = ClientCertificates
                    }, cancellationToken).ConfigureAwait(false);
                    stream = ssl;
                }

                _client = client;
                _stream = stream;
                _reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
                _writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Wraps an already-accepted <see cref="TcpClient" /> as a DeltaZulu.Forward connection,
    /// for the collector/server role that accepts inbound connections rather than dialing
    /// out. TLS, if required, must already be negotiated on <paramref name="client" />'s
    /// stream before calling this method.
    /// </summary>
    public static ForwardConnection FromAcceptedClient(TcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
        var connection = new ForwardConnection(endpoint?.Address.ToString() ?? "unknown", endpoint?.Port ?? 0);
        connection._client = client;
        connection._stream = client.GetStream();
        connection._reader = PipeReader.Create(connection._stream, new StreamPipeReaderOptions(leaveOpen: true));
        connection._writer = PipeWriter.Create(connection._stream, new StreamPipeWriterOptions(leaveOpen: true));
        return connection;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _connectLock.WaitAsync().ConfigureAwait(false);
        Stream? stream;
        TcpClient? client;
        PipeReader? reader;
        PipeWriter? writer;
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stream = _stream;
            client = _client;
            reader = _reader;
            writer = _writer;
            _stream = null;
            _client = null;
            _reader = null;
            _writer = null;
        }
        finally
        {
            _connectLock.Release();
        }

        if (reader is not null)
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }

        if (writer is not null)
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }

        // Disposing the stream outside the connection lock lets a blocked read or
        // write unblock without DisposeAsync waiting forever on the send/receive
        // semaphores held by those operations.
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        client?.Dispose();
    }

    /// <summary>Reads one complete frame from the connection.</summary>
    public async ValueTask<ForwardFrameRx> ReadFrameAsync(
        ForwardParserOptions options,
        CancellationToken cancellationToken = default)
    {
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var reader = _reader ?? throw new InvalidOperationException("Connection is not open.");
            return await ForwardFrameReader.ReadFrameAsync(reader, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <summary>Reads a raw chunk of bytes directly from the underlying stream, bypassing frame parsing.</summary>
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var stream = _stream ?? throw new InvalidOperationException("Connection is not open.");
            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    throw new IOException("Connection closed by the server.");
                }

                return buffer.AsSpan(0, count).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <summary>Sends raw bytes directly to the underlying stream, bypassing frame encoding.</summary>
    public async Task SendAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await SendAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends raw bytes directly to the underlying stream, bypassing frame encoding.</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var writer = _writer ?? throw new InvalidOperationException("Connection is not open.");
            await writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Encodes and writes one frame to the connection.</summary>
    public async ValueTask WriteFrameAsync(ForwardFrameTx frame, uint transactionNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        await SendAsync(frame.ToByteArray(transactionNumber), cancellationToken).ConfigureAwait(false);
    }
}
