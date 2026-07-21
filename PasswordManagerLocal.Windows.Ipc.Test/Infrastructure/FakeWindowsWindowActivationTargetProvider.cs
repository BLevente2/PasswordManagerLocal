using PasswordManagerLocal.Windows.Activation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsWindowActivationTargetProvider : IWindowsWindowActivationTargetProvider
{
    public IWindowsWindowActivationTarget? Target { get; set; }
    public int GetTargetCount { get; private set; }

    public IWindowsWindowActivationTarget? GetTarget()
    {
        GetTargetCount++;
        return Target;
    }
}
