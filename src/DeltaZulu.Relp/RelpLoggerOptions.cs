using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DeltaZulu.Relp;

/// <summary>Configures the RELP sink for Microsoft.Extensions.Logging.</summary>
public sealed class RelpLoggerOptions
{
    /// <summary>Gets or sets client certificates used when <see cref="UseTls" /> is true.</summary>
    public X509CertificateCollection? ClientCertificates { get; set; }

    /// <summary>Gets or sets a custom formatter that turns log entries into RELP syslog payload bytes.</summary>
    public Func<RelpLogEntry, byte[]> Formatter { get; set; } = DefaultFormatter;

    /// <summary>Gets or sets the RELP server host.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets whether active logging scopes are included in emitted messages.</summary>
    public bool IncludeScopes { get; set; }

    /// <summary>Gets or sets the minimum level written to RELP.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Gets or sets the RELP server port.</summary>
    public int Port { get; set; } = 1601;

    /// <summary>Gets or sets the bounded in-memory queue capacity.</summary>
    public int QueueCapacity { get; set; } = 1024;

    /// <summary>Gets or sets whether the RELP connection should use TLS.</summary>
    public bool UseTls { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("RELP host must not be empty.", nameof(Host));
        }

        if (Port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "RELP port must be between 1 and 65535.");
        }

        if (QueueCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "RELP queue capacity must be at least 1.");
        }

        ArgumentNullException.ThrowIfNull(Formatter);
    }

    private static byte[] DefaultFormatter(RelpLogEntry entry)
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
