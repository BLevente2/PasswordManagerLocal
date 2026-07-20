namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IBackendInitializationService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
