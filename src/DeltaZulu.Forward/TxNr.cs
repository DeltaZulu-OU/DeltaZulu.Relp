namespace DeltaZulu.Forward;

/// <summary>
/// Generates the per-frame transaction number carried in the DeltaZulu.Forward frame header.
/// Harvested from RELP's transaction identifier, but ranges over the full unsigned 32-bit
/// header field instead of RELP's decimal-digit-bounded 1..999999999 range.
/// </summary>
public sealed class TxNr : IComparable, IComparable<TxNr>, IEquatable<TxNr>, IFormattable
{
    /// <summary>The maximum valid transaction number.</summary>
    public const uint MaxValue = uint.MaxValue;

    /// <summary>The minimum valid transaction number.</summary>
    public const uint MinValue = 1;

    private const ulong range = (ulong)MaxValue - MinValue + 1;
    private uint _value;

    /// <summary>Initializes a transaction number counter starting at <see cref="MinValue" />.</summary>
    public TxNr() : this(MinValue)
    {
    }

    /// <summary>Initializes a transaction number counter starting at the given value.</summary>
    public TxNr(uint initialValue)
    {
        if (initialValue < MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(initialValue), $"Transaction number must be at least {MinValue}.");
        }

        _value = initialValue;
    }

    /// <summary>Gets the current transaction number.</summary>
    public uint Value => Volatile.Read(ref _value);

    /// <summary>Converts an underlying value to a transaction number.</summary>
    public static explicit operator TxNr(uint value) => new(value);

    /// <summary>Converts a transaction number to its underlying value.</summary>
    public static implicit operator uint(TxNr txNr)
    {
        ArgumentNullException.ThrowIfNull(txNr);
        return txNr.Value;
    }

    public static bool operator !=(TxNr left, TxNr right)
    {
        return !(left == right);
    }

    /// <summary>Advances a transaction number by an offset, wrapping around <see cref="MaxValue" />.</summary>
    public static TxNr operator +(TxNr txNr, uint offset)
    {
        ArgumentNullException.ThrowIfNull(txNr);
        return new TxNr(Shift(txNr.Value, offset));
    }

    /// <summary>Advances a transaction number by one, wrapping around <see cref="MaxValue" />.</summary>
    public static TxNr operator ++(TxNr txNr)
    {
        ArgumentNullException.ThrowIfNull(txNr);
        txNr.Move(1);
        return txNr;
    }

    public static bool operator <(TxNr left, TxNr right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(TxNr left, TxNr right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator ==(TxNr left, TxNr right)
    {
        if (ReferenceEquals(left, null))
        {
            return ReferenceEquals(right, null);
        }

        return left.Equals(right);
    }

    public static bool operator >(TxNr left, TxNr right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(TxNr left, TxNr right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <summary>Parses a transaction number from its decimal string representation.</summary>
    public static TxNr Parse(string value)
    {
        if (!TryParse(value, out var txNr))
        {
            throw new FormatException($"Value must be a transaction number of at least {MinValue}.");
        }

        return txNr!;
    }

    /// <summary>Attempts to parse a transaction number from its decimal string representation.</summary>
    public static bool TryParse(string? value, out TxNr? txNr)
    {
        txNr = null;
        if (!uint.TryParse(value, out var parsed) || parsed < MinValue)
        {
            return false;
        }

        txNr = new TxNr(parsed);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj switch {
        null => 1,
        TxNr txNr => CompareTo(txNr),
        uint value => Value.CompareTo(value),
        _ => throw new ArgumentException("Object must be a TxNr or uint.", nameof(obj))
    };

    /// <inheritdoc />
    public int CompareTo(TxNr? other) => other is null ? 1 : Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(TxNr? other) => other is not null && Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj switch {
        TxNr txNr => Equals(txNr),
        uint value => Value == value,
        _ => false
    };

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Returns the current transaction number and advances the counter, wrapping around <see cref="MaxValue" />.</summary>
    public uint Next() => Move(1);

    /// <inheritdoc />
    public override string ToString() => Value.ToString();

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

    private static uint Shift(uint value, uint offset)
    {
        var zeroBased = (ulong)(value - MinValue);
        var shifted = (zeroBased + offset) % range;
        return (uint)shifted + MinValue;
    }

    private uint Move(uint offset)
    {
        while (true)
        {
            var current = Volatile.Read(ref _value);
            var next = Shift(current, offset);
            if (Interlocked.CompareExchange(ref _value, next, current) == current)
            {
                return current;
            }
        }
    }
}
