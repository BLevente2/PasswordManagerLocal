using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

public static class Hashing
{
    public static byte[] SHA256Hash(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        try
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public static byte[] SHA512Hash(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        try
        {
            using var sha = SHA512.Create();
            return sha.ComputeHash(buffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public static byte[] SHA256Hash(ReadOnlySpan<byte> data, ReadOnlySpan<byte> salt)
    {
        var buf = new byte[data.Length + salt.Length];
        data.CopyTo(buf);
        salt.CopyTo(buf.AsSpan(data.Length));
        try
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(buf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buf);
        }
    }

    public static byte[] SHA512Hash(ReadOnlySpan<byte> data, ReadOnlySpan<byte> salt)
    {
        var buf = new byte[data.Length + salt.Length];
        data.CopyTo(buf);
        salt.CopyTo(buf.AsSpan(data.Length));
        try
        {
            using var sha = SHA512.Create();
            return sha.ComputeHash(buf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buf);
        }
    }

    public static byte[] HMACSHA256(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var keyBuffer = key.ToArray();
        var dataBuffer = data.ToArray();
        try
        {
            using var h = new HMACSHA256(keyBuffer);
            return h.ComputeHash(dataBuffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBuffer);
            CryptographicOperations.ZeroMemory(dataBuffer);
        }
    }

    public static byte[] HMACSHA512(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var keyBuffer = key.ToArray();
        var dataBuffer = data.ToArray();
        try
        {
            using var h = new HMACSHA512(keyBuffer);
            return h.ComputeHash(dataBuffer);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBuffer);
            CryptographicOperations.ZeroMemory(dataBuffer);
        }
    }

    public static bool Verify(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static byte[] GenerateSalt(int length = 32)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var salt = new byte[length];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }
}