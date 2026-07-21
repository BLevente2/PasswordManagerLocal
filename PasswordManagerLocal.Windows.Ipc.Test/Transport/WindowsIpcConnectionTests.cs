using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Transport;

namespace PasswordManagerLocal.Windows.Ipc.Test.Transport;

[TestClass]
public sealed class WindowsIpcConnectionTests
{
    [TestMethod]
    public async Task ConcurrentWritesAreSerializedAsCompleteFrames()
    {
        await using var stream = new ConcurrencyDetectingWriteStream();
        var codec = new IpcFrameCodec();
        await using var connection = new WindowsIpcConnection(stream, codec);
        var first = CreateFrame(10, new byte[] { 1, 2, 3 });
        var second = CreateFrame(11, new byte[] { 4, 5, 6 });

        await Task.WhenAll(
            connection.WriteFrameAsync(first).AsTask(),
            connection.WriteFrameAsync(second).AsTask());

        Assert.AreEqual(1, stream.MaximumConcurrentWrites);
        await using var captured = new MemoryStream(stream.GetBytes());
        var restored = new[]
        {
            await codec.ReadAsync(captured),
            await codec.ReadAsync(captured)
        };
        Assert.IsTrue(restored.All(frame => frame is not null));
        CollectionAssert.AreEquivalent(
            new long[] { 10, 11 },
            restored.Select(frame => frame!.Header.CorrelationId).ToArray());
        foreach (var frame in restored)
        {
            var expected = frame!.Header.CorrelationId == 10 ? first : second;
            CollectionAssert.AreEqual(expected.Payload, frame.Payload);
        }
    }

    [TestMethod]
    public async Task CancelledPartialWriteClosesConnection()
    {
        await using var stream = new BlockingWriteStream();
        var connection = new WindowsIpcConnection(stream, new IpcFrameCodec());
        using var cancellationSource = new CancellationTokenSource();
        var writeTask = connection.WriteFrameAsync(
            CreateFrame(12, new byte[] { 7 }),
            cancellationSource.Token).AsTask();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await writeTask);
        Assert.IsFalse(connection.IsConnected);
        await connection.DisposeAsync();
    }

    [TestMethod]
    public async Task RepeatedDisposalPreservesTheOriginalDisposalFailure()
    {
        var expected = new IOException("stream disposal failed");
        var stream = new ThrowingDisposeStream(expected);
        var connection = new WindowsIpcConnection(stream, new IpcFrameCodec());

        var first = await Assert.ThrowsAsync<IOException>(async () =>
            await connection.DisposeAsync());
        var second = await Assert.ThrowsAsync<IOException>(async () =>
            await connection.DisposeAsync());

        Assert.AreSame(expected, first);
        Assert.AreSame(expected, second);
        Assert.AreEqual(1, stream.DisposeCallCount);
        Assert.IsFalse(connection.IsConnected);
    }

    private static IpcFrame CreateFrame(long correlationId, byte[] payload) =>
        new(
            new IpcFrameHeader(
                WindowsIpcProtocol.CurrentVersion,
                IpcMessageKind.Request,
                IpcFrameFlags.None,
                correlationId,
                payload.Length),
            payload);
}
