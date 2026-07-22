using PasswordManagerLocal.Windows.EndpointRpc.Contracts;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed record EndpointRpcDispatchResult(byte[]? Result, EndpointRpcError? Error)
{
    public bool IsSuccess => Error is null;

    public static EndpointRpcDispatchResult Success(byte[] result) =>
        new(result ?? throw new ArgumentNullException(nameof(result)), null);

    public static EndpointRpcDispatchResult Failure(EndpointRpcError error) =>
        new(null, error ?? throw new ArgumentNullException(nameof(error)));
}
