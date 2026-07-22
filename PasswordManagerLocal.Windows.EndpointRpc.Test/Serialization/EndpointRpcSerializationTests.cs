using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.EndpointRpc.Metadata;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using System.Text.Json;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Serialization;

[TestClass]
public sealed class EndpointRpcSerializationTests
{
    [TestMethod]
    public void EveryMappedRequestAndResponseUsesSourceGeneratedMetadata()
    {
        foreach (var descriptor in EndpointOperationManifest.All)
        {
            RoundTrip(descriptor.RequestType);
            RoundTrip(descriptor.ResponseType);
        }
    }

    [TestMethod]
    public void MalformedJsonIsRejected()
    {
        var serializer = new EndpointRpcSerializer();
        Assert.ThrowsExactly<EndpointRpcPayloadException>(() =>
            serializer.Deserialize(
                "{"u8.ToArray(),
                EndpointRpcJsonContext.Default.LoginEndpointRequest));
    }


    [TestMethod]
    public void UnknownJsonMemberIsRejected()
    {
        var serializer = new EndpointRpcSerializer();
        Assert.ThrowsExactly<EndpointRpcPayloadException>(() =>
            serializer.Deserialize(
                "{\"token\":\"00000000-0000-0000-0000-000000000001\",\"unexpected\":true}"u8.ToArray(),
                EndpointRpcJsonContext.Default.GetSavedPasswordsEndpointRequest));
    }

    [TestMethod]
    public void LargeTransferContractsRoundTripWithSourceGeneratedMetadata()
    {
        RoundTrip(typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.EndpointLargeResultDescriptor));
        RoundTrip(typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.GetEndpointLargeResultChunkRequest));
        RoundTrip(typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.GetEndpointLargeResultChunkResponse));
        RoundTrip(typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.ReleaseEndpointLargeResultRequest));
        RoundTrip(typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.ReleaseEndpointLargeResultResponse));
    }

    private static void RoundTrip(Type type)
    {
        var metadata = EndpointRpcJsonContext.Default.GetTypeInfo(type);
        Assert.IsNotNull(metadata, type.FullName);
        var value = Activator.CreateInstance(type);
        Assert.IsNotNull(value, type.FullName);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, metadata);
        var result = JsonSerializer.Deserialize(payload, metadata);
        Assert.IsNotNull(result, type.FullName);
        Assert.AreEqual(type, result.GetType());
    }
}
