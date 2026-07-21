using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Forward;

/// <summary>
/// Configures the DeltaZulu.Forward sink for Microsoft.Extensions.Logging. Log entries are not
/// catalog-typed values, so they are sent as <see cref="ForwardFrameType.RawEnvelope" />
/// batches, one entry per batch, rather than as typed batches.
/// </summary>
public sealed class ForwardLoggerOptions
{
    /// <summary>Gets or sets client certificates used when <see cref="UseTls" /> is true.</summary>
    public X509CertificateCollection? ClientCertificates { get; set; }

    /// <summary>Gets or sets the dedup-window size offered during the handshake.</summary>
    public uint DedupWindowSize { get; set; } = 256;

    /// <summary>Gets or sets a custom formatter that turns log entries into raw-envelope payload bytes.</summary>
    public Func<ForwardLogEntry, byte[]> Formatter { get; set; } = DefaultFormatter;

    /// <summary>Gets or sets the DeltaZulu.Forward server host.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets whether active logging scopes are included in emitted messages.</summary>
    public bool IncludeScopes { get; set; }

    /// <summary>Gets or sets the minimum level written to DeltaZulu.Forward.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Gets or sets the DeltaZulu.Forward server port.</summary>
    public int Port { get; set; } = 1601;

    /// <summary>Gets or sets the bounded in-memory queue capacity.</summary>
    public int QueueCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the in-flight window size requested during the handshake.</summary>
    public uint RequestedWindowSize { get; set; } = 16;

    /// <summary>Gets or sets whether the DeltaZulu.Forward connection should use TLS.</summary>
    public bool UseTls { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("DeltaZulu.Forward host must not be empty.", nameof(Host));
        }

        if (Port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "DeltaZulu.Forward port must be between 1 and 65535.");
        }

        if (QueueCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "DeltaZulu.Forward queue capacity must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(Formatter);
    }

    private static byte[] DefaultFormatter(ForwardLogEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append(entry.Timestamp.ToString("O"))
            .Append(' ')
            .Append(entry.Level)
            .Append(' ')
            .Append(entry.Category)
            .Append('[')
            .Append(entry.EventId.Id)
            .Append("] ")
            .Append(entry.Message);

        if (entry.Scopes.Count > 0)
        {
            builder.Append(" scopes=").AppendJoin(" => ", entry.Scopes);
        }

        if (entry.Exception is not null)
        {
            builder.AppendLine().Append(entry.Exception);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
