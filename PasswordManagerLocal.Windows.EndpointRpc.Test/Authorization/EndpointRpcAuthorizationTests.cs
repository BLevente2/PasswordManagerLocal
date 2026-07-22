using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.EndpointRpc.Authorization;
using PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Authorization;

[TestClass]
public sealed class EndpointRpcAuthorizationTests
{
    [TestMethod]
    public void UiRoleCapabilityAndControlRegistrationAreRequired()
    {
        var resolver = new FakeEndpointUiRegistrationResolver();
        var authorizer = new EndpointRpcConnectionAuthorizer(resolver);

        var wrongRole = authorizer.Authorize(CreateConnection(IpcPeerRole.TestClient, IpcCapabilities.EndpointRpc));
        var missingCapability = authorizer.Authorize(CreateConnection(IpcPeerRole.Ui, IpcCapabilities.Control));
        resolver.IsRegisteredResult = false;
        var unregistered = authorizer.Authorize(CreateConnection(IpcPeerRole.Ui, IpcCapabilities.EndpointRpc));

        Assert.AreEqual(IpcErrorCode.UnexpectedPeerRole, wrongRole.ErrorCode);
        Assert.AreEqual(IpcErrorCode.UnsupportedCapability, missingCapability.ErrorCode);
        Assert.AreEqual(IpcErrorCode.UiNotRegistered, unregistered.ErrorCode);
    }

    [TestMethod]
    public async Task DuplicateEndpointConnectionIsRejectedUntilLifecycleRelease()
    {
        var authorizer = new EndpointRpcConnectionAuthorizer(new FakeEndpointUiRegistrationResolver());
        var first = CreateConnection(IpcPeerRole.Ui, IpcCapabilities.EndpointRpc);
        var second = CreateConnection(IpcPeerRole.Ui, IpcCapabilities.EndpointRpc);

        Assert.IsTrue(authorizer.Authorize(first).IsAuthorized);
        Assert.AreEqual(IpcErrorCode.UiAlreadyRegistered, authorizer.Authorize(second).ErrorCode);

        await authorizer.OnConnectionLifecycleChangedAsync(
            new IpcConnectionLifecycleNotification(
                first.ConnectionId,
                IpcConnectionLifecycleState.Disconnected,
                first,
                IpcDisconnectKind.Clean,
                DateTimeOffset.UtcNow));

        Assert.IsTrue(authorizer.Authorize(second).IsAuthorized);
    }

    [TestMethod]
    public void EndpointOperationAuthorizerRequiresActiveRegisteredConnection()
    {
        var resolver = new FakeEndpointUiRegistrationResolver();
        var connectionAuthorizer = new EndpointRpcConnectionAuthorizer(resolver);
        var authorizer = new EndpointRpcOperationAuthorizer(connectionAuthorizer);
        var connection = CreateConnection(IpcPeerRole.Ui, IpcCapabilities.EndpointRpc);
        var request = CreateRequestContext(connection, IpcOperationId.EndpointRpcRequest);

        Assert.IsFalse(authorizer.Authorize(request).IsAuthorized);
        Assert.IsTrue(connectionAuthorizer.Authorize(connection).IsAuthorized);
        Assert.IsTrue(authorizer.Authorize(request).IsAuthorized);

        var control = authorizer.Authorize(CreateRequestContext(connection, IpcOperationId.GetAgentStatus));
        resolver.IsRegisteredResult = false;
        var unregistered = authorizer.Authorize(request);

        Assert.IsFalse(control.IsAuthorized);
        Assert.IsFalse(unregistered.IsAuthorized);
    }

    private static IpcConnectionContext CreateConnection(
        IpcPeerRole role,
        IpcCapabilities capabilities) =>
        new(Guid.NewGuid(), role, 1234, Guid.NewGuid(), capabilities);

    private static IpcRequestContext CreateRequestContext(
        IpcConnectionContext connection,
        IpcOperationId operationId) =>
        new(
            connection,
            new IpcRequestEnvelope(2, operationId, null),
            new WindowsIpcSerializer());
}
