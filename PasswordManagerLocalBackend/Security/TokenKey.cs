using static PasswordManagerLocalBackend.Security.Hashing;

namespace PasswordManagerLocalBackend.Security;

public static class TokenKey
{
    public static string HashToken(string token)
    {
        var bytes = Convert.FromBase64String(token);
        var hash = SHA256Hash(bytes);
        return Convert.ToBase64String(hash);
    }
}