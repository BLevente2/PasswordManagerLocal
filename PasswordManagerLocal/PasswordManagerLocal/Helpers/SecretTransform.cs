using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocal.Helpers;

internal static class SecretTransform
{
    public static byte[] HashPassword(string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            return Hashing.SHA512Hash(passwordBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public static byte[] Utf8Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
