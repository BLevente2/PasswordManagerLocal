using PasswordManagerLocal.Windows.Ipc.Coordination;
using System.Diagnostics;

namespace PasswordManagerLocal.Windows.Agent.Ui;

public sealed class WindowsUiLauncher : IWindowsUiLauncher
{
    private readonly string _uiExecutablePath;
    private readonly IProcessLauncher _processLauncher;

    public WindowsUiLauncher(
        string executableDirectory,
        IProcessLauncher? processLauncher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        var fullDirectory = Path.GetFullPath(executableDirectory);
        var trimmedDirectory = Path.TrimEndingDirectorySeparator(fullDirectory);
        var uiDirectory = string.Equals(
            Path.GetFileName(trimmedDirectory),
            WindowsExecutableNames.AgentDeploymentDirectoryName,
            StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(trimmedDirectory)?.FullName ?? fullDirectory
            : fullDirectory;
        _uiExecutablePath = Path.Combine(
            uiDirectory,
            WindowsExecutableNames.UiExecutableFileName);
        _processLauncher = processLauncher ?? new WindowsProcessLauncher();
    }

    public Task<UiLaunchResult> LaunchAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_uiExecutablePath))
        {
            return Task.FromResult(new UiLaunchResult(
                UiLaunchResultKind.ExecutableNotFound,
                "The PasswordManagerLocal UI executable was not found."));
        }

        try
        {
            var started = _processLauncher.TryStart(new ProcessStartInfo
            {
                FileName = _uiExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(_uiExecutablePath)!,
                UseShellExecute = true
            });
            return Task.FromResult(started
                ? new UiLaunchResult(
                    UiLaunchResultKind.LaunchRequested,
                    "The PasswordManagerLocal UI launch was requested.")
                : new UiLaunchResult(
                    UiLaunchResultKind.LaunchFailed,
                    "The PasswordManagerLocal UI could not be launched."));
        }
        catch
        {
            return Task.FromResult(new UiLaunchResult(
                UiLaunchResultKind.LaunchFailed,
                "The PasswordManagerLocal UI could not be launched."));
        }
    }
}
