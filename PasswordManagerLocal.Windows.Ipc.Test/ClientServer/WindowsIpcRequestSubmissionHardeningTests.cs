using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.Ipc.Test.ClientServer;

[TestClass]
public sealed class WindowsIpcRequestSubmissionHardeningTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task CreatedRequestCancellationCompletesLocallyAndReleasesOwnershipOnce()
    {
        using var cancellationSource = new CancellationTokenSource();
        var releaseCount = 0;
        var pending = new PendingIpcRequest(
            2,
            cancellationSource.Token,
            _ => Interlocked.Increment(ref releaseCount));
        pending.RegisterCallerCancellation(() => pending.RequestCallerCancellation());

        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.Task);
        pending.DisposeCancellationRegistration();
        Assert.AreEqual(IpcRequestSubmissionState.Completed, pending.SubmissionState);
        Assert.AreEqual(1, releaseCount);
        Assert.IsFalse(pending.RequestCallerCancellation());
        Assert.IsFalse(pending.TrySetException(new IOException("late failure")));
        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public async Task SubmissionStateTransitionsThroughSentAndCompletesOnce()
    {
        var releaseCount = 0;
        var pending = new PendingIpcRequest(
            3,
            CancellationToken.None,
            _ => Interlocked.Increment(ref releaseCount));

        Assert.AreEqual(IpcRequestSubmissionState.Created, pending.SubmissionState);
        Assert.IsTrue(pending.TryBeginSending());
        Assert.AreEqual(IpcRequestSubmissionState.Sending, pending.SubmissionState);
        Assert.IsFalse(pending.CommitSent());
        Assert.AreEqual(IpcRequestSubmissionState.Sent, pending.SubmissionState);
        Assert.IsTrue(pending.TrySetResult(IpcResponseEnvelope.Success(pending.CorrelationId)));
        Assert.AreEqual(IpcRequestSubmissionState.Completed, pending.SubmissionState);
        Assert.IsTrue((await pending.Task).IsSuccess);
        Assert.IsFalse(pending.TrySetException(new IOException("late failure")));
        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public async Task ResponseDuringSendingIsDeferredUntilSendCommit()
    {
        var releaseCount = 0;
        var pending = new PendingIpcRequest(
            4,
            CancellationToken.None,
            _ => Interlocked.Increment(ref releaseCount));
        var response = IpcResponseEnvelope.Success(pending.CorrelationId);

        Assert.IsTrue(pending.TryBeginSending());
        Assert.IsTrue(pending.TrySetResult(response));
        Assert.AreEqual(IpcRequestSubmissionState.Sending, pending.SubmissionState);
        Assert.IsFalse(pending.Task.IsCompleted);
        Assert.IsFalse(pending.CommitSent());

        Assert.AreEqual(IpcRequestSubmissionState.Completed, pending.SubmissionState);
        Assert.AreSame(response, await pending.Task);
        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    public async Task SendFailureCompletesDirectlyFromSending()
    {
        var releaseCount = 0;
        var failure = new IOException("send failed");
        var pending = new PendingIpcRequest(
            4,
            CancellationToken.None,
            _ => Interlocked.Increment(ref releaseCount));

        Assert.IsTrue(pending.TryBeginSending());
        Assert.IsTrue(pending.TrySetException(failure));

        var observed = await Assert.ThrowsAsync<IOException>(async () => await pending.Task);
        Assert.AreSame(failure, observed);
        Assert.AreEqual(IpcRequestSubmissionState.Completed, pending.SubmissionState);
        Assert.IsFalse(pending.CommitSent());
        Assert.AreEqual(1, releaseCount);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task PreCancelledSendWritesNoRequestAndLeavesCapacityAvailable()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.SendAsync(
                IpcOperationId.Ping,
                cancellationToken: cancellationSource.Token));

        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame => frame.Header.MessageKind == IpcMessageKind.Request));
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));

        var followUp = client.SendAsync(IpcOperationId.Ping);
        await connection.WaitForCompletedWritesAsync(2);
        var correlationId = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request).Header.CorrelationId;
        connection.QueueSuccessResponse(correlationId);
        Assert.IsTrue((await followUp).IsSuccess);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task CancellationDuringBlockedSendCommitsRequestBeforeCancellationFrame()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWrites: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForStartedWritesAsync(2);

        cancellationSource.Cancel();

        Assert.IsFalse(requestTask.IsCompleted);
        Assert.IsFalse(connection.LastRequestWriteToken.IsCancellationRequested);
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));

        connection.ReleaseRequestWrites();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask);
        await connection.WaitForCompletedWritesAsync(3);

        var completed = connection.CompletedWrites;
        var requestIndex = completed.ToList().FindIndex(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);
        var cancellationIndex = completed.ToList().FindIndex(frame =>
            frame.Header.MessageKind == IpcMessageKind.RequestCancellation);
        Assert.IsTrue(requestIndex >= 0);
        Assert.IsTrue(cancellationIndex > requestIndex);
        Assert.AreEqual(
            completed[requestIndex].Header.CorrelationId,
            completed[cancellationIndex].Header.CorrelationId);
        Assert.AreEqual(0, client.PendingRequestCount);

        var followUp = client.SendAsync(IpcOperationId.Ping);
        await connection.WaitForCompletedWritesAsync(4);
        var followUpRequest = connection.CompletedWrites.Last(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);
        connection.QueueSuccessResponse(followUpRequest.Header.CorrelationId);
        Assert.IsTrue((await followUp).IsSuccess);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task CancellationIntentDuringSendWinsOverLaterResponse()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWriteReturns: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);

        cancellationSource.Cancel();
        connection.QueueSuccessResponse(request.Header.CorrelationId);
        await connection.WaitForReadCallsAsync(3);

        Assert.IsFalse(requestTask.IsCompleted);
        connection.ReleaseRequestWriteReturns();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask);
        await connection.WaitForCompletedWritesAsync(3);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.AreEqual(
            1,
            connection.CompletedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ResponseInCommitGapWinsBeforeCallerCancellation()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWriteReturns: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);

        connection.QueueSuccessResponse(request.Header.CorrelationId);
        await connection.WaitForReadCallsAsync(3);
        Assert.IsFalse(requestTask.IsCompleted);
        cancellationSource.Cancel();
        connection.ReleaseRequestWriteReturns();

        Assert.IsTrue((await requestTask).IsSuccess);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task CancellationAfterCommitSendsOnlyOneCancellationFrame()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer);
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);

        cancellationSource.Cancel();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask);
        await connection.WaitForCompletedWritesAsync(3);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);
        var cancellations = connection.CompletedWrites.Where(frame =>
            frame.Header.MessageKind == IpcMessageKind.RequestCancellation).ToArray();
        Assert.AreEqual(1, cancellations.Length);
        Assert.AreEqual(request.Header.CorrelationId, cancellations[0].Header.CorrelationId);
        Assert.AreEqual(0, client.PendingRequestCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task AcceptedResponseWinsBeforeCallerCancellation()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer);
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);
        connection.QueueSuccessResponse(request.Header.CorrelationId);

        Assert.IsTrue((await requestTask).IsSuccess);
        cancellationSource.Cancel();

        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
        Assert.AreEqual(0, client.PendingRequestCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task CancellationFrameFailureDoesNotReplaceLocalCancellation()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer)
        {
            FailCancellationWrites = true
        };
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);

        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask);
        await connection.WaitForStartedWritesAsync(3);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.AreEqual(
            1,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ResponseDuringCancellationWriteCannotDoubleCompleteRequest()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockCancellationWrites: true);
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);

        cancellationSource.Cancel();
        await connection.WaitForStartedWritesAsync(3);
        connection.QueueSuccessResponse(request.Header.CorrelationId);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask);
        connection.ReleaseCancellationWrites();
        await connection.WaitForCompletedWritesAsync(3);
        Assert.AreEqual(0, client.PendingRequestCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task SendFailureWinsOverCancellationRecordedDuringTransmission()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWrites: true)
        {
            FailRequestWrites = true
        };
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForStartedWritesAsync(2);

        cancellationSource.Cancel();
        connection.ReleaseRequestWrites();

        await Assert.ThrowsAsync<IOException>(async () => await requestTask);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task DisposeInterruptsBlockedRequestWriteAndIsIdempotent()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWrites: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        var requestTask = client.SendAsync(IpcOperationId.Ping);
        await connection.WaitForStartedWritesAsync(2);
        var requestWriteToken = connection.LastRequestWriteToken;

        var firstDispose = client.DisposeAsync().AsTask();
        var secondDispose = client.DisposeAsync().AsTask();
        Exception? requestFailure = null;
        try
        {
            await requestTask;
        }
        catch (Exception exception)
        {
            requestFailure = exception;
        }

        await firstDispose;
        await secondDispose;
        Assert.IsNotNull(requestFailure);
        Assert.IsTrue(
            requestFailure is OperationCanceledException or IpcConnectionClosedException);
        Assert.IsTrue(requestWriteToken.IsCancellationRequested);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.IsFalse(connection.IsConnected);
        Assert.AreEqual(
            0,
            connection.CompletedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.Request));
        Assert.AreEqual(
            0,
            connection.StartedWrites.Count(frame =>
                frame.Header.MessageKind == IpcMessageKind.RequestCancellation));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ReadLoopFailureInterruptsBlockedRequestWrite()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWrites: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 1);
        await client.HandshakeAsync();
        var requestTask = client.SendAsync(IpcOperationId.Ping);
        await connection.WaitForStartedWritesAsync(2);
        var requestWriteToken = connection.LastRequestWriteToken;

        await connection.DisposeAsync();

        Exception? observed = null;
        try
        {
            await requestTask;
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Assert.IsNotNull(observed);
        Assert.IsTrue(
            observed is OperationCanceledException or IpcConnectionClosedException);
        Assert.IsTrue(requestWriteToken.IsCancellationRequested);
        Assert.AreEqual(0, client.PendingRequestCount);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task DisconnectRacingCallerCancellationCompletesExactlyOnce()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer);
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        using var cancellationSource = new CancellationTokenSource();
        var requestTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: cancellationSource.Token);
        await connection.WaitForCompletedWritesAsync(2);

        await Task.WhenAll(
            Task.Run(cancellationSource.Cancel),
            connection.DisposeAsync().AsTask());

        Exception? observed = null;
        try
        {
            await requestTask;
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        Assert.IsNotNull(observed);
        Assert.IsTrue(observed is OperationCanceledException or IpcConnectionClosedException);
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.IsTrue(connection.StartedWrites.Count(frame =>
            frame.Header.MessageKind == IpcMessageKind.RequestCancellation) <= 1);
        await client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task DisposeRacingAcceptedResponseCompletesExactlyOnce()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(serializer);
        var client = CreateClient(connection, serializer);
        await client.HandshakeAsync();
        var requestTask = client.SendAsync(IpcOperationId.Ping);
        await connection.WaitForCompletedWritesAsync(2);
        var request = connection.CompletedWrites.Single(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request);

        connection.QueueSuccessResponse(request.Header.CorrelationId);
        var firstDispose = client.DisposeAsync().AsTask();
        var secondDispose = client.DisposeAsync().AsTask();
        try
        {
            await requestTask;
        }
        catch (IpcConnectionClosedException)
        {
        }

        await firstDispose;
        await secondDispose;
        Assert.AreEqual(0, client.PendingRequestCount);
        Assert.IsFalse(connection.IsConnected);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task ConcurrentRequestsPreservePerRequestCancellationOrdering()
    {
        var serializer = new WindowsIpcSerializer();
        var connection = new RecordingWindowsIpcConnection(
            serializer,
            blockRequestWrites: true);
        var client = CreateClient(connection, serializer, maximumPendingRequests: 2);
        await client.HandshakeAsync();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var firstTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: firstCancellation.Token);
        var secondTask = client.SendAsync(
            IpcOperationId.Ping,
            cancellationToken: secondCancellation.Token);
        await connection.WaitForStartedWritesAsync(2);

        firstCancellation.Cancel();
        secondCancellation.Cancel();
        connection.ReleaseRequestWrites();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await firstTask);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await secondTask);
        await connection.WaitForStartedWritesAsync(5);
        var writes = connection.StartedWrites;
        var requests = writes.Where(frame =>
            frame.Header.MessageKind == IpcMessageKind.Request).ToArray();
        var cancellations = writes.Where(frame =>
            frame.Header.MessageKind == IpcMessageKind.RequestCancellation).ToArray();
        Assert.AreEqual(2, requests.Length);
        Assert.AreEqual(2, cancellations.Length);
        foreach (var request in requests)
        {
            var requestIndex = writes.ToList().IndexOf(request);
            var cancellation = cancellations.Single(frame =>
                frame.Header.CorrelationId == request.Header.CorrelationId);
            var cancellationIndex = writes.ToList().IndexOf(cancellation);
            Assert.IsTrue(cancellationIndex > requestIndex);
        }

        Assert.AreEqual(0, client.PendingRequestCount);
        await client.DisposeAsync();
    }

    private static WindowsIpcClient CreateClient(
        RecordingWindowsIpcConnection connection,
        WindowsIpcSerializer serializer,
        int maximumPendingRequests = WindowsIpcClientOptions.DefaultMaximumPendingRequests) =>
        new(
            connection,
            serializer,
            new WindowsIpcClientOptions(
                IpcPeerRole.Ui,
                IpcPeerRole.Agent,
                IpcCapabilities.Control | IpcCapabilities.Status,
                Environment.ProcessId,
                Guid.NewGuid(),
                maximumPendingRequests));
}
