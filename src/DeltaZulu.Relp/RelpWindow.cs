using System.Collections.Concurrent;

namespace DeltaZulu.Relp;

/// <summary>Tracks pending RELP transactions in the client window.</summary>
public sealed class RelpWindow
{
    private readonly ConcurrentDictionary<int, int> _pending = [];

    /// <summary>Gets the number of pending transactions.</summary>
    public int Size => _pending.Count;

    /// <summary>Gets the request identifier associated with a pending transaction.</summary>
    /// <param name="transactionId">The RELP transaction identifier.</param>
    /// <returns>The request identifier, or <see langword="null" /> if the transaction is not pending.</returns>
    public int? GetPending(int transactionId) => _pending.TryGetValue(transactionId, out var requestId) ? requestId : null;

    /// <summary>Determines whether the specified transaction is pending.</summary>
    /// <param name="transactionId">The RELP transaction identifier.</param>
    /// <returns><see langword="true" /> if the transaction is pending; otherwise, <see langword="false" />.</returns>
    public bool IsPending(int transactionId) => _pending.ContainsKey(transactionId);

    /// <summary>Adds or updates a pending transaction and associated request identifier.</summary>
    /// <param name="transactionId">The RELP transaction identifier.</param>
    /// <param name="requestId">The request identifier associated with the transaction.</param>
    public void PutPending(int transactionId, int requestId) => _pending[transactionId] = requestId;

    /// <summary>Removes a transaction from the pending window.</summary>
    /// <param name="transactionId">The RELP transaction identifier.</param>
    public void RemovePending(int transactionId) => _pending.TryRemove(transactionId, out _);
}
