using PasswordManagerLocal.Windows.EndpointRpc.Authorization;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public sealed class FakeEndpointUiRegistrationResolver : IEndpointUiRegistrationResolver
{
    public bool IsRegisteredResult { get; set; } = true;

    public bool IsRegistered(int processId, Guid sessionId) => IsRegisteredResult;
}
