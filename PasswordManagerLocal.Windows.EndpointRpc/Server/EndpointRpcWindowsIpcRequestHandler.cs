using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Security;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Server;

public sealed class EndpointRpcWindowsIpcRequestHandler : IWindowsIpcRequestHandler
{
    private readonly EndpointRpcDispatcher _dispatcher;
    private readonly EndpointRpcMessageCodec _messageCodec;
    private readonly EndpointRpcContractValidator _validator;

    public EndpointRpcWindowsIpcRequestHandler(
        EndpointRpcDispatcher dispatcher,
        EndpointRpcMessageCodec messageCodec,
        EndpointRpcContractValidator validator)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _messageCodec = messageCodec ?? throw new ArgumentNullException(nameof(messageCodec));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public IpcOperationId OperationId => IpcOperationId.EndpointRpcRequest;

    public async Task<IpcResponseEnvelope> HandleAsync(
        IpcRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Payload is null)
            return SuccessFailure(context, CreateInvalidPayloadError(context.Request.CorrelationId));

        byte[]? decodedPayload = null;
        byte[]? dispatchPayload = null;
        try
        {
            EndpointOperationId operationId;
            try
            {
                (operationId, decodedPayload) = _messageCodec.DecodeRequest(context.Request.Payload);
            }
            catch (EndpointRpcPayloadException)
            {
                return SuccessFailure(context, CreateInvalidPayloadError(context.Request.CorrelationId));
            }

            dispatchPayload = decodedPayload;
            decodedPayload = null;
            var endpointContext = new EndpointRequestContext(
                context.Connection.ConnectionId,
                context.Request.CorrelationId,
                operationId,
                context.Connection.PeerRole,
                context.Connection.PeerProcessId,
                context.Connection.PeerSessionId,
                cancellationToken);
            var result = await _dispatcher.DispatchAsync(
                endpointContext,
                dispatchPayload,
                cancellationToken);

            byte[] encoded;
            if (result.IsSuccess)
            {
                try
                {
                    encoded = _messageCodec.EncodeSuccess(result.Result!);
                }
                finally
                {
                    EndpointSensitiveData.Clear(result.Result);
                }
            }
            else
            {
                _validator.Validate(result.Error!);
                encoded = _messageCodec.EncodeFailure(result.Error!);
            }

            return IpcResponseEnvelope.Success(context.Request.CorrelationId, encoded);
        }
        finally
        {
            EndpointSensitiveData.Clear(decodedPayload);
            EndpointSensitiveData.Clear(dispatchPayload);
            EndpointSensitiveData.Clear(context.Request.Payload);
        }
    }

    private IpcResponseEnvelope SuccessFailure(
        IpcRequestContext context,
        EndpointRpcError error)
    {
        _validator.Validate(error);
        return IpcResponseEnvelope.Success(
            context.Request.CorrelationId,
            _messageCodec.EncodeFailure(error));
    }

    private static EndpointRpcError CreateInvalidPayloadError(long correlationId) =>
        new(
            EndpointRpcErrorCode.ValidationFailed,
            EndpointRpcErrorCategory.Validation,
            "The endpoint RPC payload is invalid.",
            correlationId,
            DateTimeOffset.UtcNow,
            IsRetryable: false,
            RequiresProcessRestart: false);
}
