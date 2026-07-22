using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PasswordManagerLocal.Windows.EndpointRpc.Serialization;

public sealed class EndpointRpcSerializer
{
    public byte[] Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new EndpointRpcPayloadException("The endpoint RPC payload could not be serialized.", exception);
        }
    }

    public T Deserialize<T>(ReadOnlySpan<byte> payload, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo)
                ?? throw new EndpointRpcPayloadException("The endpoint RPC payload is empty.");
        }
        catch (EndpointRpcPayloadException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new EndpointRpcPayloadException("The endpoint RPC payload is malformed.", exception);
        }
    }
}
