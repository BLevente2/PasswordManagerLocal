using Android.App;
using Android.Runtime;
using PasswordManagerLocal.Common.Backend.Constants;

namespace PasswordManagerLocal.Android.Frontend;

[Application]
public sealed class PasswordManagerLocalApplication : Application
{
    public PasswordManagerLocalApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public AndroidRuntimeServiceConnector RuntimeServiceConnector { get; private set; } = null!;
    public string ApplicationDataDirectory { get; private set; } = string.Empty;

    public override void OnCreate()
    {
        base.OnCreate();
        var filesDirectory = FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(filesDirectory))
            throw new InvalidOperationException("The Android application-data directory is unavailable.");

        ApplicationDataDirectory = Path.Combine(
            filesDirectory,
            ApplicationFileNames.AppFolderName);
        RuntimeServiceConnector = new AndroidRuntimeServiceConnector();
    }
}
