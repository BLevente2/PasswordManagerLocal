using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Validation;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Payload;

[TestClass]
public sealed class EndpointRpcPayloadLimitTests
{
    [TestMethod]
    public void MaximumEndpointPayloadsFitHardenedOuterTransportBounds()
    {
        Assert.IsTrue(EndpointRpcLimits.MaximumRequestPayloadSize + 4 <= IpcContractLimits.MaximumInnerPayloadSize);
        Assert.IsTrue(EndpointRpcLimits.MaximumResponsePayloadSize + 1 <= IpcContractLimits.MaximumInnerPayloadSize);
        Assert.IsTrue(IpcContractLimits.MaximumInnerPayloadSize < WindowsIpcProtocol.MaximumPayloadSize);
    }


    [TestMethod]
    public void MaximumEndpointPayloadsAreAcceptedByTheInnerCodec()
    {
        var codec = new EndpointRpcMessageCodec(new EndpointRpcSerializer());
        var request = codec.EncodeRequest(
            EndpointOperationId.AddNewPassword,
            new byte[EndpointRpcLimits.MaximumRequestPayloadSize]);
        var response = codec.EncodeSuccess(
            new byte[EndpointRpcLimits.MaximumResponsePayloadSize]);

        Assert.AreEqual(EndpointRpcLimits.MaximumRequestPayloadSize + 4, request.Length);
        Assert.AreEqual(EndpointRpcLimits.MaximumResponsePayloadSize + 1, response.Length);
    }

    [TestMethod]
    public void RequestAndResponseCodecRejectOversizedPayloads()
    {
        var codec = new EndpointRpcMessageCodec(new EndpointRpcSerializer());
        Assert.ThrowsExactly<EndpointRpcPayloadException>(() =>
            codec.EncodeRequest(
                EndpointOperationId.Login,
                new byte[EndpointRpcLimits.MaximumRequestPayloadSize + 1]));
        Assert.ThrowsExactly<EndpointRpcPayloadException>(() =>
            codec.EncodeSuccess(
                new byte[EndpointRpcLimits.MaximumResponsePayloadSize + 1]));
    }

    [TestMethod]
    public void OuterValidatorRejectsOversizedEndpointEnvelopeBeforeHandler()
    {
        var validator = new WindowsIpcContractValidator();
        Assert.ThrowsExactly<PasswordManagerLocal.Windows.Ipc.Serialization.IpcPayloadLimitExceededException>(() =>
            validator.Validate(new IpcRequestEnvelope(
                2,
                IpcOperationId.EndpointRpcRequest,
                new byte[IpcContractLimits.MaximumInnerPayloadSize + 1])));
    }

    [TestMethod]
    public void OversizedErrorPayloadIsRejectedBeforeDeserialization()
    {
        var codec = new EndpointRpcMessageCodec(new EndpointRpcSerializer());
        var message = new byte[EndpointRpcLimits.MaximumErrorPayloadSize + 2];
        message[0] = 0;

        Assert.ThrowsExactly<EndpointRpcPayloadException>(() => codec.DecodeResponse(message));
    }

}
