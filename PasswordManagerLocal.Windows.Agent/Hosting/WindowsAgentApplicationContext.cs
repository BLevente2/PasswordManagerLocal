using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using System.Windows.Forms;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentApplicationContext : ApplicationContext
{
    private readonly IWindowsAgentHost _host;
    private readonly WindowsAgentStateStore _stateStore;
    private readonly Control _dispatcher = new();
    private int _startupCompleted;
    private int _failureExitRequested;

    public bool ShellFailed { get; private set; }

    public WindowsAgentApplicationContext(
        IWindowsAgentHost host,
        WindowsAgentStateStore stateStore)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _dispatcher.CreateControl();
        _stateStore.StateChanged += HandleStateChanged;
        // Start through the message loop so tray initialization stays on the WinForms UI thread.
        _dispatcher.BeginInvoke((MethodInvoker)(() => _ = StartAsync()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateStore.StateChanged -= HandleStateChanged;
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task StartAsync()
    {
        try
        {
            await _host.StartAsync();
            Interlocked.Exchange(ref _startupCompleted, 1);
            if (_stateStore.State == AgentState.Failed)
                RequestFailureExitThread();
        }
        catch (ProcessInstanceAlreadyOwnedException)
        {
            ShellFailed = true;
            RequestExitThread();
        }
        catch
        {
            ShellFailed = true;
            MessageBox.Show(
                "The PasswordManagerLocal agent could not start.",
                "PasswordManagerLocal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            RequestExitThread();
        }
    }

    private void HandleStateChanged(object? sender, WindowsAgentStateChangedEventArgs args)
    {
        if (args.Current == AgentState.Failed)
        {
            ShellFailed = true;
            if (Volatile.Read(ref _startupCompleted) != 0)
                RequestFailureExitThread();
            return;
        }

        if (args.Current == AgentState.Stopped)
            RequestExitThread();
    }

    private void RequestFailureExitThread()
    {
        if (Interlocked.Exchange(ref _failureExitRequested, 1) != 0)
            return;

        if (!_dispatcher.InvokeRequired)
        {
            ExitThread();
            return;
        }

        _dispatcher.BeginInvoke((MethodInvoker)ExitThread);
    }

    private void RequestExitThread()
    {
        if (!_dispatcher.InvokeRequired)
        {
            ExitThread();
            return;
        }

        _dispatcher.BeginInvoke((MethodInvoker)ExitThread);
    }
}
