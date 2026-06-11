namespace PasswordManagerLocalBackend.Sync.Tcp;

public enum SyncProtocolStatusCode
{
    Unknown,
    Unauthenticated,
    Unavailable,
    InvalidArgument,
    PermissionDenied,
    ResourceExhausted,
    FailedPrecondition
}
