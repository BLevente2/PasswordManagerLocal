namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncControlledHostedService : IBackendHostedService
{
    int StartOrder { get; }
}
