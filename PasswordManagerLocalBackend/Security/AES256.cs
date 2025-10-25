using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

internal static class AES256
{
    internal const int KeySizeInBytes = 32;
    internal const int NonceSizeInBytes = 12;
    internal const int TagSizeInBytes = 16;
    internal const int DefaultFrameSize = 64 * 1024;

    private const uint Magic = 0x4D434741;
    private const byte Version = 1;

    internal static byte[] GenerateKey()
    {
        var key = new byte[KeySizeInBytes];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    internal static async Task<byte[]> EncryptAsync(byte[] data, EncryptionKey key, byte[]? associatedData = null, int frameSize = DefaultFrameSize)
    {
        using var input = new MemoryStream(data, writable: false);
        using var output = new MemoryStream();
        await EncryptToStreamAsync(input, output, key, associatedData, frameSize);
        return output.ToArray();
    }

    internal static async Task<byte[]> DecryptAsync(byte[] blob, EncryptionKey key, byte[]? associatedData = null)
    {
        using var input = new MemoryStream(blob, writable: false);
        using var output = new MemoryStream();
        await DecryptToStreamAsync(input, output, key, associatedData);
        return output.ToArray();
    }

    internal static async Task<Stream> EncryptAsync(Stream input, EncryptionKey key, byte[]? associatedData = null, int frameSize = DefaultFrameSize)
    {
        var output = new MemoryStream();
        await EncryptToStreamAsync(input, output, key, associatedData, frameSize);
        output.Position = 0;
        return output;
    }

    internal static async Task<Stream> DecryptAsync(Stream input, EncryptionKey key, byte[]? associatedData = null)
    {
        var output = new MemoryStream();
        await DecryptToStreamAsync(input, output, key, associatedData);
        output.Position = 0;
        return output;
    }

    internal static async Task EncryptToStreamAsync(Stream input, Stream output, EncryptionKey key, byte[]? associatedData = null, int frameSize = DefaultFrameSize)
    {
        if (frameSize <= 0) throw new ArgumentOutOfRangeException(nameof(frameSize));

        var header = new byte[4 + 1 + 8 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        header[4] = Version;

        var noncePrefix = new byte[8];
        RandomNumberGenerator.Fill(noncePrefix);
        Buffer.BlockCopy(noncePrefix, 0, header, 5, 8);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(13, 4), frameSize);

        await output.WriteAsync(header, 0, header.Length);

        var buffer = new byte[frameSize];
        var cipher = new byte[frameSize];
        var tag = new byte[TagSizeInBytes];
        var nonce = new byte[NonceSizeInBytes];
        Buffer.BlockCopy(noncePrefix, 0, nonce, 0, 8);

        var lenBuf = new byte[4];
        int frameIndex = 0;

        var keyBytes = key.ExportCopy();
        try
        {
            using (var gcm = new AesGcm(keyBytes, TagSizeInBytes))
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(8, 4), frameIndex);

                    int aadLen = header.Length + 8 + (associatedData?.Length ?? 0);
                    var aadBuf = ArrayPool<byte>.Shared.Rent(aadLen);
                    try
                    {
                        Buffer.BlockCopy(header, 0, aadBuf, 0, header.Length);
                        BinaryPrimitives.WriteInt32LittleEndian(aadBuf.AsSpan(header.Length, 4), frameIndex);
                        BinaryPrimitives.WriteInt32LittleEndian(aadBuf.AsSpan(header.Length + 4, 4), read);
                        if (associatedData != null)
                            Buffer.BlockCopy(associatedData, 0, aadBuf, header.Length + 8, associatedData.Length);

                        gcm.Encrypt(nonce, buffer.AsSpan(0, read), cipher.AsSpan(0, read), tag, aadBuf.AsSpan(0, aadLen));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(aadBuf, clearArray: true);
                    }

                    BinaryPrimitives.WriteInt32LittleEndian(lenBuf, read);
                    await output.WriteAsync(lenBuf, 0, 4);
                    await output.WriteAsync(cipher, 0, read);
                    await output.WriteAsync(tag, 0, TagSizeInBytes);

                    frameIndex++;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(cipher);
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    internal static async Task DecryptToStreamAsync(Stream input, Stream output, EncryptionKey key, byte[]? associatedData = null)
    {
        var header = new byte[4 + 1 + 8 + 4];
        await ReadExactAsync(input, header, 0, header.Length);

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (magic != Magic) throw new CryptographicException("Invalid header.");

        var version = header[4];
        if (version != Version) throw new CryptographicException("Unsupported version.");

        var noncePrefix = header.AsSpan(5, 8).ToArray();
        var frameSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(13, 4));
        if (frameSize <= 0) throw new CryptographicException("Invalid frame size.");

        var cipher = new byte[frameSize];
        var plain = new byte[frameSize];
        var tag = new byte[TagSizeInBytes];
        var nonce = new byte[NonceSizeInBytes];
        Buffer.BlockCopy(noncePrefix, 0, nonce, 0, 8);

        var lenBuf = new byte[4];
        int frameIndex = 0;

        var keyBytes = key.ExportCopy();
        try
        {
            using (var gcm = new AesGcm(keyBytes, TagSizeInBytes))
            {
                while (true)
                {
                    int lenRead = await input.ReadAsync(lenBuf, 0, 4);
                    if (lenRead == 0) break;
                    if (lenRead != 4) throw new CryptographicException("Corrupted stream.");

                    int chunkLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
                    if (chunkLen < 0 || chunkLen > frameSize) throw new CryptographicException("Invalid chunk length.");

                    await ReadExactAsync(input, cipher, 0, chunkLen);
                    await ReadExactAsync(input, tag, 0, TagSizeInBytes);

                    BinaryPrimitives.WriteInt32LittleEndian(nonce.AsSpan(8, 4), frameIndex);

                    int aadLen = header.Length + 8 + (associatedData?.Length ?? 0);
                    var aadBuf = ArrayPool<byte>.Shared.Rent(aadLen);
                    try
                    {
                        Buffer.BlockCopy(header, 0, aadBuf, 0, header.Length);
                        BinaryPrimitives.WriteInt32LittleEndian(aadBuf.AsSpan(header.Length, 4), frameIndex);
                        BinaryPrimitives.WriteInt32LittleEndian(aadBuf.AsSpan(header.Length + 4, 4), chunkLen);
                        if (associatedData != null)
                            Buffer.BlockCopy(associatedData, 0, aadBuf, header.Length + 8, associatedData.Length);

                        gcm.Decrypt(nonce, cipher.AsSpan(0, chunkLen), tag, plain.AsSpan(0, chunkLen), aadBuf.AsSpan(0, aadLen));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(aadBuf, clearArray: true);
                    }

                    await output.WriteAsync(plain, 0, chunkLen);
                    frameIndex++;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    private static async Task ReadExactAsync(Stream s, byte[] buf, int offset, int count)
    {
        int readTotal = 0;
        while (readTotal < count)
        {
            int r = await s.ReadAsync(buf, offset + readTotal, count - readTotal);
            if (r <= 0) throw new EndOfStreamException();
            readTotal += r;
        }
    }
}