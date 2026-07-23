using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Ipc.Authorization;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Ipc.Test.Authorization;

[TestClass]
public sealed class WindowsIpcOperationAuthorizerTests
{
    [TestMethod]
    public void PublicShellOperationsAreAllowedAfterHandshake()
    {
        var authorizer = new WindowsIpcOperationAuthorizer(new SingleUiConnectionCoordinator());
        var connectionId = Guid.NewGuid();

        foreach (var operation in new[]
        {
            IpcOperationId.Ping,
            IpcOperationId.GetAgentStatus,
            IpcOperationId.GetBackendRuntimeStatus,
            IpcOperationId.GetInteractiveSessionStatus,
            IpcOperationId.GetSynchronizationStatus,
            IpcOperationId.RequestUiOpen,
            IpcOperationId.RequestUiActivation
        })
        {
            Assert.IsTrue(authorizer.Authorize(CreateContext(connectionId, IpcPeerRole.TestClient, operation)).IsAuthorized);
        }
    }

    [TestMethod]
    public void UiRegistrationRequiresUiRole()
    {
        var authorizer = new WindowsIpcOperationAuthorizer(new SingleUiConnectionCoordinator());

        var denied = authorizer.Authorize(CreateContext(
            Guid.NewGuid(),
            IpcPeerRole.TestClient,
            IpcOperationId.RegisterUiConnection));
        var allowed = authorizer.Authorize(CreateContext(
            Guid.NewGuid(),
            IpcPeerRole.Ui,
            IpcOperationId.RegisterUiConnection));

        Assert.IsFalse(denied.IsAuthorized);
        Assert.AreEqual(IpcErrorCode.UnauthorizedOperation, denied.ErrorCode);
        Assert.IsTrue(allowed.IsAuthorized);
    }

    [TestMethod]
    public void UnregisterAndExitRequireCurrentlyRegisteredUiConnection()
    {
        var coordinator = new SingleUiConnectionCoordinator();
        var registered = CreateContext(Guid.NewGuid(), IpcPeerRole.Ui, IpcOperationId.RegisterUiConnection);
        var other = Guid.NewGuid();
        Assert.IsTrue(coordinator.TryRegister(registered.Connection, out _));
        var authorizer = new WindowsIpcOperationAuthorizer(coordinator);

        foreach (var operation in new[]
        {
            IpcOperationId.UnregisterUiConnection,
            IpcOperationId.RequestAgentExit,
            IpcOperationId.ResetDatabase
        })
        {
            Assert.IsTrue(authorizer.Authorize(CreateContext(registered.Connection.ConnectionId, IpcPeerRole.Ui, operation)).IsAuthorized);
            var denied = authorizer.Authorize(CreateContext(other, IpcPeerRole.Ui, operation));
            Assert.IsFalse(denied.IsAuthorized);
            Assert.AreEqual(IpcErrorCode.UiNotRegistered, denied.ErrorCode);
        }
    }

    [TestMethod]
    public void StoppingAgentAllowsOnlyPingAndAgentStatus()
    {
        var state = new WindowsAgentStateStore();
        state.MarkRunning(DateTimeOffset.UtcNow);
        state.MarkStopping();
        var authorizer = new WindowsAgentOperationAuthorizer(
            state,
            new WindowsIpcOperationAuthorizer(new SingleUiConnectionCoordinator()));

        Assert.IsTrue(authorizer.Authorize(CreateContext(Guid.NewGuid(), IpcPeerRole.TestClient, IpcOperationId.Ping)).IsAuthorized);
        Assert.IsTrue(authorizer.Authorize(CreateContext(Guid.NewGuid(), IpcPeerRole.TestClient, IpcOperationId.GetAgentStatus)).IsAuthorized);
        var denied = authorizer.Authorize(CreateContext(Guid.NewGuid(), IpcPeerRole.TestClient, IpcOperationId.RequestUiOpen));
        Assert.IsFalse(denied.IsAuthorized);
        Assert.AreEqual(IpcErrorCode.AgentStopping, denied.ErrorCode);
    }

    private static IpcRequestContext CreateContext(
        Guid connectionId,
        IpcPeerRole role,
        IpcOperationId operation) =>
        new(
            new IpcConnectionContext(
                connectionId,
                role,
                PeerProcessId: 100,
                0,
                PeerSessionId: Guid.NewGuid(),
                PeerCapabilities: IpcCapabilities.Control | IpcCapabilities.Status | IpcCapabilities.UiActivation),
            new IpcRequestEnvelope(1, operation, Payload: null),
            new WindowsIpcSerializer());
}
