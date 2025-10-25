using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

public static class Hashing
{
    public static byte[] SHA256Hash(ReadOnlySpan<byte> data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data.ToArray());
    }

    public static byte[] SHA512Hash(ReadOnlySpan<byte> data)
    {
        using var sha = SHA512.Create();
        return sha.ComputeHash(data.ToArray());
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
        using var h = new HMACSHA256(key.ToArray());
        return h.ComputeHash(data.ToArray());
    }

    public static byte[] HMACSHA512(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        using var h = new HMACSHA512(key.ToArray());
        return h.ComputeHash(data.ToArray());
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