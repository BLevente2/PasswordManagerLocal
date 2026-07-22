using PasswordManagerLocal.Windows.EndpointRpc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Client;

public sealed class EndpointOperationOutcomeUnknownException : Exception
{
    public EndpointOperationOutcomeUnknownException(
        EndpointOperationId operationId,
        Exception innerException)
        : base("The endpoint operation may have executed, but its outcome could not be confirmed.", innerException)
    {
        OperationId = operationId;
    }

    public EndpointOperationId OperationId { get; }
}
