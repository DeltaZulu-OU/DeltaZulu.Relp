using System.Net;
using System.Net.Sockets;
using System.Text;
using ZstdSharp;

namespace DeltaZulu.Forward.Examples.Server
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 1601;
            var bindAddress = args.ElementAtOrDefault(1)?.Equals("any", StringComparison.OrdinalIgnoreCase) == true
                ? IPAddress.Any
                : IPAddress.Loopback;
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            var listener = new TcpListener(bindAddress, port);
            listener.Start();
            Console.Error.WriteLine($"DeltaZulu.Forward zstd NDJSON example server listening on {bindAddress}:{port}");
            Console.Error.WriteLine("Press Ctrl+C to stop.");

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cts.Token);
                    _ = Task.Run(() => RunClientAsync(client, cts.Token), CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Graceful shutdown requested by Ctrl+C.
            }
            finally
            {
                listener.Stop();
            }

            static async Task RunClientAsync(TcpClient client, CancellationToken cancellationToken)
            {
                try
                {
                    await HandleClientAsync(client, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Server shutdown requested.
                }
                catch (Exception exception) when (exception is IOException or SocketException or InvalidOperationException or FormatException)
                {
                    Console.Error.WriteLine($"Client session ended unexpectedly: {exception.Message}");
                }
            }

            static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
            {
                await using var connection = ForwardConnection.FromAcceptedClient(client);
                using var decompressor = new Decompressor();

                var options = new ForwardSessionOptions {
                    CatalogVersion = "example-1",
                    RequestedWindowSize = 64,
                    DedupWindowSize = 4096,
                    BatchHandler = (frameType, batchId, payload, _) => {
                        PrintCompressedJsonLines(decompressor, frameType, batchId, payload);
                        return Task.FromResult(new ForwardAckOutcome(0, null));
                    }
                };

                var session = await ForwardSession.AcceptAsync(
                    connection,
                    offer => new ForwardHandshakeAck(
                        Accepted: true,
                        ProtocolVersion: offer.ProtocolVersion,
                        SessionId: offer.SessionResumeToken == Guid.Empty ? Guid.NewGuid() : offer.SessionResumeToken,
                        GrantedWindowSize: offer.RequestedWindowSize,
                        DedupWindowSize: offer.DedupWindowSize,
                        CompressionSelected: offer.CompressionOffered,
                        UnknownSchemaFingerprints: [],
                        RejectReason: string.Empty),
                    options,
                    cancellationToken);

                Console.WriteLine($"Accepted session {session.SessionId}.");

                // The accepted session's background pump now owns the connection; block here
                // until the peer closes the session or the process is shutting down.
                var pumpCompletion = session.ReceiveLoopCompletion ?? Task.CompletedTask;
                var shutdownRequested = Task.Delay(Timeout.Infinite, cancellationToken);
                await Task.WhenAny(pumpCompletion, shutdownRequested);

                if (session.IsActive)
                {
                    await session.CloseAsync(CancellationToken.None);
                }
            }

            static void PrintCompressedJsonLines(Decompressor decompressor, ForwardFrameType frameType, Guid batchId, byte[] compressed)
            {
                var payload = Encoding.UTF8.GetString(decompressor.Unwrap(compressed));
                foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    Console.WriteLine($"{DateTimeOffset.UtcNow:O} [{frameType} {batchId}]: {line}");
                }
            }
        }
    }
}
