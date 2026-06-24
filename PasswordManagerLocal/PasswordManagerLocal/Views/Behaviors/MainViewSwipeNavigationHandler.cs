using Avalonia;
using Avalonia.Input;
using PasswordManagerLocal.ViewModels;
using PasswordManagerLocal.Views;

namespace PasswordManagerLocal.Views.Behaviors;

internal sealed class MainViewSwipeNavigationHandler
{
    private const double SwipeThreshold = 96;
    private const double SwipeDominanceRatio = 1.25;

    private readonly MainView _view;
    private Point? _startPoint;
    private IPointer? _trackedPointer;
    private bool _trackingCancelled;

    public MainViewSwipeNavigationHandler(MainView view)
    {
        _view = view;
    }

    public void HandlePointerPressed(PointerPressedEventArgs e)
    {
        if (!CanStartTracking(e))
        {
            Reset();
            return;
        }

        _trackedPointer = e.Pointer;
        _startPoint = e.GetPosition(_view);
        _trackingCancelled = false;
    }

    public void HandlePointerMoved(PointerEventArgs e)
    {
        if (!CanContinueTracking(e))
            return;

        var delta = GetMovement(e);
        if (IsMostlyVerticalMovement(delta.X, delta.Y))
        {
            _trackingCancelled = true;
            return;
        }

        TryCompleteSwipe(delta.X, delta.Y, e);
    }

    public void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (!CanContinueTracking(e))
        {
            Reset();
            return;
        }

        var delta = GetMovement(e);
        TryCompleteSwipe(delta.X, delta.Y, e);
        Reset();
    }

    public void Reset()
    {
        _startPoint = null;
        _trackedPointer = null;
        _trackingCancelled = false;
    }

    private bool CanStartTracking(PointerEventArgs e) =>
        IsNavigationEnabled()
        && IsPointerInsideView(e)
        && !TextBoxClipboardHandler.IsTextInputSource(e.Source);

    private bool CanContinueTracking(PointerEventArgs e) =>
        _startPoint is not null
        && !_trackingCancelled
        && Equals(_trackedPointer, e.Pointer)
        && IsNavigationEnabled();

    private Point GetMovement(PointerEventArgs e)
    {
        var currentPoint = e.GetPosition(_view);
        var startPoint = _startPoint!.Value;
        return new Point(currentPoint.X - startPoint.X, currentPoint.Y - startPoint.Y);
    }

    private bool IsPointerInsideView(PointerEventArgs e) =>
        new Rect(0, 0, _view.Bounds.Width, _view.Bounds.Height).Contains(e.GetPosition(_view));

    private bool IsNavigationEnabled() =>
        _view.DataContext is MainViewModel
        {
            IsMobileNavigationEnabled: true,
            IsAuthenticated: true,
            IsSessionRenewalDialogOpen: false
        };

    private static bool IsMostlyVerticalMovement(double deltaX, double deltaY) =>
        Math.Abs(deltaY) >= SwipeThreshold && Math.Abs(deltaY) > Math.Abs(deltaX);

    private void TryCompleteSwipe(double deltaX, double deltaY, PointerEventArgs e)
    {
        var absoluteDeltaX = Math.Abs(deltaX);
        var absoluteDeltaY = Math.Abs(deltaY);
        if (absoluteDeltaX < SwipeThreshold || absoluteDeltaX < absoluteDeltaY * SwipeDominanceRatio)
            return;

        if (_view.DataContext is not MainViewModel viewModel)
        {
            Reset();
            return;
        }

        var changedPage = deltaX < 0
            ? viewModel.NavigateToNextMainPage()
            : viewModel.NavigateToPreviousMainPage();

        if (changedPage)
            e.Handled = true;

        Reset();
    }
}
