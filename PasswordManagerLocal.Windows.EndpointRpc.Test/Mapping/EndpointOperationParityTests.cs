using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Windows.EndpointRpc.Contracts;
using PasswordManagerLocal.Windows.EndpointRpc.Metadata;
using PasswordManagerLocal.Windows.EndpointRpc.Serialization;
using PasswordManagerLocal.Windows.EndpointRpc.Validation;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Mapping;

[TestClass]
public sealed class EndpointOperationParityTests
{
    [TestMethod]
    public void EveryEndpointMethodHasExactlyOneCompleteMapping()
    {
        var methods = typeof(IEndpoints).GetMethods();
        var descriptors = EndpointOperationManifest.All;
        var operationIds = Enum.GetValues<EndpointOperationId>();
        var validator = new EndpointRpcContractValidator();

        Assert.HasCount(40, methods);
        Assert.HasCount(methods.Length, descriptors);
        Assert.HasCount(methods.Length, operationIds);
        Assert.HasCount(operationIds.Length, operationIds.Distinct());
        Assert.HasCount(operationIds.Length, descriptors.Select(item => item.OperationId).Distinct());
        CollectionAssert.AreEquivalent(
            operationIds.Cast<object>().ToArray(),
            descriptors.Select(item => (object)item.OperationId).ToArray());
        Assert.IsFalse(operationIds.Any(value => (int)value == 0));

        foreach (var method in methods)
        {
            var matches = descriptors.Where(item => item.MethodName == method.Name).ToArray();
            Assert.HasCount(1, matches, method.Name);
            var parameters = method.GetParameters();
            Assert.IsTrue(parameters.Length > 0, method.Name);
            Assert.AreEqual(typeof(CancellationToken), parameters[^1].ParameterType, method.Name);
            Assert.IsTrue(parameters[^1].HasDefaultValue, method.Name);
        }

        foreach (var descriptor in descriptors)
        {
            Assert.AreEqual(descriptor, EndpointOperationManifest.Get(descriptor.OperationId));
            Assert.IsTrue(Enum.IsDefined(descriptor.OperationId));
            Assert.IsTrue(validator.CanValidateRequest(descriptor.RequestType));
            Assert.IsTrue(validator.CanValidateResponse(descriptor.ResponseType));
            Assert.IsNotNull(EndpointRpcJsonContext.Default.GetTypeInfo(descriptor.RequestType));
            Assert.IsNotNull(EndpointRpcJsonContext.Default.GetTypeInfo(descriptor.ResponseType));
            Assert.IsTrue(descriptor.MaximumRequestPayloadSize > 0);
            Assert.IsTrue(descriptor.MaximumRequestPayloadSize <= EndpointRpcLimits.MaximumRequestPayloadSize);
            Assert.IsTrue(descriptor.MaximumResponsePayloadSize > 0);
            Assert.IsTrue(descriptor.MaximumResponsePayloadSize <= EndpointRpcLimits.MaximumResponsePayloadSize);
            Assert.IsTrue(descriptor.MaximumLogicalResponsePayloadSize >= descriptor.MaximumResponsePayloadSize);
            Assert.IsTrue(descriptor.MaximumLogicalResponsePayloadSize <= EndpointRpcLimits.MaximumLargeResultTotalBytes);
            Assert.IsTrue(Enum.IsDefined(descriptor.CancellationClassification));
        }
    }
    [TestMethod]
    public void OnlySavedPasswordsUsesTheBoundedLargeResponseProtocol()
    {
        var largeDescriptors = EndpointOperationManifest.All
            .Where(descriptor => descriptor.SupportsLargeResponse)
            .ToArray();

        Assert.HasCount(1, largeDescriptors);
        Assert.AreEqual(EndpointOperationId.GetSavedPasswords, largeDescriptors[0].OperationId);
        Assert.AreEqual(
            EndpointRpcLimits.MaximumLargeResultTotalBytes,
            largeDescriptors[0].MaximumLogicalResponsePayloadSize);
        Assert.IsTrue(EndpointOperationManifest.All
            .Where(descriptor => descriptor.OperationId != EndpointOperationId.GetSavedPasswords)
            .All(descriptor => !descriptor.SupportsLargeResponse));
    }

    [TestMethod]
    public void InternalLargeTransferContractsUseSourceGeneratedMetadata()
    {
        var internalTypes = new[]
        {
            typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.EndpointLargeResultDescriptor),
            typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.GetEndpointLargeResultChunkRequest),
            typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.GetEndpointLargeResultChunkResponse),
            typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.ReleaseEndpointLargeResultRequest),
            typeof(PasswordManagerLocal.Windows.EndpointRpc.Contracts.LargeTransfer.ReleaseEndpointLargeResultResponse)
        };

        foreach (var type in internalTypes)
            Assert.IsNotNull(EndpointRpcJsonContext.Default.GetTypeInfo(type), type.FullName);
    }

}
