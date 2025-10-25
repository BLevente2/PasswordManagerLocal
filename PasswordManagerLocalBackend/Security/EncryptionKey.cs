using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

public sealed class EncryptionKey : IDisposable
{
    public const int Size = 32;

    private readonly byte[] _key;
    private bool _disposed;

    public EncryptionKey()
    {
        _key = AES256.GenerateKey();
    }

    public static EncryptionKey FromRaw(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != Size) throw new ArgumentException("Invalid key size.");
        var k = new byte[Size];
        raw.CopyTo(k);
        return new EncryptionKey(k);
    }

    public static EncryptionKey FromPassword(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations = 100_000, HashAlgorithmName alg = default)
    {
        if (password.Length == 0) throw new ArgumentException("Empty password.");
        if (salt.Length == 0) throw new ArgumentException("Empty salt.");
        if (alg == default) alg = HashAlgorithmName.SHA512;

        using var kdf = new Rfc2898DeriveBytes(password.ToArray(), salt.ToArray(), iterations, alg);
        var key = kdf.GetBytes(Size);
        return new EncryptionKey(key);
    }

    private EncryptionKey(byte[] key)
    {
        _key = key;
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        ThrowIfDisposed();
        return _key.AsSpan();
    }

    public byte[] ExportCopy()
    {
        ThrowIfDisposed();
        var copy = new byte[_key.Length];
        Buffer.BlockCopy(_key, 0, copy, 0, _key.Length);
        return copy;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EncryptionKey));
    }
}