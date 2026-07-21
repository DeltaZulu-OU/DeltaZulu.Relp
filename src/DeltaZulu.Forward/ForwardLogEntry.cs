using Microsoft.Extensions.Logging;

namespace DeltaZulu.Forward;

/// <summary>Represents a formatted Microsoft.Extensions.Logging event ready for DeltaZulu.Forward serialization.</summary>
public sealed record ForwardLogEntry(
    DateTimeOffset Timestamp,
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<object?> Scopes);
