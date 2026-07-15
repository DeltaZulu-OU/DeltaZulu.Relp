using Microsoft.Extensions.Logging;

namespace DeltaZulu.Relp;

/// <summary>Represents a formatted Microsoft.Extensions.Logging event ready for RELP serialization.</summary>
public sealed record RelpLogEntry(
    DateTimeOffset Timestamp,
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<object?> Scopes);
