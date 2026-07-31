using PasswordManagerLocal.Contracts.Preferences;
using System.Drawing;
using System.Windows.Forms;

namespace PasswordManagerLocal.Windows.Agent.Tray;

public sealed class WindowsFormsTrayIconAdapter : ITrayIconAdapter
{
    private readonly string _iconPath;
    private readonly AppLanguage _language;
    private readonly Control _dispatcher = new();
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private Icon? _icon;
    private int _disposed;

    public WindowsFormsTrayIconAdapter(string iconPath, AppLanguage language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        if (language is not AppLanguage.English and not AppLanguage.Hungarian)
            throw new ArgumentOutOfRangeException(nameof(language));
        _iconPath = Path.GetFullPath(iconPath);
        _language = language;
        _dispatcher.CreateControl();
    }

    public event EventHandler<TrayIconMouseEventArgs>? MouseClicked;
    public event EventHandler? OpenCommandSelected;
    public event EventHandler? ExitCommandSelected;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(_iconPath))
                throw new FileNotFoundException("The tray icon resource was not found.", _iconPath);

            _icon = new Icon(_iconPath);
            var openItem = new ToolStripMenuItem(
                _language == AppLanguage.Hungarian
                    ? "PasswordManagerLocal megnyitása"
                    : "Open PasswordManagerLocal");
            var exitItem = new ToolStripMenuItem(
                _language == AppLanguage.Hungarian
                    ? "Kilépés"
                    : "Exit");
            openItem.Click += (_, _) => OpenCommandSelected?.Invoke(this, EventArgs.Empty);
            exitItem.Click += (_, _) => ExitCommandSelected?.Invoke(this, EventArgs.Empty);
            _menu = new ContextMenuStrip();
            _menu.Items.Add(openItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _notifyIcon = new NotifyIcon
            {
                Icon = _icon,
                Text = "PasswordManagerLocal",
                ContextMenuStrip = _menu
            };
            _notifyIcon.MouseClick += HandleMouseClick;
            _notifyIcon.Visible = true;
        });

    public Task ShowErrorAsync(
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        return InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _notifyIcon?.ShowBalloonTip(
                4000,
                "PasswordManagerLocal",
                safeMessage,
                ToolTipIcon.Error);
        });
    }

    public Task HideAndDisposeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return Task.CompletedTask;

        return InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.MouseClick -= HandleMouseClick;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _menu?.Dispose();
            _menu = null;
            _icon?.Dispose();
            _icon = null;
            _dispatcher.Dispose();
        });
    }

    private void HandleMouseClick(object? sender, MouseEventArgs args)
    {
        var button = args.Button switch
        {
            MouseButtons.Left => TrayIconMouseButton.Left,
            MouseButtons.Right => TrayIconMouseButton.Right,
            _ => TrayIconMouseButton.Other
        };
        MouseClicked?.Invoke(this, new TrayIconMouseEventArgs(button, args.Clicks));
    }

    private Task InvokeAsync(Action action)
    {
        if (!_dispatcher.InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.BeginInvoke((MethodInvoker)(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }));
        return completion.Task;
    }
}
