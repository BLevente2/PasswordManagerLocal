using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;
using PasswordManagerLocal.Windows.SingleInstance;

namespace PasswordManagerLocal.Windows.Ipc.Test.SingleInstance;

[TestClass]
public sealed class WindowsUiSingleInstanceControllerTests
{
    [TestMethod]
    public async Task FirstUiOwnerBecomesPrimaryWithoutActivationRequest()
    {
        var processLock = new FakeProcessInstanceLock();
        var activation = new DelegateWindowsUiActivationClient((_, _) =>
            Task.FromResult(new UiActivationResult(UiActivationResultKind.Activated, "Activated.")));
        var controller = new WindowsUiSingleInstanceController(processLock, activation);

        var role = await controller.EnterAsync();

        Assert.AreEqual(WindowsUiInstanceRole.Primary, role);
        Assert.AreEqual(1, processLock.EnsureOwnershipCount);
        Assert.AreEqual(0, activation.CallCount);
    }

    [TestMethod]
    public async Task SecondUiRequestsActivationAndExitsSecondaryPath()
    {
        var activation = new DelegateWindowsUiActivationClient((_, _) =>
            Task.FromResult(new UiActivationResult(UiActivationResultKind.Activated, "Activated.")));
        var controller = new WindowsUiSingleInstanceController(
            new FakeProcessInstanceLock(isOwner: false),
            activation,
            retryDelay: TimeSpan.Zero);

        var role = await controller.EnterAsync();

        Assert.AreEqual(WindowsUiInstanceRole.SecondaryActivationRequested, role);
        Assert.AreEqual(1, activation.CallCount);
    }

    [TestMethod]
    public async Task MissingActivationServerUsesBoundedAttempts()
    {
        var activation = new DelegateWindowsUiActivationClient((_, _) =>
            Task.FromResult(new UiActivationResult(UiActivationResultKind.Unavailable, "Unavailable.")));
        var controller = new WindowsUiSingleInstanceController(
            new FakeProcessInstanceLock(isOwner: false),
            activation,
            maximumActivationAttempts: 3,
            retryDelay: TimeSpan.Zero);

        var role = await controller.EnterAsync();

        Assert.AreEqual(WindowsUiInstanceRole.SecondaryActivationUnavailable, role);
        Assert.AreEqual(3, activation.CallCount);
    }

    [TestMethod]
    public async Task ProtocolFailureStillUsesSecondaryExitPathWithoutRetrying()
    {
        var activation = new DelegateWindowsUiActivationClient((_, _) =>
            Task.FromResult(new UiActivationResult(UiActivationResultKind.Failed, "Protocol failed.")));
        var controller = new WindowsUiSingleInstanceController(
            new FakeProcessInstanceLock(isOwner: false),
            activation,
            maximumActivationAttempts: 3,
            retryDelay: TimeSpan.Zero);

        var role = await controller.EnterAsync();

        Assert.AreEqual(WindowsUiInstanceRole.SecondaryActivationRequested, role);
        Assert.AreEqual(1, activation.CallCount);
    }
}
