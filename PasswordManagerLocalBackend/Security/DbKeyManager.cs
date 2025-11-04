using PasswordManagerLocalBackend.Abstractions.Security;
using static PasswordManagerLocalBackend.Constants.PathConstants;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

internal static class DbKeyManager
{
    internal static string GetOrCreateSqlCipherPassword(IKeyProtector protector)
    {
        var path = Path.Combine(AppRootFolder, KeyFileName);

        if (File.Exists(path))
        {
            var protectedBlob = File.ReadAllBytes(path);
            var keyBytes = protector.Unprotect(protectedBlob);
            try
            {
                return Convert.ToBase64String(keyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
        else
        {
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            try
            {
                var protectedBlob = protector.Protect(keyBytes);
                File.WriteAllBytes(path, protectedBlob);
                try
                {
                    return Convert.ToBase64String(keyBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedBlob);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
    }
}