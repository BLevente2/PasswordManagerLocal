namespace PasswordManagerLocalBackend.Models;

public enum AuthSessionInvalidationReason
{
    None,
    LoggedOut,
    Expired,
    ProfilePasswordChanged,
    ProfileRemoved
}
