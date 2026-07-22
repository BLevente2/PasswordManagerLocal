using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using System.Buffers.Binary;

namespace PasswordManagerLocal.Windows.EndpointRpc.Serialization;

public sealed class EndpointRpcMessageCodec
{
    private const int RequestHeaderSize = 4;
    private const int ResponseHeaderSize = 1;
    private readonly EndpointRpcSerializer _serializer;
    private readonly PasswordManagerLocal.Windows.EndpointRpc.Validation.EndpointRpcContractValidator _validator;

    public EndpointRpcMessageCodec(EndpointRpcSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = new PasswordManagerLocal.Windows.EndpointRpc.Validation.EndpointRpcContractValidator();
    }

    public byte[] EncodeRequest(EndpointOperationId operationId, ReadOnlySpan<byte> payload)
    {
        if (!Enum.IsDefined(operationId))
            throw new ArgumentOutOfRangeException(nameof(operationId));
        if (payload.Length > EndpointRpcLimits.MaximumRequestPayloadSize)
            throw new EndpointRpcPayloadException("The endpoint RPC request payload exceeds the permitted size.");

        var result = new byte[RequestHeaderSize + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(0, RequestHeaderSize), (int)operationId);
        payload.CopyTo(result.AsSpan(RequestHeaderSize));
        return result;
    }

    public (EndpointOperationId OperationId, byte[] Payload) DecodeRequest(ReadOnlySpan<byte> message)
    {
        if (message.Length < RequestHeaderSize || message.Length - RequestHeaderSize > EndpointRpcLimits.MaximumRequestPayloadSize)
            throw new EndpointRpcPayloadException("The endpoint RPC request message is invalid.");

        var operationId = (EndpointOperationId)BinaryPrimitives.ReadInt32BigEndian(message[..RequestHeaderSize]);
        if (!Enum.IsDefined(operationId))
            throw new EndpointRpcPayloadException("The endpoint RPC operation is unknown.");
        return (operationId, message[RequestHeaderSize..].ToArray());
    }

    public byte[] EncodeSuccess(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > EndpointRpcLimits.MaximumResponsePayloadSize)
            throw new EndpointRpcPayloadException("The endpoint RPC response payload exceeds the permitted size.");
        var result = new byte[ResponseHeaderSize + payload.Length];
        result[0] = 1;
        payload.CopyTo(result.AsSpan(ResponseHeaderSize));
        return result;
    }

    public byte[] EncodeFailure(EndpointRpcError error)
    {
        _validator.Validate(error);
        var payload = _serializer.Serialize(error, EndpointRpcJsonContext.Default.EndpointRpcError);
        try
        {
            if (payload.Length > EndpointRpcLimits.MaximumErrorPayloadSize)
                throw new EndpointRpcPayloadException("The endpoint RPC error payload exceeds the permitted size.");
            var result = new byte[ResponseHeaderSize + payload.Length];
            result[0] = 0;
            payload.CopyTo(result.AsSpan(ResponseHeaderSize));
            return result;
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    public byte[] DecodeResponse(ReadOnlySpan<byte> message)
    {
        if (message.Length < ResponseHeaderSize || message.Length - ResponseHeaderSize > EndpointRpcLimits.MaximumResponsePayloadSize)
            throw new EndpointRpcPayloadException("The endpoint RPC response message is invalid.");
        if (message[0] == 1)
            return message[ResponseHeaderSize..].ToArray();
        if (message[0] != 0)
            throw new EndpointRpcPayloadException("The endpoint RPC response status is invalid.");
        if (message.Length - ResponseHeaderSize > EndpointRpcLimits.MaximumErrorPayloadSize)
            throw new EndpointRpcPayloadException("The endpoint RPC error payload exceeds the permitted size.");

        var error = _serializer.Deserialize(message[ResponseHeaderSize..], EndpointRpcJsonContext.Default.EndpointRpcError);
        _validator.Validate(error);
        throw new Client.EndpointRpcRemoteException(error);
    }
}
