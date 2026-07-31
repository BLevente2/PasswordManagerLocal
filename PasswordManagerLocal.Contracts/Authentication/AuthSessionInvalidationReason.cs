namespace PasswordManagerLocal.Contracts.Authentication;

public enum AuthSessionInvalidationReason
{
    None,
    LoggedOut,
    Expired,
    ProfilePasswordChanged,
    CanonicalRecovered,
    ProfileRemoved
}
