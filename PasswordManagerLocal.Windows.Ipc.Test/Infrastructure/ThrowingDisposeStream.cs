namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class ThrowingDisposeStream : MemoryStream
{
    private readonly Exception _disposeException;
    private int _disposeCallCount;

    public ThrowingDisposeStream(Exception disposeException)
    {
        _disposeException = disposeException
            ?? throw new ArgumentNullException(nameof(disposeException));
    }

    public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

    public override ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCallCount);
        return ValueTask.FromException(_disposeException);
    }
}
