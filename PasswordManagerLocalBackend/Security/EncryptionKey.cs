using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.EncryptionKeyConstants;

namespace PasswordManagerLocalBackend.Security;

public sealed class EncryptionKey : IDisposable
{
    private readonly byte[] _key;
    private bool _disposed;

    private EncryptionKey(byte[] key)
    {
        _key = key;
    }

    public static EncryptionKey Create()
    {
        var raw = AES256.GenerateKey();
        try
        {
            var copy = new byte[KeySize];
            Buffer.BlockCopy(raw, 0, copy, 0, KeySize);
            return new EncryptionKey(copy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public static EncryptionKey FromRaw(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != KeySize)
            throw new ArgumentException("Invalid key size.");

        var copy = new byte[KeySize];
        raw.CopyTo(copy);
        return new EncryptionKey(copy);
    }

    public static EncryptionKey FromPassword(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations = Iterations, HashAlgorithmName alg = default)
    {
        if (password.Length == 0)
            throw new ArgumentException("Empty password.");

        if (salt.Length == 0)
            throw new ArgumentException("Empty salt.");

        if (alg == default)
            alg = HashAlgorithmName.SHA512;

        using var kdf = new Rfc2898DeriveBytes(password.ToArray(), salt.ToArray(), iterations, alg);
        var raw = kdf.GetBytes(KeySize);
        return new EncryptionKey(raw);
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        ThrowIfDisposed();
        return _key.AsSpan();
    }

    internal byte[] ExportCopy()
    {
        ThrowIfDisposed();
        var copy = new byte[_key.Length];
        Buffer.BlockCopy(_key, 0, copy, 0, _key.Length);
        return copy;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EncryptionKey));
    }
}