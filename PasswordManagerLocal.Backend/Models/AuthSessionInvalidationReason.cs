namespace PasswordManagerLocal.Backend.Models;

public enum AuthSessionInvalidationReason
{
    None,
    LoggedOut,
    Expired,
    ProfilePasswordChanged,
    ProfileRemoved
}
