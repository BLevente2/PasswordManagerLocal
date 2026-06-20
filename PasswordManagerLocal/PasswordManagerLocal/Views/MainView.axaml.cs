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
    private TopLevel? _keyboardTopLevel;

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
        AddHandler(
            TextBox.CuttingToClipboardEvent,
            HandleCuttingToClipboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        AttachedToVisualTree += HandleAttachedToVisualTree;
        DetachedFromVisualTree += HandleDetachedFromVisualTree;
    }



    private void HandleAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        ClipboardService.SetActiveTopLevel(topLevel);

        if (ReferenceEquals(_keyboardTopLevel, topLevel))
        {
            return;
        }

        DetachTopLevelKeyboardHandler();

        if (topLevel is not null)
        {
            _keyboardTopLevel = topLevel;
            topLevel.AddHandler(KeyDownEvent, HandleTopLevelKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }



    private void HandleDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        DetachTopLevelKeyboardHandler();



    private void DetachTopLevelKeyboardHandler()
    {
        if (_keyboardTopLevel is not null)
        {
            _keyboardTopLevel.RemoveHandler(KeyDownEvent, HandleTopLevelKeyDown);
            _keyboardTopLevel = null;
        }
    }



    private async void HandleTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        await HandleEscapeAsync();
    }



    private async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            await HandleEscapeAsync();
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control
            || e.Source is not Control sourceControl)
        {
            return;
        }

        var textBox = FindSourceTextBox(sourceControl);

        if (textBox is null)
        {
            return;
        }

        if (e.Key == Key.C)
        {
            await CopySelectedTextAsync(textBox, e);
            return;
        }

        if (e.Key == Key.X)
        {
            await CutSelectedTextAsync(textBox, e);
        }
    }



    private async Task HandleEscapeAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (await viewModel.TryNavigateBackAsync())
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (viewModel.IsAuthenticated)
        {
            if (await ShowConfirmationDialogAsync(
                    viewModel.LogoutConfirmationTitle,
                    viewModel.LogoutConfirmationMessage,
                    viewModel.YesLabel,
                    viewModel.NoLabel))
            {
                await viewModel.RequestLogoutAsync();
            }

            return;
        }

        if (await ShowConfirmationDialogAsync(
                viewModel.ExitConfirmationTitle,
                viewModel.ExitConfirmationMessage,
                viewModel.YesLabel,
                viewModel.NoLabel))
        {
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
    }



    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string yesLabel, string noLabel)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        var dialog = new ConfirmationDialog(title, message, yesLabel, noLabel);
        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true;
    }



    private async void HandleCopyingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || e.Source is not TextBox textBox)
        {
            return;
        }

        await CopySelectedTextAsync(textBox, e);
    }



    private async void HandleCuttingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || e.Source is not TextBox textBox)
        {
            return;
        }

        await CutSelectedTextAsync(textBox, e);
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

        return FindSourceTextBox(sourceControl) is not null;
    }



    private static TextBox? FindSourceTextBox(Control sourceControl) =>
        sourceControl as TextBox ?? sourceControl.FindAncestorOfType<TextBox>();



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



    private static async Task CutSelectedTextAsync(TextBox textBox, RoutedEventArgs e)
    {
        e.Handled = true;

        if (textBox.IsReadOnly)
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var selectionStart = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, text.Length);
        var selectionEnd = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, text.Length);

        if (selectionStart >= selectionEnd)
        {
            return;
        }

        var selectedText = text[selectionStart..selectionEnd];

        if (!await ClipboardService.TrySetTextAsync(selectedText))
        {
            return;
        }

        textBox.Text = text.Remove(selectionStart, selectionEnd - selectionStart);
        textBox.CaretIndex = selectionStart;
        textBox.SelectionStart = selectionStart;
        textBox.SelectionEnd = selectionStart;
    }
}
