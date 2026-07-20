using Android.Content;
using PasswordManagerLocal.Backend.Android.Security;
using PasswordManagerLocal.Backend.Configuration;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Backend.Android;

public static class AndroidBackendRuntimeFactory
{
    public static BackendRuntimeComposition Create(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext
            ?? throw new InvalidOperationException("The Android application context is unavailable.");
        var filesDirectory = applicationContext.FilesDir?.AbsolutePath;

        if (string.IsNullOrWhiteSpace(filesDirectory))
            throw new InvalidOperationException("The Android application-data directory is unavailable.");

        var storagePaths = new BackendStoragePaths(
            Path.Combine(filesDirectory, ApplicationFileNames.AppFolderName));
        var runtime = BackendRuntimeFactory.Create(
            storagePaths,
            static () => new AndroidKeyProtector(),
            () => new AndroidLocalDiscoveryNetworkLease(applicationContext));

        return new BackendRuntimeComposition(
            runtime,
            new BackendRuntimeLifetimeCoordinator(runtime),
            storagePaths.RootDirectory);
    }
}
