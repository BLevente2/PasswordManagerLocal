using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentParsedCode
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Secret { get; set; } = [];
}

public static class DeviceEnrollmentCode
{
    private const string Prefix = "PML";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static (string Code, string SessionId, byte[] Secret) Create()
    {
        var sessionBytes = RandomNumberGenerator.GetBytes(5);
        var secret = RandomNumberGenerator.GetBytes(16);
        var sessionId = Encode(sessionBytes);
        var secretText = Encode(secret);
        return ($"{Prefix}-{sessionId}-{Group(secretText)}", sessionId, secret);
    }


    public static DeviceEnrollmentParsedCode Parse(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidDataException("Device enrollment code is missing.");

        var normalized = code.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != Prefix)
            throw new InvalidDataException("Device enrollment code is invalid.");

        var sessionId = parts[1];
        var secretText = string.Concat(parts.Skip(2));
        if (sessionId.Length < 8 || secretText.Length < 20)
            throw new InvalidDataException("Device enrollment code is invalid.");

        return new DeviceEnrollmentParsedCode
        {
            SessionId = sessionId,
            Secret = Decode(secretText)
        };
    }


    public static string BuildEnrollmentAdvertisementHash(string sessionId, byte[] secret, string tlsFingerprint, byte[] signPublicKey) =>
        Encode(Hmac(secret, $"advertise:{sessionId}:{NormalizeFingerprint(tlsFingerprint)}:{Convert.ToHexString(signPublicKey)}").Take(12).ToArray());


    public static byte[] BuildCompletionProof(string sessionId, byte[] secret, string sourceDeviceId, byte[] sourceSignPublicKey, string sourceTlsFingerprint) =>
        Hmac(secret, $"complete:{sessionId}:{sourceDeviceId}:{Convert.ToHexString(sourceSignPublicKey)}:{NormalizeFingerprint(sourceTlsFingerprint)}");


    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(left, right);
    }


    public static string Encode(byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                output.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);

        return output.ToString();
    }


    private static byte[] Decode(string value)
    {
        var normalized = value.Trim().ToUpperInvariant().Replace("-", string.Empty);
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var ch in normalized)
        {
            var index = Alphabet.IndexOf(ch);
            if (index < 0)
                throw new InvalidDataException("Device enrollment code contains an invalid character.");

            buffer = (buffer << 5) | index;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 255));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }


    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));


    private static string Group(string value) =>
        string.Join("-", Enumerable.Range(0, (value.Length + 4) / 5).Select(i => value.Substring(i * 5, Math.Min(5, value.Length - i * 5))));


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
