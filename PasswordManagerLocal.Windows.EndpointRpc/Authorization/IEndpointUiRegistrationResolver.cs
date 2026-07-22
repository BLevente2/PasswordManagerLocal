namespace PasswordManagerLocal.Windows.EndpointRpc.Authorization;

public interface IEndpointUiRegistrationResolver
{
    bool IsRegistered(int processId, Guid sessionId);
}
