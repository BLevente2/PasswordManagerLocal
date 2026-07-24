using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeBackendExecutionProfileProvider : IBackendExecutionProfileProvider
{
    public BackendExecutionProfile? Current { get; set; }
    public bool IsInteractive { get; set; }
    public bool IsEnrollmentAllowed => IsInteractive;
    public event EventHandler? ProfileChanged;

    public void PublishProfileChanged() =>
        ProfileChanged?.Invoke(this, EventArgs.Empty);
}
