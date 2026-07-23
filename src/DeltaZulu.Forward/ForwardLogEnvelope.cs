namespace DeltaZulu.Forward;

/// <summary>
/// A single KQL-aligned log record forwarded over DeltaZulu.Forward. <see cref="Fields" />
/// is restricted, after normalization, to the ten KQL scalar types (<see cref="bool" />,
/// <see cref="long" />, <see cref="double" />, <see cref="string" />,
/// <see cref="DateTimeOffset" />, <see cref="TimeSpan" />, <see cref="Guid" />,
/// <see cref="decimal" />, dynamic maps/arrays, and <see langword="null" />) — never an
/// upstream producer's internal event model.
/// </summary>
public sealed record ForwardLogRecord
{
    /// <summary>Gets the identifier of the delivery this record was produced for.</summary>
    public required string DeliveryId { get; init; }

    /// <summary>Gets the identifier of the agent that produced this record.</summary>
    public required string AgentId { get; init; }

    /// <summary>Gets the type of the source this record was read from.</summary>
    public required string SourceType { get; init; }

    /// <summary>Gets the name of the source this record was read from.</summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the identifier of the profile that shaped this record, if any.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Gets the version of the profile that shaped this record, if any.</summary>
    public string? ProfileVersion { get; init; }

    /// <summary>Gets the platform of the agent that produced this record, if known.</summary>
    public string? Platform { get; init; }

    /// <summary>Gets the hostname of the agent that produced this record, if known.</summary>
    public string? Hostname { get; init; }

    /// <summary>Gets the identifier of this record, unique within its batch.</summary>
    public required string RecordId { get; init; }

    /// <summary>Gets the instant this record was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the record's field values. After normalization, every value is one of the ten
    /// KQL scalar types, a dynamic map (<see cref="IReadOnlyDictionary{TKey, TValue}" />),
    /// a dynamic array (<see cref="IReadOnlyList{T}" />), or <see langword="null" />.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
}

/// <summary>A batch of <see cref="ForwardLogRecord" /> values forwarded as a single unit.</summary>
public sealed record ForwardLogBatch
{
    /// <summary>Gets the batch identifier, the unit of deduplication and acknowledgement.</summary>
    public required Guid BatchId { get; init; }

    /// <summary>Gets the instant this batch was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the records carried by this batch.</summary>
    public required IReadOnlyList<ForwardLogRecord> Records { get; init; }
}
