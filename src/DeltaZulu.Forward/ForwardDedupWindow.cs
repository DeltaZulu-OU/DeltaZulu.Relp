namespace DeltaZulu.Forward;

/// <summary>
/// A bounded, session-spanning set of recently seen batch UUIDs. At-least-once delivery makes
/// duplicate batches guaranteed rather than incidental, and per ADR-7 the receiving side (the
/// collector) is responsible for deduplicating before decode; this window is the mechanism.
/// It is not tied to any single <see cref="ForwardSession" /> so a fresh session after a
/// reconnect still rejects duplicates from the previous one.
/// </summary>
public sealed class ForwardDedupWindow
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Queue<Guid> _order;
    private readonly HashSet<Guid> _seen;

    /// <summary>Initializes a dedup window bounded to the given number of batch identifiers.</summary>
    public ForwardDedupWindow(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Dedup window capacity must be positive.");
        }

        _capacity = capacity;
        _seen = new HashSet<Guid>(capacity);
        _order = new Queue<Guid>(capacity);
    }

    /// <summary>Gets the configured window capacity.</summary>
    public int Capacity => _capacity;

    /// <summary>Gets the number of batch identifiers currently tracked.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _seen.Count;
            }
        }
    }

    /// <summary>Determines whether the given batch identifier is currently tracked as already seen.</summary>
    public bool Contains(Guid batchId)
    {
        lock (_gate)
        {
            return _seen.Contains(batchId);
        }
    }

    /// <summary>
    /// Records the batch identifier as seen if it is not already present, evicting the oldest
    /// entry once the window is full.
    /// </summary>
    /// <returns><see langword="true" /> if the batch had not been seen before (admit it for processing); <see langword="false" /> if it is a duplicate.</returns>
    public bool TryAdmit(Guid batchId)
    {
        lock (_gate)
        {
            if (!_seen.Add(batchId))
            {
                return false;
            }

            _order.Enqueue(batchId);
            if (_order.Count > _capacity)
            {
                _seen.Remove(_order.Dequeue());
            }

            return true;
        }
    }
}
