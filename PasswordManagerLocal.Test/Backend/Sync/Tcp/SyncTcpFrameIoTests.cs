using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Tcp;
using System.Buffers.Binary;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Sync.Tcp;

[TestClass]
public sealed class SyncTcpFrameIoTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task WriteThenRead_RoundTripsMessageTypeAndProtobufPayload()
    {
        var message = new HelloRequest
        {
            UserId = Guid.NewGuid().ToString("N"),
            DeviceId = Guid.NewGuid().ToString("N"),
            DatasetHash = Google.Protobuf.ByteString.CopyFrom(new byte[] { 1, 2, 3 }),
            SignPub = Google.Protobuf.ByteString.CopyFrom(Enumerable.Repeat((byte)4, 32).ToArray()),
            DatabaseVersion = DatabaseConstants.CurrentDbVersion,
            ProtocolVersion = SyncConstants.SyncProtocolVersion
        };
        await using var stream = new MemoryStream();

        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.HelloRequest, message, CancellationToken.None);
        stream.Position = 0;
        var frame = await SyncTcpFrameIo.ReadAsync(stream, CancellationToken.None);

        MSTestAssert.IsNotNull(frame);
        MSTestAssert.AreEqual(SyncTcpMessageType.HelloRequest, frame.Type);
        var restored = frame.Parse(HelloRequest.Parser);
        MSTestAssert.AreEqual(message.UserId, restored.UserId);
        MSTestAssert.AreEqual(message.DeviceId, restored.DeviceId);
        CollectionAssert.AreEqual(message.DatasetHash.ToByteArray(), restored.DatasetHash.ToByteArray());
        CollectionAssert.AreEqual(message.SignPub.ToByteArray(), restored.SignPub.ToByteArray());
        MSTestAssert.AreEqual(DatabaseConstants.CurrentDbVersion, restored.DatabaseVersion);
        MSTestAssert.AreEqual(SyncConstants.SyncProtocolVersion, restored.ProtocolVersion);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task WriteThenRead_RoundTripsUserSnapshotInventoryRequest()
    {
        var userId = Guid.NewGuid();
        var originDeviceId = Guid.NewGuid();
        var originInstanceId = Guid.NewGuid();
        var request = new UserSnapshotInventoryExchangeRequest();
        var user = new UserSnapshotUserInventory
        {
            UserId = userId.ToString("N"),
            UserKeyEpoch = 1,
            MembershipEpoch = 2
        };
        user.Revisions.Add(new UserSnapshotRevisionInventory
        {
            OriginDeviceId = originDeviceId.ToString("N"),
            OriginInstanceId = originInstanceId.ToString("N"),
            UserKeyEpoch = 1,
            HighestStoredRevision = 15,
            HighestStoredSnapshotHash = Google.Protobuf.ByteString.CopyFrom(Enumerable.Repeat((byte)0x15, 32).ToArray()),
            HighestMergedRevision = 12
        });
        request.Users.Add(user);
        await using var stream = new MemoryStream();

        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.UserSnapshotInventoryRequest, request, CancellationToken.None);
        stream.Position = 0;
        var frame = await SyncTcpFrameIo.ReadAsync(stream, CancellationToken.None);

        MSTestAssert.IsNotNull(frame);
        MSTestAssert.AreEqual(SyncTcpMessageType.UserSnapshotInventoryRequest, frame.Type);
        var restored = frame.Parse(UserSnapshotInventoryExchangeRequest.Parser);
        MSTestAssert.HasCount(1, restored.Users);
        MSTestAssert.AreEqual(userId, Guid.Parse(restored.Users[0].UserId));
        MSTestAssert.HasCount(1, restored.Users[0].Revisions);
        MSTestAssert.AreEqual(15L, restored.Users[0].Revisions[0].HighestStoredRevision);
        MSTestAssert.AreEqual(12L, restored.Users[0].Revisions[0].HighestMergedRevision);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task ReadAsync_WhenTransportFragmentsEveryByte_ReassemblesFrame()
    {
        await using var source = new MemoryStream();
        await SyncTcpFrameIo.WriteAsync(source, SyncTcpMessageType.Ack, new Ack { LastSyncedTs = 123456789 }, CancellationToken.None);
        source.Position = 0;
        await using var fragmented = new ChunkedReadStream(source, 1);

        var frame = await SyncTcpFrameIo.ReadAsync(fragmented, CancellationToken.None);

        MSTestAssert.IsNotNull(frame);
        MSTestAssert.AreEqual(SyncTcpMessageType.Ack, frame.Type);
        MSTestAssert.AreEqual(123456789L, frame.Parse(Ack.Parser).LastSyncedTs);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task ReadAsync_EmptyStream_ReturnsNull()
    {
        await using var stream = new MemoryStream();

        var frame = await SyncTcpFrameIo.ReadAsync(stream, CancellationToken.None);

        MSTestAssert.IsNull(frame);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task ReadAsync_PartialHeaderOrPayload_ThrowsEndOfStreamException()
    {
        await using var partialHeader = new MemoryStream(new byte[] { 0, 0 });
        await ExpectThrowsAsync<EndOfStreamException>(() =>
            SyncTcpFrameIo.ReadAsync(partialHeader, CancellationToken.None));

        var bytes = new byte[4 + 2];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), 10);
        bytes[4] = (byte)SyncTcpMessageType.Ack;
        bytes[5] = 1;
        await using var partialPayload = new MemoryStream(bytes);
        await ExpectThrowsAsync<EndOfStreamException>(() =>
            SyncTcpFrameIo.ReadAsync(partialPayload, CancellationToken.None));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task ReadAsync_RejectsZeroNegativeAndOversizedFrameLengths()
    {
        await ExpectInvalidLengthAsync(0);
        await ExpectInvalidLengthAsync(-1);
        await ExpectInvalidLengthAsync(SyncConstants.MaxSyncTcpFrameBytes + 1);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task WriteAndRead_EmptyPayloadFrame_RoundTrips()
    {
        await using var stream = new MemoryStream();

        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.PushDeltaEnd, CancellationToken.None);
        stream.Position = 0;
        var frame = await SyncTcpFrameIo.ReadAsync(stream, CancellationToken.None);

        MSTestAssert.IsNotNull(frame);
        MSTestAssert.AreEqual(SyncTcpMessageType.PushDeltaEnd, frame.Type);
        MSTestAssert.IsEmpty(frame.Payload);
    }

    private static async Task ExpectInvalidLengthAsync(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        await using var stream = new MemoryStream(bytes);
        await ExpectThrowsAsync<InvalidDataException>(() =>
            SyncTcpFrameIo.ReadAsync(stream, CancellationToken.None));
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _maxReadSize;

        public ChunkedReadStream(Stream inner, int maxReadSize)
        {
            _inner = inner;
            _maxReadSize = maxReadSize;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, _maxReadSize));

        public override int Read(Span<byte> buffer) =>
            _inner.Read(buffer[..Math.Min(buffer.Length, _maxReadSize)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(buffer.Length, _maxReadSize)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
