using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PasswordManagerLocal.Services;
using PasswordManagerLocal.ViewModels;

namespace PasswordManagerLocal.Views;

public partial class MainView : UserControl
{
    private const double SwipeThreshold = 96;
    private const double SwipeDominanceRatio = 1.25;

    private Point? _swipeStartPoint;

    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, HandlePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(
            TextBox.CopyingToClipboardEvent,
            HandleCopyingToClipboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        AttachedToVisualTree += (_, _) => ClipboardService.SetActiveTopLevel(TopLevel.GetTopLevel(this));
    }



    private async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled
            || e.Key != Key.C
            || (e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control
            || e.Source is not Control sourceControl)
        {
            return;
        }

        var textBox = sourceControl as TextBox ?? sourceControl.FindAncestorOfType<TextBox>();

        if (textBox is null)
        {
            return;
        }

        await CopySelectedTextAsync(textBox, e);
    }



    private async void HandleCopyingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || e.Source is not TextBox textBox)
        {
            return;
        }

        await CopySelectedTextAsync(textBox, e);
    }



    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsMobileSwipeNavigationEnabled() || IsTextInputSource(e.Source))
        {
            _swipeStartPoint = null;
            return;
        }

        _swipeStartPoint = e.GetPosition(this);
    }



    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_swipeStartPoint is not { } startPoint)
        {
            return;
        }

        _swipeStartPoint = null;

        if (!IsMobileSwipeNavigationEnabled())
        {
            return;
        }

        var endPoint = e.GetPosition(this);
        var deltaX = endPoint.X - startPoint.X;
        var deltaY = endPoint.Y - startPoint.Y;
        var absoluteDeltaX = Math.Abs(deltaX);
        var absoluteDeltaY = Math.Abs(deltaY);

        if (absoluteDeltaX < SwipeThreshold || absoluteDeltaX < absoluteDeltaY * SwipeDominanceRatio)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var changedPage = deltaX < 0
            ? viewModel.NavigateToNextMainPage()
            : viewModel.NavigateToPreviousMainPage();

        if (changedPage)
        {
            e.Handled = true;
        }
    }



    private bool IsMobileSwipeNavigationEnabled() =>
        DataContext is MainViewModel { IsMobileNavigationEnabled: true, IsAuthenticated: true };



    private static bool IsTextInputSource(object? source)
    {
        if (source is not Control sourceControl)
        {
            return false;
        }

        return sourceControl is TextBox || sourceControl.FindAncestorOfType<TextBox>() is not null;
    }



    private static async Task CopySelectedTextAsync(TextBox textBox, RoutedEventArgs e)
    {
        var selectedText = ClipboardService.GetSelectedText(textBox);

        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        e.Handled = true;
        await ClipboardService.TrySetTextAsync(selectedText);
    }
}
