namespace PasswordManagerLocalBackend.Constants;

public static class DataCachingConstants
{
    public static readonly TimeSpan CacheExpirationTime = TimeSpan.FromMinutes(60); // 60 minutes, which is equal to token expiration time
}