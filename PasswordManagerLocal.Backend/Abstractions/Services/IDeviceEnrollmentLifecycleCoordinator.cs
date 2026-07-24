namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentLifecycleCoordinator
{
    void OpenInteractiveAdmission();
    Task CloseInteractiveAdmissionAsync(CancellationToken cancellationToken = default);
}
