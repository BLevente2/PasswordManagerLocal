namespace PasswordManagerLocalBackend.Exceptions;

public sealed class DeviceIdentityNotInitilaizedException : InvalidOperationException
{
    public DeviceIdentityNotInitilaizedException()
        : base("Device identity is not initialized.")
    {
    }
}