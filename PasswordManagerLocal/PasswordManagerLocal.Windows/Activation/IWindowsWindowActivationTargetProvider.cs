namespace PasswordManagerLocal.Windows.Activation;

public interface IWindowsWindowActivationTargetProvider
{
    IWindowsWindowActivationTarget? GetTarget();
}
