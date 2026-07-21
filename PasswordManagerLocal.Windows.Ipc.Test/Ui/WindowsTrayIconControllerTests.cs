using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Tray;
using PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

namespace PasswordManagerLocal.Windows.Ipc.Test.Ui;

[TestClass]
public sealed class WindowsTrayIconControllerTests
{
    [TestMethod]
    public async Task LeftClickTriggersOpenOnceAndRightClickDoesNot()
    {
        var adapter = new FakeTrayIconAdapter();
        await using var controller = new WindowsTrayIconController(adapter);
        var openCount = 0;
        controller.OpenRequested += (_, _) => openCount++;
        await controller.InitializeAsync();

        adapter.RaiseMouse(TrayIconMouseButton.Left);
        adapter.RaiseMouse(TrayIconMouseButton.Right);
        adapter.RaiseMouse(TrayIconMouseButton.Left, clicks: 2);

        Assert.AreEqual(1, openCount);
    }

    [TestMethod]
    public async Task ContextMenuCommandsRaiseOpenAndExit()
    {
        var adapter = new FakeTrayIconAdapter();
        await using var controller = new WindowsTrayIconController(adapter);
        var openCount = 0;
        var exitCount = 0;
        controller.OpenRequested += (_, _) => openCount++;
        controller.ExitRequested += (_, _) => exitCount++;
        await controller.InitializeAsync();

        adapter.RaiseOpenCommand();
        adapter.RaiseExitCommand();

        Assert.AreEqual(1, openCount);
        Assert.AreEqual(1, exitCount);
    }

    [TestMethod]
    public async Task DisposalHidesAndDisposesIconAndIsIdempotent()
    {
        var adapter = new FakeTrayIconAdapter();
        var controller = new WindowsTrayIconController(adapter);
        await controller.InitializeAsync();

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        Assert.AreEqual(1, adapter.HideAndDisposeCount);
    }
}
