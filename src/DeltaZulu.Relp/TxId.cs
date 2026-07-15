namespace DeltaZulu.Relp;

/// <summary>Represents a RELP API type.</summary>
public sealed class TxId : IComparable, IComparable<TxId>, IEquatable<TxId>, IFormattable
{
    /// <summary>Defines a RELP API constant.</summary>
    public const int MaxValue = 999_999_999;

    /// <summary>Defines a RELP API constant.</summary>
    public const int MinValue = 1;

    private int _transactionIdentifier;

    /// <summary>Provides a RELP API operation.</summary>
    public TxId() : this(MinValue)
    {
    }

    internal TxId(int initialTransactionIdentifier)
    {
        if (initialTransactionIdentifier < MinValue || initialTransactionIdentifier > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTransactionIdentifier), $"RELP transaction id must be between {MinValue} and {MaxValue}.");
        }

        _transactionIdentifier = initialTransactionIdentifier;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public uint UnsignedValue => (uint)Value;

    /// <summary>Provides a RELP API operation.</summary>
    public int Value => Volatile.Read(ref _transactionIdentifier);

    /// <summary>Provides a RELP API operation.</summary>
    public static explicit operator TxId(int value) => new(value);

    /// <summary>Provides a RELP API operation.</summary>
    public static explicit operator TxId(uint value)
    {
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"RELP transaction id must be between {MinValue} and {MaxValue}.");
        }

        return new TxId((int)value);
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static implicit operator int(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return txId.Value;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static implicit operator uint(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return txId.UnsignedValue;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static TxId operator -(TxId txId, int offset)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return new TxId(Shift(txId.Value, -(long)offset));
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static TxId operator --(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        txId.Move(-1);
        return txId;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static TxId operator +(TxId txId, int offset)
    {
        ArgumentNullException.ThrowIfNull(txId);
        return new TxId(Shift(txId.Value, offset));
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static TxId operator ++(TxId txId)
    {
        ArgumentNullException.ThrowIfNull(txId);
        txId.Move(1);
        return txId;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static TxId Parse(string value)
    {
        if (!TryParse(value, out var txId))
        {
            throw new FormatException($"Value must be a RELP transaction id between {MinValue} and {MaxValue}.");
        }

        return txId!;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public static bool TryParse(string? value, out TxId? txId)
    {
        txId = null;
        if (!uint.TryParse(value, out var parsed) || parsed < MinValue || parsed > MaxValue)
        {
            return false;
        }

        txId = new TxId((int)parsed);
        return true;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public int CompareTo(object? obj) => obj switch {
        null => 1,
        TxId txId => CompareTo(txId),
        int value => Value.CompareTo(value),
        uint value => UnsignedValue.CompareTo(value),
        _ => throw new ArgumentException("Object must be a TxId, int, or uint.", nameof(obj))
    };

    /// <summary>Provides a RELP API operation.</summary>
    public int CompareTo(TxId? other) => other is null ? 1 : UnsignedValue.CompareTo(other.UnsignedValue);

    /// <summary>Provides a RELP API operation.</summary>
    public bool Equals(TxId? other) => other is not null && UnsignedValue == other.UnsignedValue;

    /// <summary>Provides a RELP API operation.</summary>
    public override bool Equals(object? obj) => obj switch {
        TxId txId => Equals(txId),
        int value => Value == value,
        uint value => UnsignedValue == value,
        _ => false
    };

    /// <summary>Provides a RELP API operation.</summary>
    public override int GetHashCode() => UnsignedValue.GetHashCode();

    /// <summary>Provides a RELP API operation.</summary>
    public int Next() => Move(1);

    /// <summary>Provides a RELP API operation.</summary>
    public int Previous() => Move(-1);

    /// <summary>Provides a RELP API operation.</summary>
    public override string ToString() => UnsignedValue.ToString();

    /// <summary>Provides a RELP API operation.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => UnsignedValue.ToString(format, formatProvider);

    private static int Shift(int value, long offset)
    {
        var zeroBased = value - MinValue;
        var shifted = (zeroBased + offset) % MaxValue;
        if (shifted < 0)
        {
            shifted += MaxValue;
        }

        return (int)shifted + MinValue;
    }

    private int Move(long offset)
    {
        while (true)
        {
            var current = Volatile.Read(ref _transactionIdentifier);
            var next = Shift(current, offset);
            if (Interlocked.CompareExchange(ref _transactionIdentifier, next, current) == current)
            {
                return current;
            }
        }
    }
}
