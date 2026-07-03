namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncControlledHostedService : IBackendHostedService
{
    int StartOrder { get; }
}
