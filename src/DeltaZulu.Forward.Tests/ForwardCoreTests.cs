using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DeltaZulu.Forward.Tests;

[TestClass]
public sealed class ForwardCoreTests
{
    [TestMethod]
    public void AckCodecRoundTripsThroughEncoding()
    {
        var outcome = new ForwardAckOutcome(3, "rejected: schema unknown");
        var decoded = ForwardAckCodec.Decode(ForwardAckCodec.Encode(outcome));

        Assert.AreEqual(outcome.StatusCode, decoded.StatusCode);
        Assert.AreEqual(outcome.Detail, decoded.Detail);
        Assert.IsFalse(decoded.Committed);

        var committed = ForwardAckCodec.Decode(ForwardAckCodec.Encode(new ForwardAckOutcome(0, null)));
        Assert.IsTrue(committed.Committed);
        Assert.IsNull(committed.Detail);
    }

    [TestMethod]
    public void BatchEnvelopeRoundTripsThroughEncoding()
    {
        var envelope = new ForwardBatchEnvelope(Guid.NewGuid(), [1, 2, 3, 4, 5]);
        var decoded = ForwardBatchEnvelope.Decode(envelope.Encode());

        Assert.AreEqual(envelope.BatchId, decoded.BatchId);
        CollectionAssert.AreEqual(envelope.Payload, decoded.Payload);
    }

    [TestMethod]
    public void ControlMessageRoundTripsThroughEncoding()
    {
        var message = new ForwardControlMessage(ForwardControlType.WindowAdjust, 42);
        var decoded = ForwardControlMessage.Decode(message.Encode());

        Assert.AreEqual(message.ControlType, decoded.ControlType);
        Assert.AreEqual(message.Value, decoded.Value);
    }

