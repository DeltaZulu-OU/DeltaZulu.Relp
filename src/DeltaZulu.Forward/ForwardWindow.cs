using System.Collections.Concurrent;

namespace DeltaZulu.Forward;

/// <summary>The outcome carried by an <see cref="ForwardFrameType.Ack" /> frame.</summary>
public readonly record struct ForwardAckOutcome(byte StatusCode, string? Detail)
{
    /// <summary>Gets a value indicating whether the batch was durably committed.</summary>
    public bool Committed => StatusCode == 0;
}

/// <summary>
/// Tracks transactions awaiting acknowledgement in a DeltaZulu.Forward session's negotiated
/// window. Unlike RELP's single-flight client, multiple transactions may be pending at once,
/// up to the credit granted by <see cref="ForwardCreditWindow" />; each is resolved
/// independently as its <see cref="ForwardFrameType.Ack" /> frame arrives on the background
/// receive pump.
/// </summary>
public sealed class ForwardWindow
{
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ForwardAckOutcome>> _pending = [];

    /// <summary>Gets the number of transactions currently awaiting acknowledgement.</summary>
    public int Size => _pending.Count;

    /// <summary>Determines whether the specified transaction is pending.</summary>
    public bool IsPending(uint transactionNumber) => _pending.ContainsKey(transactionNumber);

    /// <summary>Registers a transaction as pending and returns a task that completes when it is acknowledged.</summary>
    public Task<ForwardAckOutcome> RegisterPending(uint transactionNumber)
    {
        var tcs = new TaskCompletionSource<ForwardAckOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[transactionNumber] = tcs;
        return tcs.Task;
    }

    /// <summary>Completes a pending transaction with the given acknowledgement outcome, if it is still pending.</summary>
    public bool TryComplete(uint transactionNumber, ForwardAckOutcome outcome)
    {
        if (!_pending.TryRemove(transactionNumber, out var tcs))
        {
            return false;
        }

        return tcs.TrySetResult(outcome);
    }

    /// <summary>Removes a pending transaction without completing it, faulting its task with the given exception.</summary>
    public void Fault(uint transactionNumber, Exception exception)
    {
        if (_pending.TryRemove(transactionNumber, out var tcs))
        {
            tcs.TrySetException(exception);
        }
    }

    /// <summary>Faults every pending transaction with the given exception, for example on connection loss.</summary>
    public void FaultAll(Exception exception)
    {
        foreach (var transactionNumber in _pending.Keys.ToArray())
        {
            Fault(transactionNumber, exception);
        }
    }

    /// <summary>Removes a transaction from the pending window without resolving its task.</summary>
    public void RemovePending(uint transactionNumber) => _pending.TryRemove(transactionNumber, out _);
}
