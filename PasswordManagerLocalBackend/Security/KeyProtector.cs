using System.Buffers.Binary;
using System.Security.Cryptography;
#if WINDOWS
using System.Runtime.Versioning;
#endif

namespace PasswordManagerLocalBackend.Security;

public sealed class PassphraseKeyProtector : IKeyProtector, IDisposable
{
    private readonly byte[] _passphrase;
    private readonly int _iterations;
    private readonly int _saltLen;
    private bool _disposed;

    public PassphraseKeyProtector(ReadOnlySpan<byte> passphrase, int iterations = 100_000, int saltLen = 16)
    {
        if (passphrase.Length == 0) throw new ArgumentException("Empty passphrase.");
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (saltLen <= 0) throw new ArgumentOutOfRangeException(nameof(saltLen));

        _passphrase = passphrase.ToArray();
        _iterations = iterations;
        _saltLen = saltLen;
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var salt = new byte[_saltLen];
        RandomNumberGenerator.Fill(salt);
        using var kdf = new Rfc2898DeriveBytes(_passphrase, salt, _iterations, HashAlgorithmName.SHA512);
        var kek = kdf.GetBytes(AES256.KeySizeInBytes);

        var nonce = new byte[AES256.NonceSizeInBytes];
        RandomNumberGenerator.Fill(nonce);

        var ct = new byte[plaintext.Length];
        var tag = new byte[AES256.TagSizeInBytes];

        using (var gcm = new AesGcm(kek, AES256.TagSizeInBytes))
        {
            gcm.Encrypt(nonce, plaintext, ct, tag);
        }

        var blob = new byte[1 + 1 + salt.Length + 4 + nonce.Length + 4 + ct.Length + tag.Length];
        int o = 0;
        blob[o++] = 1;
        blob[o++] = (byte)salt.Length;
        Buffer.BlockCopy(salt, 0, blob, o, salt.Length); o += salt.Length;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(o, 4), _iterations); o += 4;
        Buffer.BlockCopy(nonce, 0, blob, o, nonce.Length); o += nonce.Length;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(o, 4), ct.Length); o += 4;
        Buffer.BlockCopy(ct, 0, blob, o, ct.Length); o += ct.Length;
        Buffer.BlockCopy(tag, 0, blob, o, tag.Length);

        CryptographicOperations.ZeroMemory(kek);
        CryptographicOperations.ZeroMemory(tag);
        CryptographicOperations.ZeroMemory(ct);
        CryptographicOperations.ZeroMemory(nonce);
        Array.Clear(salt, 0, salt.Length);
        return blob;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        int o = 0;
        if (protectedBlob.Length < 2) throw new CryptographicException("Invalid blob.");
        var ver = protectedBlob[o++]; if (ver != 1) throw new CryptographicException("Unsupported blob.");
        int saltLen = protectedBlob[o++];
        if (saltLen <= 0) throw new CryptographicException("Invalid salt.");
        if (protectedBlob.Length < 2 + saltLen + 4 + AES256.NonceSizeInBytes + 4 + AES256.TagSizeInBytes) throw new CryptographicException("Invalid blob.");

        var salt = protectedBlob.Slice(o, saltLen).ToArray(); o += saltLen;
        int iterations = BinaryPrimitives.ReadInt32LittleEndian(protectedBlob.Slice(o, 4)); o += 4;
        var nonce = protectedBlob.Slice(o, AES256.NonceSizeInBytes).ToArray(); o += AES256.NonceSizeInBytes;
        int ctLen = BinaryPrimitives.ReadInt32LittleEndian(protectedBlob.Slice(o, 4)); o += 4;
        if (ctLen < 0) throw new CryptographicException("Invalid length.");
        var ct = protectedBlob.Slice(o, ctLen).ToArray(); o += ctLen;
        var tag = protectedBlob.Slice(o, AES256.TagSizeInBytes).ToArray();

        using var kdf = new Rfc2898DeriveBytes(_passphrase, salt, iterations, HashAlgorithmName.SHA512);
        var kek = kdf.GetBytes(AES256.KeySizeInBytes);

        var pt = new byte[ctLen];
        using (var gcm = new AesGcm(kek, AES256.TagSizeInBytes))
        {
            gcm.Decrypt(nonce, ct, tag, pt);
        }

        CryptographicOperations.ZeroMemory(kek);
        CryptographicOperations.ZeroMemory(tag);
        CryptographicOperations.ZeroMemory(ct);
        CryptographicOperations.ZeroMemory(nonce);
        Array.Clear(salt, 0, salt.Length);
        return pt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_passphrase);
        _disposed = true;
        GC.SuppressFinalize(this);
    }


}

#if WINDOWS
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
public byte[] Protect(ReadOnlySpan<byte> plaintext)
{
return ProtectedData.Protect(plaintext.ToArray(), null, DataProtectionScope.CurrentUser);
}

public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
{
    return ProtectedData.Unprotect(protectedBlob.ToArray(), null, DataProtectionScope.CurrentUser);
}


}
#endif