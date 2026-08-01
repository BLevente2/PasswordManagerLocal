using PasswordManagerLocal.Windows.Agent.Native;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PasswordManagerLocal.Windows.Agent.Tray;

internal sealed class WindowsNativeTrayIconAdapter : ITrayIconAdapter
{
    private const uint IconId = 1;
    private const uint OpenCommandId = 1001;
    private const uint ExitCommandId = 1002;

    private readonly string _iconPath;
    private readonly WindowsAgentTrayText _text;
    private readonly WindowsNativeMessageWindow _messageWindow;
    private readonly WindowsNativeShellDispatcher _dispatcher;
    private WindowsNativeIcon? _icon;
    private uint _taskbarCreatedMessage;
    private bool _initialized;
    private bool _visibleRequested;
    private bool _isAdded;
    private bool _version4Enabled;
    private int _disposed;

    internal WindowsNativeTrayIconAdapter(
        string iconPath,
        WindowsAgentTrayText text,
        WindowsNativeMessageWindow messageWindow,
        WindowsNativeShellDispatcher dispatcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        _iconPath = Path.GetFullPath(iconPath);
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _messageWindow.MessageReceived += HandleWindowMessage;
    }

    public event EventHandler<TrayIconMouseEventArgs>? MouseClicked;
    public event EventHandler? OpenCommandSelected;
    public event EventHandler? ExitCommandSelected;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (_initialized)
                throw new InvalidOperationException("The native tray icon has already been initialized.");

