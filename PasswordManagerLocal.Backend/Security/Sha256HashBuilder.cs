using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Security;

public sealed class Sha256HashBuilder : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finalized;
    private bool _disposed;

    public void Write(byte value)
    {
        EnsureWritable();
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        _hash.AppendData(buffer);
    }

    public void Write(bool value) => Write(value ? (byte)1 : (byte)0);

    public void Write(int value)
    {
        EnsureWritable();
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _hash.AppendData(buffer);
    }

    public void Write(long value)
    {
        EnsureWritable();
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        _hash.AppendData(buffer);
    }

    public void Write(Guid value)
    {
        EnsureWritable();
        Span<byte> buffer = stackalloc byte[16];
        if (!value.TryWriteBytes(buffer))
            throw new InvalidOperationException("Failed to write GUID bytes for hashing.");

        _hash.AppendData(buffer);
    }

    public void Write(DateTime value) => Write(UtcDateTimeUtil.ToUtc(value).Ticks);

    public void Write(DateTimeOffset value) => Write(UtcDateTimeUtil.ToUtc(value).ToUnixTimeMilliseconds());

    public void Write(DateTimeOffset? value) => Write(value.HasValue ? UtcDateTimeUtil.ToUtc(value.Value).ToUnixTimeMilliseconds() : 0);

    public void WriteBytes(byte[]? value)
    {
        if (value is null)
        {
            Write(0);
            return;
        }

        WriteBytes(value.AsSpan());
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureWritable();
        Write(value.Length);
        if (!value.IsEmpty)
            _hash.AppendData(value);
    }

    public void WriteString(string? value)
    {
        EnsureWritable();
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        try
        {
            WriteBytes(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public byte[] GetHashAndReset()
    {
        EnsureWritable();
        _finalized = true;
        return _hash.GetHashAndReset();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hash.Dispose();
    }

    private void EnsureWritable()
    {
        if (_disposed || _finalized)
            throw new ObjectDisposedException(nameof(Sha256HashBuilder));
    }
}
