using PasswordManagerLocal.Frontend.Services;

namespace PasswordManagerLocal.Android;

public sealed class AndroidQrCodeCameraScanner : IEnrollmentQrCodeCameraScanner
{
    private readonly MainActivity _activity;

    public AndroidQrCodeCameraScanner(MainActivity activity) =>
        _activity = activity;

    public bool IsAvailable => true;

    public Task<string?> ScanEnrollmentCodeAsync(
        string? title = null,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        _activity.ScanEnrollmentQrCodeAsync(title, description, cancellationToken);
}
