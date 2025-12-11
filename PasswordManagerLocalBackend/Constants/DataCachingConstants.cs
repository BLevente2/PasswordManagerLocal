namespace PasswordManagerLocalBackend.Constants;

public static class DataCachingConstants
{
    public static readonly TimeSpan UserDataCacheExpirationTime = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan GroupDataCacheExpirationTime = TimeSpan.FromMinutes(60);
}