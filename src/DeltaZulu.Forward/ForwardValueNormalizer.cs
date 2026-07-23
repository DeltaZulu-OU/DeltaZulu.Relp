using System.Collections;

namespace DeltaZulu.Forward;

/// <summary>
/// Normalizes <see cref="ForwardLogRecord.Fields" /> values down to the ten KQL scalar
/// types (contract <c>FWD-CONTRACT-v1</c> §1) plus dynamic maps/arrays and
/// <see langword="null" />. Widening/aliased CLR inputs (e.g. <see cref="int" />,
/// <see cref="float" />, <see cref="DateTime" />) are converted to their canonical scalar;
/// anything that does not fit the allowed set is rejected with
/// <see cref="NotSupportedException" /> rather than passed through silently.
/// </summary>
internal static class ForwardValueNormalizer
{
    /// <summary>Returns a copy of <paramref name="batch" /> with every record's fields normalized.</summary>
    public static ForwardLogBatch NormalizeBatch(ForwardLogBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var records = new List<ForwardLogRecord>(batch.Records.Count);
        foreach (var record in batch.Records)
        {
            records.Add(record with { Fields = NormalizeFields(record.Fields) });
        }

        return batch with { Records = records };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeFields(IReadOnlyDictionary<string, object?> fields)
    {
        var normalized = new Dictionary<string, object?>(fields.Count, StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            normalized[pair.Key] = Normalize(pair.Value);
        }

        return normalized;
    }

    /// <summary>Normalizes a single field value, throwing if it cannot be represented on the wire.</summary>
    public static object? Normalize(object? value) => value switch
    {
        null => null,
        bool b => b,
        long l => l,
        int i => (long)i,
        short s => (long)s,
        sbyte sb => (long)sb,
        byte by => (long)by,
        ushort us => (long)us,
        uint ui => (long)ui,
        ulong ul => checked((long)ul),
        double d => d,
        float f => (double)f,
        string str => str,
        DateTimeOffset dto => dto,
        DateTime dt => NormalizeDateTime(dt),
        TimeSpan ts => ts,
        Guid g => g,
        decimal dec => dec,
        IReadOnlyDictionary<string, object?> map => NormalizeFields(map),
        IEnumerable enumerable => NormalizeArray(enumerable),
        _ => throw new NotSupportedException(
            $"Field value of type '{value.GetType()}' is not a supported KQL scalar, dynamic map, or dynamic array.")
    };

    private static DateTimeOffset NormalizeDateTime(DateTime dt) =>
        dt.Kind == DateTimeKind.Local
            ? new DateTimeOffset(dt)
            : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

    private static IReadOnlyList<object?> NormalizeArray(IEnumerable enumerable)
    {
        var list = new List<object?>();
        foreach (var item in enumerable)
        {
            list.Add(Normalize(item));
        }

        return list;
    }
}
