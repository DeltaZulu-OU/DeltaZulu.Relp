using System.Globalization;
using MessagePack;
using MessagePack.Formatters;

namespace DeltaZulu.Forward;

/// <summary>Wire tags written ahead of each normalized field value's payload.</summary>
internal enum ForwardValueTag : byte
{
    /// <summary>A <see cref="bool" /> value.</summary>
    Bool = 0,

    /// <summary>A <see cref="long" /> value.</summary>
    Long = 1,

    /// <summary>A <see cref="double" /> value.</summary>
    Double = 2,

    /// <summary>A <see cref="string" /> value.</summary>
    String = 3,

    /// <summary>A <see cref="DateTimeOffset" /> value.</summary>
    DateTimeOffset = 4,

    /// <summary>A <see cref="TimeSpan" /> value.</summary>
    TimeSpan = 5,

    /// <summary>A <see cref="Guid" /> value.</summary>
    Guid = 6,

    /// <summary>A <see cref="decimal" /> value.</summary>
    Decimal = 7,

    /// <summary>A dynamic map (<see cref="IReadOnlyDictionary{TKey, TValue}" />) value.</summary>
    Map = 8,

    /// <summary>A dynamic array (<see cref="IReadOnlyList{T}" />) value.</summary>
    Array = 9
}

/// <summary>
/// Formats <see cref="ForwardLogRecord.Fields" /> values as a self-describing
/// <c>[tag, payload]</c> pair so every one of the ten KQL scalars (plus dynamic
/// maps/arrays and <see langword="null" />) round-trips as its exact CLR type. Plain
/// MessagePack/"typeless" inference is lossy at the <see cref="object" /> boundary — for
/// example <see cref="DateTimeOffset" /> and <see cref="decimal" /> would otherwise both
/// decode as ambiguous strings — so this formatter is registered ahead of the contractless
/// resolver for every <see cref="object" />-typed field value.
/// </summary>
internal sealed class ForwardObjectFormatter : IMessagePackFormatter<object?>
{
    /// <summary>The shared formatter instance.</summary>
    public static readonly ForwardObjectFormatter Instance = new();

    private ForwardObjectFormatter()
    {
    }

    /// <inheritdoc />
    public void Serialize(ref MessagePackWriter writer, object? value, MessagePackSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNil();
                return;

            case bool b:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Bool);
                writer.Write(b);
                return;

            case long l:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Long);
                writer.Write(l);
                return;

            case double d:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Double);
                writer.Write(d);
                return;

            case string s:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.String);
                writer.Write(s);
                return;

            case DateTimeOffset dto:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.DateTimeOffset);
                writer.Write(dto.ToString("o", CultureInfo.InvariantCulture));
                return;

            case TimeSpan ts:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.TimeSpan);
                writer.Write(ts.Ticks);
                return;

            case Guid g:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Guid);
                writer.Write(g.ToString("D", CultureInfo.InvariantCulture));
                return;

            case decimal dec:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Decimal);
                writer.Write(dec.ToString(CultureInfo.InvariantCulture));
                return;

            case IReadOnlyDictionary<string, object?> map:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Map);
                writer.WriteMapHeader(map.Count);
                foreach (var pair in map)
                {
                    writer.Write(pair.Key);
                    Serialize(ref writer, pair.Value, options);
                }

                return;

            case IReadOnlyList<object?> list:
                writer.WriteArrayHeader(2);
                writer.Write((byte)ForwardValueTag.Array);
                writer.WriteArrayHeader(list.Count);
                foreach (var item in list)
                {
                    Serialize(ref writer, item, options);
                }

                return;

            default:
                throw new NotSupportedException(
                    $"Field value of type '{value.GetType()}' cannot be encoded. Values must be normalized via ForwardValueNormalizer before encoding.");
        }
    }

    /// <inheritdoc />
    public object? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        var headerLength = reader.ReadArrayHeader();
        if (headerLength != 2)
        {
            throw new MessagePackSerializationException(
                $"Expected a 2-element tagged Forward value array but found {headerLength} elements.");
        }

        var tag = (ForwardValueTag)reader.ReadByte();
        return tag switch
        {
            ForwardValueTag.Bool => reader.ReadBoolean(),
            ForwardValueTag.Long => reader.ReadInt64(),
            ForwardValueTag.Double => reader.ReadDouble(),
            ForwardValueTag.String => reader.ReadString(),
            ForwardValueTag.DateTimeOffset => System.DateTimeOffset.ParseExact(
                reader.ReadString()!, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ForwardValueTag.TimeSpan => new TimeSpan(reader.ReadInt64()),
            ForwardValueTag.Guid => System.Guid.ParseExact(reader.ReadString()!, "D"),
            ForwardValueTag.Decimal => decimal.Parse(reader.ReadString()!, CultureInfo.InvariantCulture),
            ForwardValueTag.Map => DeserializeMap(ref reader, options),
            ForwardValueTag.Array => DeserializeArray(ref reader, options),
            _ => throw new MessagePackSerializationException($"Unknown Forward value tag '{tag}'.")
        };
    }

    private IReadOnlyDictionary<string, object?> DeserializeMap(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadMapHeader();
        var map = new Dictionary<string, object?>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString()!;
            map[key] = Deserialize(ref reader, options);
        }

        return map;
    }

    private IReadOnlyList<object?> DeserializeArray(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var count = reader.ReadArrayHeader();
        var list = new List<object?>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(Deserialize(ref reader, options));
        }

        return list;
    }
}
