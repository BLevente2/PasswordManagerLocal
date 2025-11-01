namespace PasswordManagerLocalBackend.Constants;

public static class TokenConstrants
{
    public const int TokenLength = 32;  // 32 bytes = 256 bits

    public static readonly TimeSpan TokenExpirationTime = TimeSpan.FromMinutes(60); // 60 min = 1 hour

    public const int NumberOfTokenGenerationRetries = 1;
}