    [TestMethod]
    public async Task ConnectionExposesConstructorStateAndRejectsNullSendBeforeOpening()
    {
        var certificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection();
        var connection = new ForwardConnection("localhost", 6514, useTls: true, certificates);
        var cts = new CancellationTokenSource();

        Assert.AreEqual("localhost", connection.Host);
        Assert.AreEqual(6514, connection.Port);
        Assert.IsTrue(connection.UseTls);
        Assert.AreSame(certificates, connection.ClientCertificates);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => connection.SendAsync(null!, cts.Token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => connection.SendAsync([], cts.Token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => connection.ReceiveAsync(cts.Token));
        await connection.DisposeAsync();
        await connection.DisposeAsync();
        foreach (var item in certificates)
        {
            item.Dispose();
        }
    }

    [TestMethod]
    public void ConnectionRejectsInvalidConstructorArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ForwardConnection("", 601));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ForwardConnection("localhost", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ForwardConnection("localhost", 65_536));
    }

    [TestMethod]
    public async Task ConnectionRejectsUseAfterDispose()
    {
        await using var connection = new ForwardConnection("localhost", 601);
        var cts = new CancellationTokenSource();
        await connection.DisposeAsync();

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.ConnectAsync(cts.Token));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => connection.SendAsync([], cts.Token));
    }

    [TestMethod]
    public async Task ConnectionReportsUnavailableReceiverOnConnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);

        await Assert.ThrowsExactlyAsync<SocketException>(() => connection.ConnectAsync(timeout.Token));
    }

    [TestMethod]
    public void CreditWindowAdjustCapacityGrowsAndWakesWaiters()
    {
        var window = new ForwardCreditWindow(1);
        window.AcquireAsync().GetAwaiter().GetResult();
        Assert.AreEqual(0, window.Available);

        var waiterTask = window.AcquireAsync();
        Assert.IsFalse(waiterTask.IsCompleted);

        window.AdjustCapacity(2);
        waiterTask.GetAwaiter().GetResult();
        Assert.AreEqual(0, window.Available);
        Assert.AreEqual(2, window.Capacity);
    }

    [TestMethod]
    public void CreditWindowAdjustCapacityCanShrinkWithoutRevokingGrantedCredits()
    {
        var window = new ForwardCreditWindow(4);
        window.AdjustCapacity(1);

        Assert.AreEqual(1, window.Capacity);
        Assert.AreEqual(1, window.Available);
    }

    [TestMethod]
    public async Task CreditWindowBlocksUntilReleased()
    {
        var window = new ForwardCreditWindow(1);
        await window.AcquireAsync();

        var blocked = window.AcquireAsync();
        await Task.Delay(50);
        Assert.IsFalse(blocked.IsCompleted);

        window.Release();
        await blocked;
    }

    [TestMethod]
    public async Task CreditWindowRespectsCancellation()
    {
        var window = new ForwardCreditWindow(0);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => window.AcquireAsync(cts.Token));
    }

    [TestMethod]
    public void DedupWindowAdmitsFirstAndRejectsDuplicate()
    {
        var window = new ForwardDedupWindow(4);
        var batchId = Guid.NewGuid();

        Assert.IsTrue(window.TryAdmit(batchId));
        Assert.IsFalse(window.TryAdmit(batchId));
        Assert.IsTrue(window.Contains(batchId));
    }

    [TestMethod]
    public void DedupWindowEvictsOldestBeyondCapacity()
    {
        var window = new ForwardDedupWindow(2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        Assert.IsTrue(window.TryAdmit(first));
        Assert.IsTrue(window.TryAdmit(second));
        Assert.IsTrue(window.TryAdmit(third));

        Assert.IsFalse(window.Contains(first));
        Assert.IsTrue(window.Contains(second));
        Assert.IsTrue(window.Contains(third));
        Assert.AreEqual(2, window.Count);
    }

    [TestMethod]
    public void DeadLetterRoundTripsThroughEncoding()
    {
        var deadLetter = new ForwardDeadLetter(Guid.NewGuid(), "unparseable timestamp", [9, 8, 7]);
        var decoded = ForwardDeadLetter.Decode(deadLetter.Encode());

        Assert.AreEqual(deadLetter.OriginalBatchId, decoded.OriginalBatchId);
        Assert.AreEqual(deadLetter.Reason, decoded.Reason);
        CollectionAssert.AreEqual(deadLetter.OriginalPayload, decoded.OriginalPayload);
    }

    [TestMethod]
    public void FrameHeaderEncodesAndDecodesRoundTrip()
    {
        var header = new ForwardFrameHeader(ForwardFrameType.TypedBatch, ForwardFrameFlags.Compressed, 1, 42, 6, 0xDEADBEEF);
        Span<byte> encoded = stackalloc byte[ForwardFrameHeader.EncodedLength];
        header.Encode(encoded);

        var decoded = ForwardFrameHeader.Decode(encoded);
        Assert.AreEqual(header, decoded);
    }

    [TestMethod]
    public void FrameReaderConsumesCompleteFrameAndLeavesRemainder()
    {
        var first = ForwardFrameTx.FromPayload(ForwardFrameType.Ack, ForwardAckCodec.Encode(new ForwardAckOutcome(0, null))).ToByteArray(2);
        var second = ForwardFrameTx.FromPayload(ForwardFrameType.Ack, ForwardAckCodec.Encode(new ForwardAckOutcome(0, null))).ToByteArray(3);
        var buffer = new ReadOnlySequence<byte>([.. first, .. second]);

        Assert.IsTrue(ForwardFrameReader.TryReadFrame(ref buffer, ForwardParserOptions.Default, out var frame));

        Assert.AreEqual(2u, frame.TransactionNumber);
        Assert.AreEqual(ForwardFrameType.Ack, frame.FrameType);
        CollectionAssert.AreEqual(second, buffer.ToArray());
    }

    [TestMethod]
    public void FrameReaderRejectsChecksumMismatch()
    {
        var bytes = ForwardFrameTx.FromPayload(ForwardFrameType.TypedBatch, [1, 2, 3]).ToByteArray(1);
        bytes[^1] ^= 0xFF;
        var buffer = new ReadOnlySequence<byte>(bytes);

        Assert.ThrowsExactly<FormatException>(() => ForwardFrameReader.TryReadFrame(ref buffer, ForwardParserOptions.Default, out _));
    }

    [TestMethod]
    public void FrameReaderRejectsOversizedFrames()
    {
        var bytes = ForwardFrameTx.FromPayload(ForwardFrameType.TypedBatch, new byte[64]).ToByteArray(1);
        var buffer = new ReadOnlySequence<byte>(bytes);
        var options = new ForwardParserOptions(ForwardFrameHeader.EncodedLength + 8);

        Assert.ThrowsExactly<FormatException>(() => ForwardFrameReader.TryReadFrame(ref buffer, options, out _));
    }

    [TestMethod]
    public void FrameReaderWaitsForIncompletePayload()
    {
        var bytes = ForwardFrameTx.FromPayload(ForwardFrameType.Ack, [1, 2, 3, 4]).ToByteArray(2);
        var buffer = new ReadOnlySequence<byte>(bytes.AsSpan(0, bytes.Length - 1).ToArray());

        Assert.IsFalse(ForwardFrameReader.TryReadFrame(ref buffer, ForwardParserOptions.Default, out _));
    }

    [TestMethod]
    public async Task FrameReaderWrapsTransportReadFailuresAsIOException()
    {
        var reader = PipeReader.Create(new ThrowingReadStream(new InvalidOperationException("network failed")));

        var ex = await Assert.ThrowsExactlyAsync<IOException>(() => ForwardFrameReader.ReadFrameAsync(reader, ForwardParserOptions.Default).AsTask());

        Assert.AreEqual("Unable to read from the DeltaZulu.Forward connection.", ex.Message);
        Assert.IsInstanceOfType<InvalidOperationException>(ex.InnerException);
        await reader.CompleteAsync();
    }

    [TestMethod]
    public void FrameTxOmitsNoBytesForEmptyPayload()
    {
        var frame = ForwardFrameTx.FromFrameType(ForwardFrameType.Close);
        var bytes = frame.ToByteArray(7);

        Assert.AreEqual(ForwardFrameHeader.EncodedLength, bytes.Length);
        var header = ForwardFrameHeader.Decode(bytes);
        Assert.AreEqual(ForwardFrameType.Close, header.FrameType);
        Assert.AreEqual(7u, header.TransactionNumber);
        Assert.AreEqual(0u, header.PayloadLength);
        Assert.AreEqual(0u, header.PayloadChecksum);
    }

    [TestMethod]
    public void FrameTxRejectsTransactionNumberBelowMinimum() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ForwardFrameTx.FromFrameType(ForwardFrameType.Close).ToByteArray(0));

    [TestMethod]
    public void HandshakeAckRoundTripsThroughEncoding()
    {
        var ack = new ForwardHandshakeAck(true, 1, Guid.NewGuid(), 64, 4096, ForwardCompression.Zstd, [123UL, 456UL], string.Empty);
        var decoded = ForwardHandshakeAck.Decode(ack.Encode());

        Assert.AreEqual(ack.Accepted, decoded.Accepted);
        Assert.AreEqual(ack.SessionId, decoded.SessionId);
        Assert.AreEqual(ack.GrantedWindowSize, decoded.GrantedWindowSize);
        Assert.AreEqual(ack.DedupWindowSize, decoded.DedupWindowSize);
        Assert.AreEqual(ack.CompressionSelected, decoded.CompressionSelected);
        CollectionAssert.AreEqual(ack.UnknownSchemaFingerprints.ToArray(), decoded.UnknownSchemaFingerprints.ToArray());
    }

    [TestMethod]
    public void HandshakeOfferRoundTripsThroughEncoding()
    {
        var offer = new ForwardHandshakeOffer(1, Guid.NewGuid(), "catalog-7", 64, 4096, ForwardCompression.None, [1UL, 2UL, 3UL]);
        var decoded = ForwardHandshakeOffer.Decode(offer.Encode());

        Assert.AreEqual(offer.ProtocolVersion, decoded.ProtocolVersion);
        Assert.AreEqual(offer.SessionResumeToken, decoded.SessionResumeToken);
        Assert.AreEqual(offer.CatalogVersion, decoded.CatalogVersion);
        Assert.AreEqual(offer.RequestedWindowSize, decoded.RequestedWindowSize);
        Assert.AreEqual(offer.DedupWindowSize, decoded.DedupWindowSize);
        Assert.AreEqual(offer.CompressionOffered, decoded.CompressionOffered);
        CollectionAssert.AreEqual(offer.KnownSchemaFingerprints.ToArray(), decoded.KnownSchemaFingerprints.ToArray());
    }

    [TestMethod]
    public void ParserHandlesFragmentedHeaderAndPayload()
    {
        var parser = new ForwardParser();
        var bytes = ForwardFrameTx.FromPayload(ForwardFrameType.RawEnvelope, "hello"u8.ToArray()).ToByteArray(9);

        foreach (var value in bytes)
        {
            parser.Parse(value);
        }

        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual(9u, parser.TransactionNumber);
        Assert.AreEqual(ForwardFrameType.RawEnvelope, parser.FrameType);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(parser.Data));
    }

    [TestMethod]
    public void ParserKeepsBytesAfterCompleteFrameForBufferedReads()
    {
        var parser = new ForwardParser();
        var first = ForwardFrameTx.FromFrameType(ForwardFrameType.Close).ToByteArray(2);
        var second = ForwardFrameTx.FromFrameType(ForwardFrameType.CloseAck).ToByteArray(3);
        parser.Parse([.. first, .. second]);

        Assert.IsTrue(parser.IsComplete);
        Assert.AreEqual(2u, parser.TransactionNumber);
        CollectionAssert.AreEqual(second, parser.RemainingBytes);
    }

    [TestMethod]
    public void ParserRejectsAdditionalInputAfterCompletion()
    {
        var parser = new ForwardParser();
        parser.Parse(ForwardFrameTx.FromFrameType(ForwardFrameType.Close).ToByteArray(2));

        Assert.ThrowsExactly<InvalidOperationException>(() => parser.Parse((byte)'x'));
    }

    [TestMethod]
    public void ParserRejectsOversizedFrames()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ForwardParser(maxFrameLength: 4));

        var oversized = new ForwardParser(maxFrameLength: ForwardFrameHeader.EncodedLength + 4);
        var bytes = ForwardFrameTx.FromPayload(ForwardFrameType.TypedBatch, new byte[16]).ToByteArray(1);
        Assert.ThrowsExactly<FormatException>(() => oversized.Parse(bytes));
    }

    [TestMethod]
    public void ParserToFrameRequiresCompletedFrame()
    {
        var parser = new ForwardParser();
        Assert.ThrowsExactly<InvalidOperationException>(() => parser.ToFrame());
    }

    [TestMethod]
    public void SchemaRequestResponseRoundTripThroughEncoding()
    {
        var request = new ForwardSchemaRequest(0xABCDEF);
        Assert.AreEqual(request.SchemaFingerprint, ForwardSchemaRequest.Decode(request.Encode()).SchemaFingerprint);

        var response = new ForwardSchemaResponse(0xABCDEF, true, [1, 2, 3]);
        var decodedResponse = ForwardSchemaResponse.Decode(response.Encode());
        Assert.AreEqual(response.SchemaFingerprint, decodedResponse.SchemaFingerprint);
        Assert.AreEqual(response.Found, decodedResponse.Found);
        CollectionAssert.AreEqual(response.SchemaBytes, decodedResponse.SchemaBytes);
    }

    [TestMethod]
    public async Task SessionAllowsMultipleBatchesInFlightUpToNegotiatedWindow()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var arrived = new ConcurrentQueue<uint>();
        var releaseFirstAck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunHoldingBatchServerAsync(listener, arrived, releaseFirstAck.Task, grantedWindow: 2, timeout.Token);

        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new ForwardSession(connection, new ForwardSessionOptions { RequestedWindowSize = 2 });
        await session.OpenAsync(timeout.Token);

        var send1 = session.SendTypedBatchAsync([1], timeout.Token);
        var send2 = session.SendTypedBatchAsync([2], timeout.Token);

        await WaitUntilAsync(() => arrived.Count == 2, timeout.Token);

        var send3 = session.SendTypedBatchAsync([3], timeout.Token);
        await Task.Delay(100, timeout.Token);
        Assert.AreEqual(2, arrived.Count, "third batch must not be sent until the window frees a credit");
        Assert.AreEqual(0, session.CreditWindow.Available);

        releaseFirstAck.TrySetResult();

        await Task.WhenAll(send1, send2, send3);
        await WaitUntilAsync(() => arrived.Count == 3, timeout.Token);

        await session.CloseAsync(timeout.Token);
        await serverTask;
    }

    [TestMethod]
    public async Task SessionCloseIgnoresAcknowledgementFromCanceledTransaction()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = RunServerThatDelaysBatchAckUntilCloseAsync(listener, timeout.Token);

        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new ForwardSession(connection, new ForwardSessionOptions { RequestedWindowSize = 4 });

        await session.OpenAsync(timeout.Token);
        using var sendTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            session.SendTypedBatchAsync([1, 2, 3], sendTimeout.Token));

        await session.CloseAsync(timeout.Token);

        await serverTask;
        Assert.IsFalse(session.IsActive);
        Assert.AreEqual(0, session.PendingAcknowledgements);
    }

    [TestMethod]
    public async Task SessionCompletesHandshakeSendAndCloseAgainstAcceptingServer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var received = new ConcurrentQueue<(ForwardFrameType FrameType, byte[] Payload)>();
        var serverTask = RunAcceptingServerAsync(listener, received, timeout.Token);

        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new ForwardSession(connection, new ForwardSessionOptions {
            CatalogVersion = "test-catalog",
            RequestedWindowSize = 8,
            DedupWindowSize = 16
        });

        await session.OpenAsync(timeout.Token);
        Assert.IsTrue(session.IsActive);
        Assert.IsNotNull(session.SessionId);

        var firstBatchId = await session.SendTypedBatchAsync("payload-one"u8.ToArray(), timeout.Token);
        var secondBatchId = await session.SendRawEnvelopeAsync("payload-two"u8.ToArray(), timeout.Token);

        await session.CloseAsync(timeout.Token);
        await serverTask;

        Assert.AreNotEqual(Guid.Empty, firstBatchId);
        Assert.AreNotEqual(Guid.Empty, secondBatchId);
        Assert.HasCount(2, received);
        Assert.IsFalse(session.IsActive);
    }

    [TestMethod]
    public async Task SessionDedupWindowSkipsReprocessingDuplicateBatchId()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var handlerInvocations = 0;
        var acceptTask = Task.Run(async () => {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var serverConnection = ForwardConnection.FromAcceptedClient(client);
            var options = new ForwardSessionOptions {
                BatchHandler = (_, _, _, _) => {
                    Interlocked.Increment(ref handlerInvocations);
                    return Task.FromResult(new ForwardAckOutcome(0, null));
                }
            };

            var serverSession = await ForwardSession.AcceptAsync(
                serverConnection,
                offer => new ForwardHandshakeAck(true, offer.ProtocolVersion, Guid.NewGuid(), offer.RequestedWindowSize, offer.DedupWindowSize, offer.CompressionOffered, [], string.Empty),
                options,
                timeout.Token);

            await (serverSession.ReceiveLoopCompletion ?? Task.CompletedTask);
        }, timeout.Token);

        // The client side is hand-rolled rather than driven through ForwardSession, because
        // ForwardSession's own background receive pump would otherwise race this test's
        // direct connection.ReadFrameAsync calls for the same incoming acknowledgement frames.
        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);

        var offer = new ForwardHandshakeOffer(ForwardFrameHeader.CurrentProtocolVersion, Guid.Empty, "test-catalog", 4, 16, ForwardCompression.None, []);
        await connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.Hello, offer.Encode()), 1, timeout.Token);
        var helloAck = await connection.ReadFrameAsync(ForwardParserOptions.Default, timeout.Token);
        Assert.AreEqual(ForwardFrameType.HelloAck, helloAck.FrameType);

        var batchId = Guid.NewGuid();
        var payload = new ForwardBatchEnvelope(batchId, [1, 2, 3]).Encode();

        // Send the same batch UUID twice, simulating an at-least-once redelivery after a lost acknowledgement.
        await connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.TypedBatch, payload), 2, timeout.Token);
        var firstAck = await connection.ReadFrameAsync(ForwardParserOptions.Default, timeout.Token);
        Assert.AreEqual(ForwardFrameType.Ack, firstAck.FrameType);
        Assert.IsTrue(ForwardAckCodec.Decode(firstAck.Payload).Committed);

        await connection.WriteFrameAsync(ForwardFrameTx.FromPayload(ForwardFrameType.TypedBatch, payload), 3, timeout.Token);
        var secondAck = await connection.ReadFrameAsync(ForwardParserOptions.Default, timeout.Token);
        Assert.AreEqual(ForwardFrameType.Ack, secondAck.FrameType);
        var secondOutcome = ForwardAckCodec.Decode(secondAck.Payload);
        Assert.IsTrue(secondOutcome.Committed);
        StringAssert.Contains(secondOutcome.Detail, "duplicate");

        await connection.WriteFrameAsync(ForwardFrameTx.FromFrameType(ForwardFrameType.Close), 4, timeout.Token);
        var closeAck = await connection.ReadFrameAsync(ForwardParserOptions.Default, timeout.Token);
        Assert.AreEqual(ForwardFrameType.CloseAck, closeAck.FrameType);

        await acceptTask;

        Assert.AreEqual(1, handlerInvocations);
    }

    [TestMethod]
    public async Task SessionOpenAsyncThrowsWhenHandshakeIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var serverTask = Task.Run(async () => {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                var parser = new ForwardParser();
                var buffer = new byte[4096];
                while (!parser.IsComplete)
                {
                    var read = await stream.ReadAsync(buffer, timeout.Token);
                    parser.Parse(buffer.AsSpan(0, read));
                }

                var ack = new ForwardHandshakeAck(false, 1, Guid.Empty, 0, 0, ForwardCompression.None, [], "catalog version not supported");
                var response = ForwardFrameTx.FromPayload(ForwardFrameType.HelloAck, ack.Encode()).ToByteArray(parser.TransactionNumber);
                await stream.WriteAsync(response, timeout.Token);
            }, timeout.Token);

            await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
            await connection.ConnectAsync(timeout.Token);
            var session = new ForwardSession(connection);

            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => session.OpenAsync(timeout.Token));
            StringAssert.Contains(ex.Message, "catalog version not supported");
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task SessionThrottleControlFrameBlocksSendUntilResumed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var arrived = new ConcurrentQueue<uint>();
        var serverReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseThrottle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunThrottlingServerAsync(listener, arrived, serverReady, releaseThrottle.Task, timeout.Token);

        await using var connection = new ForwardConnection(IPAddress.Loopback.ToString(), port);
        await connection.ConnectAsync(timeout.Token);
        var session = new ForwardSession(connection, new ForwardSessionOptions { RequestedWindowSize = 4 });
        await session.OpenAsync(timeout.Token);
        await serverReady.Task;

        // serverReady fires once the throttle-on control frame is written; wait for the
        // client's background pump to have actually read and applied it (rather than assuming
        // a fixed delay is enough) before issuing a send that must observe it as throttled.
        await WaitUntilAsync(() => session.IsThrottled, timeout.Token);

        var send = session.SendTypedBatchAsync([1], timeout.Token);
        await Task.Delay(50, timeout.Token);
        Assert.IsFalse(send.IsCompleted, "send must block while throttled");

        releaseThrottle.TrySetResult();
        await send;
        await session.CloseAsync(timeout.Token);
        await serverTask;
    }

    [TestMethod]
    public void TxNrNextIsSafeForConcurrentCallers()
    {
        var txNr = new TxNr();
        var ids = new ConcurrentBag<uint>();

        Parallel.For(0, 1_000, _ => ids.Add(txNr.Next()));

        Assert.HasCount(1_000, ids.Distinct());
    }

    [TestMethod]
    public void TxNrWrapsAroundMaximumBackToMinimum()
    {
        var txNr = new TxNr(TxNr.MaxValue);
        Assert.AreEqual(TxNr.MaxValue, txNr.Next());
        Assert.AreEqual(TxNr.MinValue, txNr.Next());
    }

    [TestMethod]
    public void WindowFaultAllFaultsEveryPendingTask()
    {
        var window = new ForwardWindow();
        var first = window.RegisterPending(1);
        var second = window.RegisterPending(2);

        window.FaultAll(new IOException("connection lost"));

        Assert.ThrowsExactly<IOException>(() => first.GetAwaiter().GetResult());
        Assert.ThrowsExactly<IOException>(() => second.GetAwaiter().GetResult());
        Assert.AreEqual(0, window.Size);
    }

    [TestMethod]
    public void WindowResolvesPendingAcknowledgementsAndIgnoresUnknownTransactions()
    {
        var window = new ForwardWindow();
        var pending = window.RegisterPending(5);

        Assert.IsTrue(window.IsPending(5));
        Assert.IsFalse(window.TryComplete(999, new ForwardAckOutcome(0, null)));
        Assert.IsTrue(window.TryComplete(5, new ForwardAckOutcome(0, "committed")));
        Assert.IsFalse(window.IsPending(5));

        var outcome = pending.GetAwaiter().GetResult();
        Assert.IsTrue(outcome.Committed);
        Assert.AreEqual("committed", outcome.Detail);
    }

    private static async Task RunAcceptingServerAsync(
        TcpListener listener,
        ConcurrentQueue<(ForwardFrameType FrameType, byte[] Payload)> received,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var serverConnection = ForwardConnection.FromAcceptedClient(client);

            var options = new ForwardSessionOptions {
                BatchHandler = (frameType, _, payload, _) => {
                    received.Enqueue((frameType, payload));
                    return Task.FromResult(new ForwardAckOutcome(0, null));
                }
            };

            var session = await ForwardSession.AcceptAsync(
                serverConnection,
                offer => new ForwardHandshakeAck(true, offer.ProtocolVersion, Guid.NewGuid(), offer.RequestedWindowSize, offer.DedupWindowSize, offer.CompressionOffered, [], string.Empty),
                options,
                cancellationToken);

            await (session.ReceiveLoopCompletion ?? Task.CompletedTask);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunHoldingBatchServerAsync(
        TcpListener listener,
        ConcurrentQueue<uint> arrived,
        Task releaseFirstAck,
        uint grantedWindow,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var parser = new ForwardParser();
            var pending = Array.Empty<byte>();
            var buffer = new byte[4096];
            var writeLock = new SemaphoreSlim(1, 1);
            var deferredAcks = new List<Task>();

            async Task WriteLockedAsync(ForwardFrameTx frame, uint transactionNumber)
            {
                await writeLock.WaitAsync(cancellationToken);
                try
                {
                    await WriteAsync(stream, frame, transactionNumber, cancellationToken);
                }
                finally
                {
                    writeLock.Release();
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (pending.Length > 0)
                {
                    parser.Parse(pending);
                    pending = [];
                }

                // Do not await anything that depends on the test's release signal here: the
                // loop must keep reading subsequent frames off the wire (including a second
                // in-flight batch) while the first batch's acknowledgement is deliberately held back.
                while (!parser.IsComplete)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    parser.Parse(buffer.AsSpan(0, read));
                }

                pending = parser.RemainingBytes;
                var frame = parser.ToFrame();
                parser = new ForwardParser();

                switch (frame.FrameType)
                {
                    case ForwardFrameType.Hello:
                        var offer = ForwardHandshakeOffer.Decode(frame.Payload);
                        var ack = new ForwardHandshakeAck(true, offer.ProtocolVersion, Guid.NewGuid(), grantedWindow, offer.DedupWindowSize, offer.CompressionOffered, [], string.Empty);
                        await WriteLockedAsync(ForwardFrameTx.FromPayload(ForwardFrameType.HelloAck, ack.Encode()), frame.TransactionNumber);
                        break;

                    case ForwardFrameType.TypedBatch:
                        // Every batch's acknowledgement is deferred behind the same shared
                        // gate: batches that arrive before the test releases the gate stay
                        // unacked (and so keep holding their credit), while batches that
                        // arrive after release are acknowledged immediately.
                        arrived.Enqueue(frame.TransactionNumber);
                        var heldTransactionNumber = frame.TransactionNumber;
                        deferredAcks.Add(Task.Run(async () => {
                            await releaseFirstAck;
                            var outcome = ForwardAckCodec.Encode(new ForwardAckOutcome(0, null));
                            await WriteLockedAsync(ForwardFrameTx.FromPayload(ForwardFrameType.Ack, outcome), heldTransactionNumber);
                        }, cancellationToken));
                        break;

                    case ForwardFrameType.Close:
                        await Task.WhenAll(deferredAcks);
                        await WriteLockedAsync(ForwardFrameTx.FromFrameType(ForwardFrameType.CloseAck), frame.TransactionNumber);
                        return;
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunServerThatDelaysBatchAckUntilCloseAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var parser = new ForwardParser();
            var pending = Array.Empty<byte>();
            var buffer = new byte[4096];
            uint? delayedTransactionNumber = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (pending.Length > 0)
                {
                    parser.Parse(pending);
                    pending = [];
                }

                while (!parser.IsComplete)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    parser.Parse(buffer.AsSpan(0, read));
                }

                pending = parser.RemainingBytes;
                var frame = parser.ToFrame();
                parser = new ForwardParser();

                switch (frame.FrameType)
                {
                    case ForwardFrameType.Hello:
                        var offer = ForwardHandshakeOffer.Decode(frame.Payload);
                        var ack = new ForwardHandshakeAck(true, offer.ProtocolVersion, Guid.NewGuid(), offer.RequestedWindowSize, offer.DedupWindowSize, offer.CompressionOffered, [], string.Empty);
                        await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.HelloAck, ack.Encode()), frame.TransactionNumber, cancellationToken);
                        break;

                    case ForwardFrameType.TypedBatch:
                        delayedTransactionNumber = frame.TransactionNumber;
                        break;

                    case ForwardFrameType.Close:
                        if (delayedTransactionNumber.HasValue)
                        {
                            var outcome = ForwardAckCodec.Encode(new ForwardAckOutcome(0, null));
                            await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.Ack, outcome), delayedTransactionNumber.Value, cancellationToken);
                        }

                        await WriteAsync(stream, ForwardFrameTx.FromFrameType(ForwardFrameType.CloseAck), frame.TransactionNumber, cancellationToken);
                        return;
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RunThrottlingServerAsync(
        TcpListener listener,
        ConcurrentQueue<uint> arrived,
        TaskCompletionSource serverReady,
        Task releaseThrottle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var parser = new ForwardParser();
            var pending = Array.Empty<byte>();
            var buffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested)
            {
                if (pending.Length > 0)
                {
                    parser.Parse(pending);
                    pending = [];
                }

                while (!parser.IsComplete)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        return;
                    }

                    parser.Parse(buffer.AsSpan(0, read));
                }

                pending = parser.RemainingBytes;
                var frame = parser.ToFrame();
                parser = new ForwardParser();

                switch (frame.FrameType)
                {
                    case ForwardFrameType.Hello:
                        var offer = ForwardHandshakeOffer.Decode(frame.Payload);
                        var ack = new ForwardHandshakeAck(true, offer.ProtocolVersion, Guid.NewGuid(), offer.RequestedWindowSize, offer.DedupWindowSize, offer.CompressionOffered, [], string.Empty);
                        await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.HelloAck, ack.Encode()), frame.TransactionNumber, cancellationToken);

                        var throttleOn = new ForwardControlMessage(ForwardControlType.Throttle, 1);
                        await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.Control, throttleOn.Encode()), 1, cancellationToken);
                        serverReady.TrySetResult();
                        await releaseThrottle;
                        var throttleOff = new ForwardControlMessage(ForwardControlType.Throttle, 0);
                        await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.Control, throttleOff.Encode()), 1, cancellationToken);
                        break;

                    case ForwardFrameType.TypedBatch:
                        arrived.Enqueue(frame.TransactionNumber);
                        var outcome = ForwardAckCodec.Encode(new ForwardAckOutcome(0, null));
                        await WriteAsync(stream, ForwardFrameTx.FromPayload(ForwardFrameType.Ack, outcome), frame.TransactionNumber, cancellationToken);
                        break;

                    case ForwardFrameType.Close:
                        await WriteAsync(stream, ForwardFrameTx.FromFrameType(ForwardFrameType.CloseAck), frame.TransactionNumber, cancellationToken);
                        return;
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WriteAsync(NetworkStream stream, ForwardFrameTx frame, uint transactionNumber, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(frame.ToByteArray(transactionNumber), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}

internal sealed class ThrowingReadStream(Exception exception) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw exception;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(exception);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
