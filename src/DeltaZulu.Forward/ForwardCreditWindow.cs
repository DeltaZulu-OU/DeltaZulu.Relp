namespace DeltaZulu.Forward;

/// <summary>
/// A dynamically adjustable in-flight-frame credit window. DeltaZulu.Forward negotiates an
/// initial window size during the handshake and adjusts it afterward with
/// <see cref="ForwardFrameType.Control" /> window-adjustment or throttle frames, so unlike a
/// fixed-capacity semaphore this window's capacity can grow or shrink for the lifetime of the
/// session as the peer signals backpressure.
/// </summary>
public sealed class ForwardCreditWindow
{
    private readonly object _gate = new();
    private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
    private int _available;
    private int _capacity;

    /// <summary>Initializes the window with the given initial capacity.</summary>
    public ForwardCreditWindow(int initialCapacity)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity must not be negative.");
        }

        _capacity = initialCapacity;
        _available = initialCapacity;
    }

    /// <summary>Gets the number of credits currently free to acquire.</summary>
    public int Available
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    /// <summary>Gets the current total window capacity.</summary>
    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }
    }

    /// <summary>Acquires one credit, waiting if the window is fully utilized.</summary>
    public async Task AcquireAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> waiter;
        lock (_gate)
        {
            if (_available > 0)
            {
                _available--;
                return;
            }

            waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
        }

        using var registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(), waiter);
        await waiter.Task.ConfigureAwait(false);
    }

    /// <summary>Releases one credit back to the window, handing it directly to a waiter if one is queued.</summary>
    public void Release()
    {
        lock (_gate)
        {
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.TrySetResult(true))
                {
                    return;
                }
            }

            _available++;
        }
    }

    /// <summary>
    /// Sets a new total capacity in response to a peer's window-adjustment control frame.
    /// Growing the window immediately wakes queued waiters; shrinking it only reduces future
    /// availability, never revokes credits already granted.
    /// </summary>
    public void AdjustCapacity(int newCapacity)
    {
        if (newCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newCapacity), "Window capacity must not be negative.");
        }

        lock (_gate)
        {
            var delta = newCapacity - _capacity;
            _capacity = newCapacity;

            if (delta <= 0)
            {
                _available = Math.Max(0, _available + delta);
                return;
            }

            _available += delta;
            while (_available > 0 && _waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.TrySetResult(true))
                {
                    _available--;
                }
            }
        }
    }
}
