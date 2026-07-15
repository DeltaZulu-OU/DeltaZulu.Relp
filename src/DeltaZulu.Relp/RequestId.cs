namespace DeltaZulu.Relp;

/// <summary>Generates monotonically increasing request identifiers.</summary>
public sealed class RequestId
{
    private int _requestIdentifier;

    /// <summary>Gets the next request identifier.</summary>
    /// <returns>The next zero-based request identifier.</returns>
    public int Next() => Interlocked.Increment(ref _requestIdentifier) - 1;
}
