namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IBackendExecutionProfileProvider : IDeviceEnrollmentAvailability
{
    BackendExecutionProfile? Current { get; }
    bool IsInteractive { get; }
    event EventHandler? ProfileChanged;
}
