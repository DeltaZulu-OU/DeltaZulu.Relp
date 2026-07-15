using System.Reflection;
using System.Text;

namespace DeltaZulu.Relp;

/// <summary>Client-side RELP session helper that waits for application-level acknowledgements.</summary>
public sealed class RelpSession
{
    private readonly RelpConnection _connection;
    private readonly SemaphoreSlim _transactionLock = new(1, 1);
    private readonly TxId _txId = new();
    private readonly RelpWindow _window = new();

    /// <summary>Initializes a single-flight RELP session over an open connection.</summary>
    public RelpSession(RelpConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Gets a value indicating whether the RELP session is open.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the count of transactions currently awaiting acknowledgements.</summary>
    public int PendingAcknowledgements => _window.Size;

    /// <summary>Closes the RELP session if it is active.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive)
            {
                return;
            }

            var transactionId = _txId.Next();
            await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromCommand(RelpCommand.Close), transactionId, cancellationToken).ConfigureAwait(false);
            IsActive = false;
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>Opens the RELP session and verifies that the server accepts RELP version 0.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive)
            {
                throw new InvalidOperationException("Session is already active.");
            }

            var transactionId = _txId.Next();
            var openFrame = RelpFrameTx.FromCommandAndMessage(RelpCommand.Open, CreateOpenOffers());
            var response = await SendFrameAndExpectSuccessfulAckAsync(openFrame, transactionId, cancellationToken).ConfigureAwait(false);
            if (!ResponseIncludesOffer(response, "relp_version"))
            {
                var closeTransactionId = _txId.Next();
                await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromCommand(RelpCommand.Close), closeTransactionId, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("RELP server did not accept the relp_version offer.");
            }

            IsActive = true;
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>Sends one syslog message and waits for its acknowledgement before returning.</summary>
    public async Task SendMessageAsync(byte[] message, CancellationToken cancellationToken = default)
    {
        await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Session is not active.");
            }

            var transactionId = _txId.Next();
            await SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx.FromMessage(message), transactionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>Sends messages sequentially in single-flight mode. Each message is acknowledged before the next is sent.</summary>
    public async Task SendMessagesAsync(IEnumerable<byte[]> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages)
        {
            await SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] CreateOpenOffers()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
        return Encoding.UTF8.GetBytes($"relp_version=0\nrelp_software=DeltaZulu.Relp,{version},https://github.com/DeltaZulu-OU/DeltaZulu.Relp\ncommands=syslog");
    }

    private static bool ResponseIncludesOffer(RelpFrameRx response, string offerName)
    {
        var data = response.GetData();
        var acceptedOffers = data.Contains('\n') ? data[(data.IndexOf('\n') + 1)..] : string.Empty;
        return acceptedOffers.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(offer => offer.StartsWith(offerName + "=", StringComparison.Ordinal));
    }

    private async Task<RelpFrameRx> ExpectSuccessfulAckAsync(int transactionId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var response = await ReceiveFrameAsync(cancellationToken).ConfigureAwait(false);
            if (response.Command == RelpCommand.Response && response.TransactionId != transactionId && !_window.IsPending(response.TransactionId))
            {
                continue;
            }

            if (response.Command != RelpCommand.Response || response.TransactionId != transactionId || response.GetResponseCode() != 200)
            {
                throw new InvalidOperationException($"RELP transaction {transactionId} was not acknowledged successfully.");
            }

            _window.RemovePending(transactionId);
            return response;
        }
    }

    private async Task<RelpFrameRx> ReceiveFrameAsync(CancellationToken cancellationToken) =>
        await _connection.ReadFrameAsync(RelpParserOptions.Default, cancellationToken).ConfigureAwait(false);

    private async Task<RelpFrameRx> SendFrameAndExpectSuccessfulAckAsync(RelpFrameTx frame, int transactionId, CancellationToken cancellationToken)
    {
        _window.PutPending(transactionId, transactionId);
        try
        {
            await _connection.WriteFrameAsync(frame, transactionId, cancellationToken).ConfigureAwait(false);
            return await ExpectSuccessfulAckAsync(transactionId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _window.RemovePending(transactionId);
            throw;
        }
    }
}
