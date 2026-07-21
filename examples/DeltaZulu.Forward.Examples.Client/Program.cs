using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace DeltaZulu.Forward.Examples.Client
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
            var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 1601;
            var count = args.Length > 2 && int.TryParse(args[2], out var parsedCount) ? parsedCount : 5_000_000;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            await using var connection = new ForwardConnection(host, port);
            var session = new ForwardSession(connection, new ForwardSessionOptions {
                CatalogVersion = "example-1",
                RequestedWindowSize = 64,
                DedupWindowSize = 4096
            });
            using var compressor = new Compressor(3);

            try
            {
                await connection.ConnectAsync(cts.Token);
                await session.OpenAsync(cts.Token);
                Console.WriteLine($"Started new session {session.SessionId}.");
                Console.WriteLine("Forwarding typed batches...");
                Console.WriteLine("Press Ctrl+C to stop sending and close the session.");

                for (var index = 1; index <= count; index++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var line = JsonSerializer.Serialize(new {
                        timestamp = DateTimeOffset.UtcNow,
                        level = "info",
                        message = $"hello from zstd-compressed NDJSON #{index}",
                        sequence = index
                    });

                    var compressed = compressor.Wrap(Encoding.UTF8.GetBytes(line)).ToArray();
                    await session.SendTypedBatchAsync(compressed, cts.Token);
                }

                Console.WriteLine("Batch sending completed.");
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                Console.Error.WriteLine("Cancellation requested. Stopping log forwarding...");
            }
            finally
            {
                if (session.IsActive)
                {
                    Console.WriteLine("Closing session...");
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    try
                    {
                        await session.CloseAsync(closeCts.Token);
                    }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException)
                    {
                        Console.Error.WriteLine($"Session close did not complete cleanly: {exception.Message}");
                    }
                }
            }
        }
    }
}
