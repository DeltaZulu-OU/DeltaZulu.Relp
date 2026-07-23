using System.Collections.Concurrent;

namespace DeltaZulu.Forward;

/// <summary>
/// A DeltaZulu.Forward session over an open <see cref="ForwardConnection" />. Harvested from
/// RELP: application-level acknowledgements bound to durable commit, per-frame transaction
/// numbers, negotiated windowing, and an offer/capability handshake. Unlike RELP's
/// single-flight client, multiple batches may be in flight at once, up to the negotiated
/// window, and the session runs a background pump that dispatches acknowledgements, control
/// (backpressure) frames, schema request/response, and dead-letter frames concurrently with
/// sends.
/// </summary>
public sealed class ForwardSession : IAsyncDisposable
{
    private readonly ForwardConnection _connection;
    private readonly ForwardCreditWindow _creditWindow = new(0);
    private readonly ForwardSessionOptions _options;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<ForwardSchemaResponse>> _schemaPending = [];
    private readonly object _stateGate = new();
    private readonly object _throttleGate = new();
    private readonly TxNr _txNr = new();
    private readonly ForwardWindow _window = new();
    private TaskCompletionSource<bool>? _closeAckSignal;
    private ForwardDedupWindow? _dedupWindow;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private TaskCompletionSource<bool>? _throttleSignal;

    /// <summary>Initializes a session over an open connection.</summary>
    public ForwardSession(ForwardConnection connection, ForwardSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _options = options ?? new ForwardSessionOptions();
    }

    /// <summary>Raised when the peer forwards a dead-letter record.</summary>
    public event Action<ForwardDeadLetter>? DeadLetterReceived;

    /// <summary>Gets the negotiated in-flight window credit tracker.</summary>
    public ForwardCreditWindow CreditWindow => _creditWindow;

    /// <summary>Gets a value indicating whether the session is open.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets a value indicating whether the peer has throttled this session via a <see cref="ForwardFrameType.Control" /> throttle frame; sends block until it clears.</summary>
    public bool IsThrottled
    {
        get
        {
            lock (_throttleGate)
            {
                return _throttleSignal is not null;
            }
        }
    }

    /// <summary>Gets the number of transactions currently awaiting acknowledgement.</summary>
    public int PendingAcknowledgements => _window.Size;

    /// <summary>Gets the background receive pump's completion task, or <see langword="null" /> if the session has not been opened. Completes when the peer closes the session, the connection faults, or the session is disposed.</summary>
    public Task? ReceiveLoopCompletion => _pumpTask;

