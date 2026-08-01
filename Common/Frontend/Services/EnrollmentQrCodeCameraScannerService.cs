namespace PasswordManagerLocal.Common.Frontend.Services;

public static class EnrollmentQrCodeCameraScannerService
{
    private static IEnrollmentQrCodeCameraScanner? _platformScanner;

    public static bool IsAvailable => _platformScanner?.IsAvailable == true;



    public static void SetPlatformScanner(IEnrollmentQrCodeCameraScanner? platformScanner) =>
        _platformScanner = platformScanner;



    public static Task<string?> ScanEnrollmentCodeAsync(
        string? title = null,
        string? description = null,
        CancellationToken cancellationToken = default) =>
        _platformScanner?.ScanEnrollmentCodeAsync(title, description, cancellationToken) ?? Task.FromResult<string?>(null);
}
