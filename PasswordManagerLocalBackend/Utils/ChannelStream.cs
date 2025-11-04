using System.Threading.Channels;

namespace PasswordManagerLocalBackend.Utils;

internal sealed class ChannelStream : Stream
{
    private readonly Channel<byte[]> _ch;
    private readonly int _segmentSize;
    private byte[]? _cur;
    private int _curPos;
    private bool _completed;
    private bool _disposed;

    internal ChannelStream(int segmentSize = 81920, int capacitySegments = 8)
    {
        _segmentSize = segmentSize;
        _ch = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacitySegments)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_cur != null && _curPos < _cur.Length)
            {
                int n = Math.Min(count, _cur.Length - _curPos);
                Buffer.BlockCopy(_cur, _curPos, buffer, offset, n);
                _curPos += n;
                if (_curPos >= _cur.Length) { _cur = null; _curPos = 0; }
                return n;
            }

            if (await _ch.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_ch.Reader.TryRead(out var seg))
                {
                    _cur = seg;
                    _curPos = 0;
                    continue;
                }
            }
            else
            {
                return 0;
            }
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_completed) throw new IOException("Completed");
        while (count > 0)
        {
            int n = Math.Min(count, _segmentSize);
            var seg = new byte[n];
            Buffer.BlockCopy(buffer, offset, seg, 0, n);
            await _ch.Writer.WriteAsync(seg, cancellationToken).ConfigureAwait(false);
            offset += n;
            count -= n;
        }
    }

    internal void CompleteWriting(Exception? error = null)
    {
        if (_completed) return;
        _completed = true;
        _ch.Writer.TryComplete(error);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing && !_completed) _ch.Writer.TryComplete();
        _disposed = true;
        base.Dispose(disposing);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}