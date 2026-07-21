using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Transport;
using System.Threading.Channels;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class RecordingWindowsIpcConnection : IWindowsIpcConnection
{
    private readonly WindowsIpcSerializer _serializer;
    private readonly Channel<IpcFrame> _readFrames = Channel.CreateUnbounded<IpcFrame>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _releaseRequestWrites = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseCancellationWrites = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseRequestWriteReturns = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _readsGate = new();
    private readonly object _writesGate = new();
    private readonly List<IpcFrame> _startedWrites = new();
    private readonly List<IpcFrame> _completedWrites = new();
    private TaskCompletionSource _readCallsChanged = NewSignal();
    private TaskCompletionSource _startedWritesChanged = NewSignal();
    private TaskCompletionSource _completedWritesChanged = NewSignal();
    private CancellationToken _lastRequestWriteToken;
    private int _readCount;
    private int _disposeStarted;

    public RecordingWindowsIpcConnection(
        WindowsIpcSerializer serializer,
        bool blockRequestWrites = false,
        bool blockCancellationWrites = false,
        bool blockRequestWriteReturns = false)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        BlockRequestWrites = blockRequestWrites;
        BlockCancellationWrites = blockCancellationWrites;
        BlockRequestWriteReturns = blockRequestWriteReturns;
        ConnectionId = Guid.NewGuid();
    }

    public Guid ConnectionId { get; }
    public bool IsConnected => Volatile.Read(ref _disposeStarted) == 0;
    public bool BlockRequestWrites { get; set; }
    public bool BlockCancellationWrites { get; set; }
    public bool BlockRequestWriteReturns { get; set; }
    public bool FailRequestWrites { get; set; }
    public bool FailCancellationWrites { get; set; }

    public CancellationToken LastRequestWriteToken
    {
        get
        {
            lock (_writesGate)
                return _lastRequestWriteToken;
        }
    }

    public IReadOnlyList<IpcFrame> StartedWrites
    {
        get
        {
            lock (_writesGate)
                return _startedWrites.ToArray();
        }
    }

    public IReadOnlyList<IpcFrame> CompletedWrites
    {
        get
        {
            lock (_writesGate)
                return _completedWrites.ToArray();
        }
    }

    public ValueTask<IpcFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        var readCount = RecordReadCall();
        if (readCount == 1)
            return new ValueTask<IpcFrame?>(CreateHandshakeResponse());

        return ReadQueuedFrameAsync(cancellationToken);
    }

    public async ValueTask WriteFrameAsync(
        IpcFrame frame,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            RecordStarted(frame, cancellationToken);
            if (frame.Header.MessageKind == IpcMessageKind.Request && BlockRequestWrites)
                await _releaseRequestWrites.Task.WaitAsync(cancellationToken);
            if (frame.Header.MessageKind == IpcMessageKind.RequestCancellation && BlockCancellationWrites)
                await _releaseCancellationWrites.Task.WaitAsync(cancellationToken);
            if (frame.Header.MessageKind == IpcMessageKind.Request && FailRequestWrites)
                throw new IOException("Request write failed.");
            if (frame.Header.MessageKind == IpcMessageKind.RequestCancellation && FailCancellationWrites)
                throw new IOException("Cancellation write failed.");

            RecordCompleted(frame);
            if (frame.Header.MessageKind == IpcMessageKind.Request && BlockRequestWriteReturns)
                await _releaseRequestWriteReturns.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void ReleaseRequestWrites() => _releaseRequestWrites.TrySetResult();

    public void ReleaseCancellationWrites() => _releaseCancellationWrites.TrySetResult();

    public void ReleaseRequestWriteReturns() => _releaseRequestWriteReturns.TrySetResult();

    public void QueueSuccessResponse(long correlationId)
    {
        var response = IpcResponseEnvelope.Success(correlationId);
        var payload = _serializer.Serialize(
            response,
            WindowsIpcJsonContext.Default.IpcResponseEnvelope);
        _readFrames.Writer.TryWrite(new IpcFrame(
            new IpcFrameHeader(
                WindowsIpcProtocol.CurrentVersion,
                IpcMessageKind.Response,
                IpcFrameFlags.None,
                correlationId,
                payload.Length),
            payload));
    }

    public void QueueResponse(IpcResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var payload = _serializer.Serialize(
            response,
            WindowsIpcJsonContext.Default.IpcResponseEnvelope);
        _readFrames.Writer.TryWrite(new IpcFrame(
            new IpcFrameHeader(
                WindowsIpcProtocol.CurrentVersion,
                IpcMessageKind.Response,
                IpcFrameFlags.None,
                response.CorrelationId,
                payload.Length),
            payload));
    }

    public async Task WaitForReadCallsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task waitTask;
            lock (_readsGate)
            {
                if (_readCount >= count)
                    return;

                waitTask = _readCallsChanged.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public async Task WaitForStartedWritesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task waitTask;
            lock (_writesGate)
            {
                if (_startedWrites.Count >= count)
                    return;

                waitTask = _startedWritesChanged.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public async Task WaitForCompletedWritesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task waitTask;
            lock (_writesGate)
            {
                if (_completedWrites.Count >= count)
                    return;

                waitTask = _completedWritesChanged.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 0)
            _readFrames.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }

    private async ValueTask<IpcFrame?> ReadQueuedFrameAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _readFrames.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    private int RecordReadCall()
    {
        TaskCompletionSource signal;
        int readCount;
        lock (_readsGate)
        {
            readCount = ++_readCount;
            signal = _readCallsChanged;
            _readCallsChanged = NewSignal();
        }

        signal.TrySetResult();
        return readCount;
    }

    private void RecordStarted(IpcFrame frame, CancellationToken cancellationToken)
    {
        TaskCompletionSource signal;
        lock (_writesGate)
        {
            _startedWrites.Add(frame);
            if (frame.Header.MessageKind == IpcMessageKind.Request)
                _lastRequestWriteToken = cancellationToken;
            signal = _startedWritesChanged;
            _startedWritesChanged = NewSignal();
        }

        signal.TrySetResult();
    }

    private void RecordCompleted(IpcFrame frame)
    {
        TaskCompletionSource signal;
        lock (_writesGate)
        {
            _completedWrites.Add(frame);
            signal = _completedWritesChanged;
            _completedWritesChanged = NewSignal();
        }

        signal.TrySetResult();
    }

    private IpcFrame CreateHandshakeResponse()
    {
        var response = new IpcHandshakeResponse(
            Accepted: true,
            WindowsIpcProtocol.CurrentVersion,
            IpcPeerRole.Agent,
            ConnectionId,
            IpcCapabilities.Control | IpcCapabilities.Status,
            Error: null);
        var payload = _serializer.Serialize(
            response,
            WindowsIpcJsonContext.Default.IpcHandshakeResponse);
        return new IpcFrame(
            new IpcFrameHeader(
                WindowsIpcProtocol.CurrentVersion,
                IpcMessageKind.HandshakeResponse,
                IpcFrameFlags.None,
                1,
                payload.Length),
            payload);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(RecordingWindowsIpcConnection));
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
