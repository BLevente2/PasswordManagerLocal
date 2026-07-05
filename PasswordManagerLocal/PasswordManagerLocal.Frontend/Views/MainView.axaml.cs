using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Frontend.ViewModels;
using PasswordManagerLocal.Frontend.Views.Behaviors;
using System.ComponentModel;

namespace PasswordManagerLocal.Frontend.Views;

public partial class MainView : UserControl
{
    private readonly MainViewKeyboardHandler _keyboardHandler;
    private readonly MainViewSwipeNavigationHandler _swipeNavigationHandler;
    private readonly MainViewTapOutsideKeyboardDismissHandler _tapOutsideKeyboardDismissHandler;
    private readonly MainViewLongPressToolTipHandler _longPressToolTipHandler;
    private TopLevel? _inputTopLevel;
    private MainViewModel? _observedViewModel;

    public MainView()
    {
        InitializeComponent();
        _keyboardHandler = new MainViewKeyboardHandler(this);
        _swipeNavigationHandler = new MainViewSwipeNavigationHandler(this);
        _tapOutsideKeyboardDismissHandler = new MainViewTapOutsideKeyboardDismissHandler(this);
        _longPressToolTipHandler = new MainViewLongPressToolTipHandler(this);
        RegisterLocalInputHandlers();
        RegisterLifecycleHandlers();
        HandleDataContextChanged(this, EventArgs.Empty);
    }

    private void RegisterLocalInputHandlers()
    {
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel);
        AddHandler(
            TextBox.CopyingToClipboardEvent,
            HandleCopyingToClipboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        AddHandler(
            TextBox.CuttingToClipboardEvent,
            HandleCuttingToClipboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
    }

    private void RegisterLifecycleHandlers()
    {
        AttachedToVisualTree += HandleAttachedToVisualTree;
        DetachedFromVisualTree += HandleDetachedFromVisualTree;
        DataContextChanged += HandleDataContextChanged;
    }

    private void HandleAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HandleDataContextChanged(this, EventArgs.Empty);
        var topLevel = TopLevel.GetTopLevel(this);
        SetActiveTopLevelForUiServices(topLevel);

        if (ReferenceEquals(_inputTopLevel, topLevel))
            return;

        DetachTopLevelInputHandlers();
        AttachTopLevelInputHandlers(topLevel);
    }

    private void SetActiveTopLevelForUiServices(TopLevel? topLevel)
    {
        ClipboardService.SetActiveTopLevel(topLevel);
        QrImagePickerService.SetActiveTopLevel(topLevel);
        FirewallPermissionStartupPrompt.SetActiveTopLevel(topLevel);
    }

    private void AttachTopLevelInputHandlers(TopLevel? topLevel)
    {
        if (topLevel is null)
            return;

        _inputTopLevel = topLevel;
        topLevel.AddHandler(KeyDownEvent, HandleTopLevelKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (!OperatingSystem.IsAndroid())
            return;

        topLevel.AddHandler(PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        topLevel.AddHandler(PointerMovedEvent, HandlePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        topLevel.AddHandler(PointerReleasedEvent, HandlePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, HandlePointerCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
    }

    private void HandleDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachTopLevelInputHandlers();
        FirewallPermissionStartupPrompt.SetActiveTopLevel(null);
        DetachObservedViewModel();
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        DetachObservedViewModel();
        if (DataContext is not MainViewModel viewModel)
            return;

        _observedViewModel = viewModel;
        _observedViewModel.PropertyChanged += HandleViewModelPropertyChanged;
    }

    private void DetachObservedViewModel()
    {
        if (_observedViewModel is null)
            return;

        _observedViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        _observedViewModel = null;
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var loggedOut = e.PropertyName == nameof(MainViewModel.IsAuthenticated)
            && sender is MainViewModel { IsAuthenticated: false };
        if (loggedOut || e.PropertyName == nameof(MainViewModel.CurrentUserDisplayName))
            HideAccountMenuFlyout();
    }

    private void HandleAccountMenuActionClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(HideAccountMenuFlyout);

    private void HideAccountMenuFlyout()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideAccountMenuFlyout);
            return;
        }

        this.FindControl<Button>("AccountMenuButton")?.Flyout?.Hide();
        this.FindControl<Button>("MobileAccountMenuButton")?.Flyout?.Hide();
    }

    private void DetachTopLevelInputHandlers()
    {
        if (_inputTopLevel is not null)
        {
            _inputTopLevel.RemoveHandler(KeyDownEvent, HandleTopLevelKeyDown);
            if (OperatingSystem.IsAndroid())
            {
                _inputTopLevel.RemoveHandler(PointerPressedEvent, HandlePointerPressed);
                _inputTopLevel.RemoveHandler(PointerMovedEvent, HandlePointerMoved);
                _inputTopLevel.RemoveHandler(PointerReleasedEvent, HandlePointerReleased);
                RemoveHandler(PointerCaptureLostEvent, HandlePointerCaptureLost);
            }

            _inputTopLevel = null;
        }

        _swipeNavigationHandler.Reset();
        _longPressToolTipHandler.Reset();
    }

    public async Task<bool> HandleBackRequestAsync()
    {
        _longPressToolTipHandler.DismissOpenToolTip();

        if (TryDismissOpenFlyout())
            return true;

        return await _keyboardHandler.HandleBackRequestCoreAsync();
    }

    private bool TryDismissOpenFlyout()
    {
        foreach (var descendant in this.GetVisualDescendants())
        {
            if (descendant is not Button button || button.Flyout?.IsOpen != true)
                continue;

            button.Flyout.Hide();
            return true;
        }

        return false;
    }

    private async void HandleTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        _longPressToolTipHandler.DismissOpenToolTip();
        await _keyboardHandler.HandleTopLevelKeyDownAsync(e);
    }

    private async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        _longPressToolTipHandler.DismissOpenToolTip();
        await _keyboardHandler.HandleKeyDownAsync(e);
    }

    private async void HandleCopyingToClipboard(object? sender, RoutedEventArgs e) =>
        await _keyboardHandler.HandleCopyingToClipboardAsync(e);

    private async void HandleCuttingToClipboard(object? sender, RoutedEventArgs e) =>
        await _keyboardHandler.HandleCuttingToClipboardAsync(e);

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _longPressToolTipHandler.HandlePointerPressed(e);
        _tapOutsideKeyboardDismissHandler.HandlePointerPressed(e);
        _swipeNavigationHandler.HandlePointerPressed(e);
    }

    private void HandlePointerMoved(object? sender, PointerEventArgs e)
    {
        _longPressToolTipHandler.HandlePointerMoved(e);
        if (e.Handled)
            return;

        _swipeNavigationHandler.HandlePointerMoved(e);
    }

    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPressToolTipHandler.HandlePointerReleased(e);
        if (e.Handled)
        {
            _swipeNavigationHandler.Reset();
            return;
        }

        _swipeNavigationHandler.HandlePointerReleased(e);
    }

    private void HandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _longPressToolTipHandler.HandlePointerCaptureLost(e);
        _swipeNavigationHandler.HandlePointerCaptureLost(e);
    }
}
