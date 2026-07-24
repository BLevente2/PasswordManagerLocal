namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentAvailability
{
    bool IsEnrollmentAllowed { get; }
}
