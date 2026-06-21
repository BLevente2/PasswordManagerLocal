namespace PasswordManagerLocalBackend.Exceptions;

public sealed class SyncRouteDisabledException : UnauthorizedAccessException
{
    public SyncRouteDisabledException(string message) : base(message)
    {
    }
}