    /// <summary>Gets the session identifier assigned by the peer, once the handshake has completed.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>
    /// Accepts an inbound handshake offer on an already-connected socket and starts the
    /// background receive pump, for the collector/server role that receives a
    /// <see cref="ForwardFrameType.Hello" /> rather than sending one. <paramref name="negotiate" />
    /// decides whether to accept the offer and builds the <see cref="ForwardHandshakeAck" />
    /// (session id, granted window, granted dedup window, and any unrecognized schema
    /// fingerprints to request from the peer).
    /// </summary>
    public static async Task<ForwardSession> AcceptAsync(
        ForwardConnection connection,
        Func<ForwardHandshakeOffer, ForwardHandshakeAck> negotiate,
        ForwardSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(negotiate);

        var sessionOptions = options ?? new ForwardSessionOptions();
        var parserOptions = new ForwardParserOptions(sessionOptions.MaxFrameLength);

        var request = await connection.ReadFrameAsync(parserOptions, cancellationToken).ConfigureAwait(false);
        if (request.FrameType != ForwardFrameType.Hello)
        {
            throw new InvalidOperationException("Expected a Hello frame to open a DeltaZulu.Forward session.");
        }

        var offer = ForwardHandshakeOffer.Decode(request.Payload);
        var ack = negotiate(offer);

        await connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.HelloAck, ack.Encode()), request.TransactionNumber, cancellationToken).ConfigureAwait(false);

        if (!ack.Accepted)
        {
            throw new InvalidOperationException($"DeltaZulu.Forward handshake offer was rejected locally: {ack.RejectReason}");
        }

        var session = new ForwardSession(connection, sessionOptions) {
            SessionId = ack.SessionId
        };
        session._creditWindow.AdjustCapacity((int)sessionOptions.RequestedWindowSize);
        session._dedupWindow = new ForwardDedupWindow(Math.Max(1, (int)ack.DedupWindowSize));
        session.IsActive = true;
        session._pumpCts = new CancellationTokenSource();
        session._pumpTask = Task.Run(() => session.RunReceiveLoopAsync(parserOptions, session._pumpCts.Token), session._pumpCts.Token);
        return session;
    }

    /// <summary>Requests an orderly session shutdown and waits for the peer's close acknowledgement.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (!IsActive)
            {
                return;
            }
        }

        var closeAckSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closeAckSignal = closeAckSignal;

        var transactionNumber = _txNr.Next();
        await _connection.WriteFrameAsync(ForwardFrameTx.FromFrameType(ForwardFrameType.Close), transactionNumber, cancellationToken).ConfigureAwait(false);

        try
        {
            await closeAckSignal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await StopPumpAsync().ConfigureAwait(false);
            lock (_stateGate)
            {
                IsActive = false;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopPumpAsync().ConfigureAwait(false);
        lock (_stateGate)
        {
            IsActive = false;
        }
    }

    /// <summary>Performs the typed handshake and, once accepted, starts the background receive pump.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (IsActive)
            {
                throw new InvalidOperationException("Session is already active.");
            }
        }

        var offer = new ForwardHandshakeOffer(
            ForwardFrameHeader.CurrentProtocolVersion,
            _options.SessionResumeToken,
            _options.CatalogVersion,
            _options.RequestedWindowSize,
            _options.DedupWindowSize,
            _options.CompressionOffered,
            _options.KnownSchemaFingerprints);

        var parserOptions = new ForwardParserOptions(_options.MaxFrameLength);
        var openTransactionNumber = _txNr.Next();
        await _connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.Hello, offer.Encode()), openTransactionNumber, cancellationToken).ConfigureAwait(false);

        var response = await _connection.ReadFrameAsync(parserOptions, cancellationToken).ConfigureAwait(false);
        if (response.FrameType != ForwardFrameType.HelloAck || response.TransactionNumber != openTransactionNumber)
        {
            throw new InvalidOperationException("Expected a HelloAck in response to the DeltaZulu.Forward handshake offer.");
        }

        var ack = ForwardHandshakeAck.Decode(response.Payload);
        if (!ack.Accepted)
        {
            throw new InvalidOperationException($"DeltaZulu.Forward handshake was rejected: {ack.RejectReason}");
        }

        SessionId = ack.SessionId;
        _creditWindow.AdjustCapacity((int)ack.GrantedWindowSize);
        _dedupWindow = new ForwardDedupWindow(Math.Max(1, (int)ack.DedupWindowSize));

        foreach (var fingerprint in ack.UnknownSchemaFingerprints)
        {
            await ServeSchemaRequestAsync(_txNr.Next(), new ForwardSchemaRequest(fingerprint), cancellationToken).ConfigureAwait(false);
        }

        lock (_stateGate)
        {
            IsActive = true;
        }

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => RunReceiveLoopAsync(parserOptions, _pumpCts.Token), _pumpCts.Token);
    }

    /// <summary>Requests the schema bytes for a fingerprint the peer might recognize.</summary>
    public async Task<byte[]?> RequestSchemaAsync(ulong schemaFingerprint, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        var transactionNumber = _txNr.Next();
        var tcs = new TaskCompletionSource<ForwardSchemaResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _schemaPending[transactionNumber] = tcs;

        await _connection.WriteFrameAsync(
            ForwardFrameTx.FromPayload(ForwardFrameType.SchemaRequest, new ForwardSchemaRequest(schemaFingerprint).Encode()),
            transactionNumber,
            cancellationToken).ConfigureAwait(false);

        var response = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return response.Found ? response.SchemaBytes : null;
    }

    /// <summary>Forwards a batch that failed parsing or validation, with its original bytes and an error reason.</summary>
    public async Task SendDeadLetterAsync(Guid originalBatchId, string reason, byte[] originalPayload, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        var transactionNumber = _txNr.Next();
        var payload = new ForwardDeadLetter(originalBatchId, reason, originalPayload).Encode();
        await _connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.DeadLetterForward, payload), transactionNumber, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends one raw-envelope batch (bytes plus source metadata carried in the payload) and waits for its durable-commit acknowledgement.</summary>
    public Task<Guid> SendRawEnvelopeAsync(byte[] payload, CancellationToken cancellationToken = default) =>
        SendBatchAsync(ForwardFrameType.RawEnvelope, payload, cancellationToken);

    /// <summary>Sends one MessagePack-encoded <c>ForwardLogBatch</c> and waits for its durable-commit acknowledgement.</summary>
    public Task<Guid> SendTypedBatchAsync(byte[] payload, CancellationToken cancellationToken = default) =>
        SendBatchAsync(ForwardFrameType.TypedBatch, payload, cancellationToken);

    private async Task<Guid> SendBatchAsync(ForwardFrameType frameType, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EnsureActive();

        await WaitWhileThrottledAsync(cancellationToken).ConfigureAwait(false);
        await _creditWindow.AcquireAsync(cancellationToken).ConfigureAwait(false);

        var batchId = Guid.NewGuid();
        var transactionNumber = _txNr.Next();
        var ackTask = _window.RegisterPending(transactionNumber);

        try
        {
            var encoded = new ForwardBatchEnvelope(batchId, payload).Encode();
            await _connection.WriteFrameAsync(ForwardFrameTx.FromPayload(frameType, encoded), transactionNumber, cancellationToken).ConfigureAwait(false);

            var outcome = await ackTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!outcome.Committed)
            {
                throw new InvalidOperationException($"Batch {batchId} was not durably committed (status {outcome.StatusCode}: {outcome.Detail}).");
            }

            return batchId;
        }
        catch
        {
            _window.RemovePending(transactionNumber);
            throw;
        }
        finally
        {
            _creditWindow.Release();
        }
    }

    private void EnsureActive()
    {
        lock (_stateGate)
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("Session is not active.");
            }
        }
    }

    private async Task RunReceiveLoopAsync(ForwardParserOptions parserOptions, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ForwardFrameRx frame;
                try
                {
                    frame = await _connection.ReadFrameAsync(parserOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or FormatException)
                {
                    _window.FaultAll(ex);
                    return;
                }

                try
                {
                    switch (frame.FrameType)
                    {
                        case ForwardFrameType.Ack:
                            _window.TryComplete(frame.TransactionNumber, ForwardAckCodec.Decode(frame.Payload));
                            break;

                        case ForwardFrameType.Control:
                            HandleControl(ForwardControlMessage.Decode(frame.Payload));
                            break;

                        case ForwardFrameType.SchemaRequest:
                            await ServeSchemaRequestAsync(frame.TransactionNumber, ForwardSchemaRequest.Decode(frame.Payload), cancellationToken).ConfigureAwait(false);
                            break;

                        case ForwardFrameType.SchemaResponse:
                            if (_schemaPending.TryRemove(frame.TransactionNumber, out var schemaTcs))
                            {
                                schemaTcs.TrySetResult(ForwardSchemaResponse.Decode(frame.Payload));
                            }

                            break;

                        case ForwardFrameType.DeadLetterForward:
                            var deadLetter = ForwardDeadLetter.Decode(frame.Payload);
                            DeadLetterReceived?.Invoke(deadLetter);
                            _options.DeadLetterHandler?.Invoke(deadLetter);
                            break;

                        case ForwardFrameType.TypedBatch:
                        case ForwardFrameType.RawEnvelope:
                            await HandleInboundBatchAsync(frame, cancellationToken).ConfigureAwait(false);
                            break;

                        case ForwardFrameType.Close:
                            await _connection.WriteFrameAsync(ForwardFrameTx.FromFrameType(ForwardFrameType.CloseAck), frame.TransactionNumber, cancellationToken).ConfigureAwait(false);
                            lock (_stateGate)
                            {
                                IsActive = false;
                            }

                            return;

                        case ForwardFrameType.CloseAck:
                            _closeAckSignal?.TrySetResult(true);
                            break;

                        default:
                            // Hello/HelloAck are only expected during OpenAsync's inline exchange; anything else is a protocol violation.
                            _window.FaultAll(new InvalidOperationException($"Unexpected DeltaZulu.Forward frame type {frame.FrameType} after handshake."));
                            return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A malformed frame or a failing handler must not silently kill the pump:
                    // every batch or schema request still waiting on this session would then
                    // hang forever with no signal that it never will complete.
                    _window.FaultAll(ex);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Session is being closed or disposed.
        }
    }

    private void HandleControl(ForwardControlMessage message)
    {
        switch (message.ControlType)
        {
            case ForwardControlType.WindowAdjust:
                _creditWindow.AdjustCapacity((int)message.Value);
                break;

            case ForwardControlType.Throttle:
                lock (_throttleGate)
                {
                    if (message.Value > 0)
                    {
                        _throttleSignal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    else
                    {
                        _throttleSignal?.TrySetResult(true);
                        _throttleSignal = null;
                    }
                }

                break;
        }
    }

    private async Task HandleInboundBatchAsync(ForwardFrameRx frame, CancellationToken cancellationToken)
    {
        var envelope = ForwardBatchEnvelope.Decode(frame.Payload);
        var admitted = _dedupWindow?.TryAdmit(envelope.BatchId) ?? true;

        ForwardAckOutcome outcome;
        if (!admitted)
        {
            outcome = new ForwardAckOutcome(0, "duplicate batch, already committed");
        }
        else if (_options.BatchHandler is { } handler)
        {
            outcome = await handler(frame.FrameType, envelope.BatchId, envelope.Payload, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            outcome = new ForwardAckOutcome(2, "no batch handler configured");
        }

        await _connection.WriteFrameAsync(
            ForwardFrameTx.FromPayload(ForwardFrameType.Ack, ForwardAckCodec.Encode(outcome)),
            frame.TransactionNumber,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ServeSchemaRequestAsync(uint transactionNumber, ForwardSchemaRequest request, CancellationToken cancellationToken)
    {
        byte[]? schemaBytes = null;
        if (_options.SchemaResolver is { } resolver)
        {
            schemaBytes = await resolver(request.SchemaFingerprint, cancellationToken).ConfigureAwait(false);
        }

        var response = new ForwardSchemaResponse(request.SchemaFingerprint, schemaBytes is not null, schemaBytes ?? []);
        await _connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.SchemaResponse, response.Encode()), transactionNumber, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopPumpAsync()
    {
        if (_pumpCts is null)
        {
            return;
        }

        await _pumpCts.CancelAsync().ConfigureAwait(false);
        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _pumpCts.Dispose();
        _pumpCts = null;
        _pumpTask = null;
    }

    private async Task WaitWhileThrottledAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? wait;
            lock (_throttleGate)
            {
                wait = _throttleSignal?.Task;
            }

            if (wait is null)
            {
                return;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
