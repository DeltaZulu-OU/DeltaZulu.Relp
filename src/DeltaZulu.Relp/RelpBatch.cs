using System.Collections.Concurrent;

namespace DeltaZulu.Relp;

/// <summary>Represents a RELP API type.</summary>
public sealed class RelpBatch
{
    private readonly RequestId _requestId = new();
    private readonly ConcurrentDictionary<int, RelpFrameTx> _requests = [];
    private readonly ConcurrentDictionary<int, RelpFrameRx> _responses = [];
    private readonly ConcurrentDictionary<int, byte> _workQueue = [];

    /// <summary>Provides a RELP API operation.</summary>
    public IReadOnlyCollection<int> WorkQueue => _workQueue.Keys.ToArray();

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameTx? GetRequest(int id) => _requests.GetValueOrDefault(id);

    /// <summary>Provides a RELP API operation.</summary>
    public RelpFrameRx? GetResponse(int id) => _responses.GetValueOrDefault(id);

    /// <summary>Provides a RELP API operation.</summary>
    public int Insert(byte[] syslogMessage) => PutRequest(RelpFrameTx.FromMessage(syslogMessage));

    /// <summary>Provides a RELP API operation.</summary>
    public int PutRequest(RelpFrameTx request)
    {
        var id = _requestId.Next();
        _requests[id] = request;
        _workQueue[id] = 0;
        return id;
    }

    /// <summary>Provides a RELP API operation.</summary>
    public void PutResponse(int id, RelpFrameRx response)
    {
        if (_requests.ContainsKey(id))
        {
            _responses[id] = response;
        }
    }

    /// <summary>Provides a RELP API operation.</summary>
    public void RemoveRequest(int id)
    {
        _requests.TryRemove(id, out _);
        _workQueue.TryRemove(id, out _);
    }

    /// <summary>Provides a RELP API operation.</summary>
    public void RemoveTransaction(int id)
    {
        RemoveRequest(id);
        _responses.TryRemove(id, out _);
    }

    /// <summary>Provides a RELP API operation.</summary>
    public bool VerifyAllTransactions() => _requests.Keys.All(VerifyTransaction);

    /// <summary>Provides a RELP API operation.</summary>
    public bool VerifyTransaction(int id) =>
        _requests.ContainsKey(id) &&
        _responses.TryGetValue(id, out var response) &&
        response.GetResponseCode() == 200;
}
