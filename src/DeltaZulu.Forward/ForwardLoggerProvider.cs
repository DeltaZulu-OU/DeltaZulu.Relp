using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Forward;

/// <summary>Provides a Microsoft.Extensions.Logging sink that forwards log events over DeltaZulu.Forward.</summary>
public sealed class ForwardLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly Channel<ForwardLogEntry> _channel;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private bool _disposed;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    /// <summary>Initializes a new DeltaZulu.Forward logging provider.</summary>
    public ForwardLoggerProvider(ForwardLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Options = options;
        _channel = Channel.CreateBounded<ForwardLogEntry>(new BoundedChannelOptions(options.QueueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _worker = Task.Run(ProcessQueueAsync);
    }

    internal ForwardLoggerOptions Options { get; }

    internal IExternalScopeProvider ScopeProvider => _scopeProvider;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ForwardLogger(categoryName, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        _stopping.CancelAfter(TimeSpan.FromSeconds(5));
#pragma warning disable RCS1075 // Avoid empty catch clause that catches System.Exception
        try
        {
            _worker.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Logging providers must not throw during application shutdown.
        }
        finally
        {
            _stopping.Dispose();
        }
#pragma warning restore RCS1075 // Avoid empty catch clause that catches System.Exception
    }

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    internal void Enqueue(ForwardLogEntry entry)
    {
        if (!_disposed)
        {
            _channel.Writer.TryWrite(entry);
        }
    }

    internal bool IsEnabled(LogLevel logLevel) => !_disposed && logLevel != LogLevel.None && logLevel >= Options.MinimumLevel;

    private async Task ProcessQueueAsync()
    {
        ForwardConnection? connection = null;
        ForwardSession? session = null;

        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                try
                {
                    if (session?.IsActive != true)
                    {
                        connection = new ForwardConnection(Options.Host, Options.Port, Options.UseTls, Options.ClientCertificates);
                        session = new ForwardSession(connection, new ForwardSessionOptions {
                            RequestedWindowSize = Options.RequestedWindowSize,
                            DedupWindowSize = Options.DedupWindowSize
                        });
                        await connection.ConnectAsync(_stopping.Token).ConfigureAwait(false);
                        await session.OpenAsync(_stopping.Token).ConfigureAwait(false);
                    }

                    await session.SendRawEnvelopeAsync(Options.Formatter(entry), _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    if (connection is not null)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }

                    connection = null;
                    session = null;
                }
            }
        }
        finally
        {
            if (session?.IsActive == true)
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
