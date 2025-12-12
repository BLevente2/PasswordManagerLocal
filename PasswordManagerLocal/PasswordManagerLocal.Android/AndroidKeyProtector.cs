using Android.App;
using Android.Content;
using PasswordManagerLocalBackend.Abstractions.Security;
using System;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Android;

public sealed class AndroidKeyProtector : IKeyProtector
{
    private const string PreferencesName = "PasswordManagerLocal.Secure";
    private const string KeyName = "DbMasterKey";

    private readonly byte[] _masterKey;

    public AndroidKeyProtector()
    {
        var context = Application.Context;
        var prefs = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

        var existing = prefs.GetString(KeyName, null);
        if (string.IsNullOrEmpty(existing))
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            var b64 = Convert.ToBase64String(key);
            using var editor = prefs.Edit();
            editor.PutString(KeyName, b64);
            editor.Commit();

            _masterKey = key;
        }
        else
        {
            _masterKey = Convert.FromBase64String(existing);
        }
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_masterKey);
        aes.Encrypt(iv, plaintext, ciphertext, tag);

        var result = new byte[1 + iv.Length + 1 + tag.Length + ciphertext.Length];
        var offset = 0;

        result[offset++] = (byte)iv.Length;
        Buffer.BlockCopy(iv, 0, result, offset, iv.Length);
        offset += iv.Length;

        result[offset++] = (byte)tag.Length;
        Buffer.BlockCopy(tag, 0, result, offset, tag.Length);
        offset += tag.Length;

        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        var data = protectedBlob.ToArray();
        var offset = 0;

        if (data.Length < 2)
            throw new CryptographicException("Invalid protected payload.");

        var ivLen = data[offset++];
        if (ivLen <= 0 || ivLen > 32 || data.Length < 1 + ivLen + 1)
            throw new CryptographicException("Invalid IV length.");

        var iv = new byte[ivLen];
        Buffer.BlockCopy(data, offset, iv, 0, ivLen);
        offset += ivLen;

        var tagLen = data[offset++];
        if (tagLen <= 0 || tagLen > 32 || data.Length < 1 + ivLen + 1 + tagLen)
            throw new CryptographicException("Invalid tag length.");

        var tag = new byte[tagLen];
        Buffer.BlockCopy(data, offset, tag, 0, tagLen);
        offset += tagLen;

        var ciphertextLen = data.Length - offset;
        if (ciphertextLen <= 0)
            throw new CryptographicException("Invalid ciphertext length.");

        var ciphertext = new byte[ciphertextLen];
        Buffer.BlockCopy(data, offset, ciphertext, 0, ciphertextLen);

        var plaintext = new byte[ciphertextLen];

        using var aes = new AesGcm(_masterKey);
        aes.Decrypt(iv, ciphertext, tag, plaintext);

        return plaintext;
    }
}