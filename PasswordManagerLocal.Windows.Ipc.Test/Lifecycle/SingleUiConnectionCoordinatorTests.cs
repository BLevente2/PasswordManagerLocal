using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.Ipc.Server;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Protocol;

namespace PasswordManagerLocal.Windows.Ipc.Test.Lifecycle;

[TestClass]
public sealed class SingleUiConnectionCoordinatorTests
{
    [TestMethod]
    public void SecondConnectionIsRejectedAndStaleUnregisterCannotRemoveCurrentConnection()
    {
        var coordinator = new SingleUiConnectionCoordinator();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.IsTrue(coordinator.TryRegister(first));
        Assert.IsTrue(coordinator.TryRegister(first));
        Assert.IsFalse(coordinator.TryRegister(second));
        Assert.IsFalse(coordinator.Unregister(second));
        Assert.AreEqual(first, coordinator.RegisteredConnectionId);
        Assert.IsTrue(coordinator.Unregister(first));
        Assert.IsFalse(coordinator.Unregister(first));
        Assert.IsNull(coordinator.RegisteredConnectionId);
        Assert.IsTrue(coordinator.TryRegister(second));
        Assert.IsFalse(coordinator.Unregister(first));
        Assert.AreEqual(second, coordinator.RegisteredConnectionId);
    }

    [TestMethod]
    public void ServerAcceptingUiClientsRequiresCoordinator()
    {
        var pair = new InMemoryIpcConnectionPair();

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new WindowsIpcServerConnectionSession(
                pair.Server,
                new WindowsIpcSerializer(),
                new WindowsIpcRequestDispatcher(Array.Empty<IWindowsIpcRequestHandler>()),
                new WindowsIpcServerOptions(
                    IpcPeerRole.Agent,
                    new[] { IpcPeerRole.Ui },
                    IpcCapabilities.Control)));
    }

    [TestMethod]
    public void EmptyConnectionIdentifiersAreRejected()
    {
        var coordinator = new SingleUiConnectionCoordinator();

        Assert.ThrowsExactly<ArgumentException>(() => coordinator.TryRegister(Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => coordinator.Unregister(Guid.Empty));
    }
}