            try
            {
                _icon = WindowsNativeIcon.LoadOwned(_iconPath);
                _taskbarCreatedMessage = WindowsNativeMethods.RegisterWindowMessage("TaskbarCreated");
                if (_taskbarCreatedMessage == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                _initialized = true;
            }
            catch
            {
                _icon?.Dispose();
                _icon = null;
                throw;
            }
        }, cancellationToken);

    public Task SetVisibleAsync(
        bool isVisible,
        CancellationToken cancellationToken = default) =>
        _dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (!_initialized)
                throw new InvalidOperationException("The native tray icon has not been initialized.");

            _visibleRequested = isVisible;
            if (isVisible)
                AddIconIfNeeded();
            else
                RemoveIconIfNeeded(throwOnFailure: true);
        }, cancellationToken);

    public Task ShowErrorAsync(
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        return _dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (!_isAdded)
                return;

            var data = CreateNotifyIconData(WindowsNativeMethods.NifInfo);
            data.Info = Truncate(safeMessage, 255);
            data.InfoTitle = "PasswordManagerLocal";
            data.InfoFlags = WindowsNativeMethods.NiifError;
            data.TimeoutOrVersion = 4000;
            if (!WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimModify, ref data))
                Trace.TraceError("The native tray error notification could not be displayed.");
        }, cancellationToken);
    }

    public Task HideAndDisposeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return Task.CompletedTask;

        return _dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _visibleRequested = false;
            RemoveIconIfNeeded(throwOnFailure: false);
            _icon?.Dispose();
            _icon = null;
            _initialized = false;
            _messageWindow.MessageReceived -= HandleWindowMessage;
        }, cancellationToken);
    }

    private nint? HandleWindowMessage(uint message, nuint wParam, nint lParam)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_initialized)
            return null;

        if (message == _taskbarCreatedMessage)
        {
            _isAdded = false;
            _version4Enabled = false;
            if (_visibleRequested)
            {
                try { AddIconIfNeeded(); }
                catch (Exception exception)
                {
                    Trace.TraceError($"The tray icon could not be restored after Explorer restart: {exception.GetType().Name}");
                }
            }
            return 0;
        }

        if (message == WindowsNativeMethods.WmCommand)
        {
            var command = unchecked((uint)wParam) & 0xFFFF;
            if (command == OpenCommandId)
            {
                Publish(OpenCommandSelected);
                return 0;
            }
            if (command == ExitCommandId)
            {
                Publish(ExitCommandSelected);
                return 0;
            }
            return null;
        }

        if (message != WindowsNativeApplicationLoop.TrayCallbackMessage)
            return null;

        var notification = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
        if (notification is WindowsNativeMethods.WmLButtonUp or WindowsNativeMethods.NinSelect or WindowsNativeMethods.NinKeySelect)
        {
            PublishMouseClick(TrayIconMouseButton.Left);
            return 0;
        }
        if (notification is WindowsNativeMethods.WmRButtonUp or WindowsNativeMethods.WmContextMenu)
        {
            ShowContextMenu(wParam);
            return 0;
        }

        return 0;
    }

    private void AddIconIfNeeded()
    {
        if (_isAdded)
            return;
        if (_icon is null || _icon.Handle == 0)
            throw new InvalidOperationException("The native tray icon resource is unavailable.");

        var data = CreateNotifyIconData(
            WindowsNativeMethods.NifMessage |
            WindowsNativeMethods.NifIcon |
            WindowsNativeMethods.NifTip |
            WindowsNativeMethods.NifShowTip);
        if (!WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimAdd, ref data))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        _isAdded = true;
        var version = CreateNotifyIconData(0);
        version.TimeoutOrVersion = WindowsNativeMethods.NotifyIconVersion4;
        _version4Enabled = WindowsNativeMethods.ShellNotifyIcon(
            WindowsNativeMethods.NimSetVersion,
            ref version);
        if (!_version4Enabled)
            Trace.TraceWarning("The tray icon could not enable NOTIFYICON_VERSION_4 semantics.");
    }

    private void RemoveIconIfNeeded(bool throwOnFailure)
    {
        if (!_isAdded)
            return;

        var data = CreateNotifyIconData(0);
        var removed = WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimDelete, ref data);
        _isAdded = false;
        _version4Enabled = false;
        if (!removed && throwOnFailure)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!removed)
            Trace.TraceWarning("The native tray icon could not be removed cleanly.");
    }

    private void ShowContextMenu(nuint callbackPosition)
    {
        if (!_isAdded)
            return;

        var menu = WindowsNativeMethods.CreatePopupMenu();
        if (menu == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            AppendMenu(menu, WindowsNativeMethods.MfString, OpenCommandId, _text.OpenLabel);
            AppendMenu(menu, WindowsNativeMethods.MfSeparator, 0, null);
            AppendMenu(menu, WindowsNativeMethods.MfString, ExitCommandId, _text.ExitLabel);
            var point = ResolveMenuPosition(callbackPosition);

            WindowsNativeMethods.SetForegroundWindow(_messageWindow.WindowHandle);
            WindowsNativeMethods.TrackPopupMenuEx(
                menu,
                WindowsNativeMethods.TpmRightButton,
                point.X,
                point.Y,
                _messageWindow.WindowHandle,
                0);
            WindowsNativeMethods.PostMessage(
                _messageWindow.WindowHandle,
                WindowsNativeMethods.WmNull,
                0,
                0);
        }
        finally
        {
            if (!WindowsNativeMethods.DestroyMenu(menu))
                Trace.TraceWarning("A native tray context-menu handle could not be destroyed.");
        }
    }

    private WindowsNativeMethods.Point ResolveMenuPosition(nuint callbackPosition)
    {
        if (_version4Enabled)
        {
            var packed = unchecked((uint)callbackPosition);
            var point = new WindowsNativeMethods.Point
            {
                X = unchecked((short)(packed & 0xFFFF)),
                Y = unchecked((short)((packed >> 16) & 0xFFFF))
            };
            if (point.X != -1 || point.Y != -1)
                return point;
        }

        if (!WindowsNativeMethods.GetCursorPosition(out var cursor))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return cursor;
    }

    private WindowsNativeMethods.NotifyIconData CreateNotifyIconData(uint flags) =>
        new()
        {
            Size = (uint)Marshal.SizeOf<WindowsNativeMethods.NotifyIconData>(),
            Window = _messageWindow.WindowHandle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = WindowsNativeApplicationLoop.TrayCallbackMessage,
            Icon = _icon?.Handle ?? 0,
            Tip = Truncate(_text.ToolTip, 127),
            Info = string.Empty,
            InfoTitle = string.Empty
        };

    private static void AppendMenu(nint menu, uint flags, uint id, string? text)
    {
        if (!WindowsNativeMethods.AppendMenu(menu, flags, (nuint)id, text))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void PublishMouseClick(TrayIconMouseButton button)
    {
        var handlers = MouseClicked;
        if (handlers is null)
            return;
        var args = new TrayIconMouseEventArgs(button, 1);
        foreach (EventHandler<TrayIconMouseEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch (Exception exception)
            {
                Trace.TraceError($"A native tray mouse callback failed: {exception.GetType().Name}");
            }
        }
    }

    private void Publish(EventHandler? handlers)
    {
        if (handlers is null)
            return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception)
            {
                Trace.TraceError($"A native tray command callback failed: {exception.GetType().Name}");
            }
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsNativeTrayIconAdapter));
    }
}
