using Android.App;
using Android.Content;
using Android.OS;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Android;

[BroadcastReceiver(
    Name = "com.levibaba.passwordmanagerlocal.AndroidBackgroundRestorationReceiver",
    Enabled = true,
    Exported = false)]
[IntentFilter(new[]
{
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced
})]
public sealed class AndroidBackgroundRestorationReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !IsSupportedAction(intent?.Action))
            return;

        var pendingResult = GoAsync();
        _ = RestoreAsync(context.ApplicationContext ?? context, pendingResult);
    }

    private static async Task RestoreAsync(
        Context context,
        BroadcastReceiver.PendingResult pendingResult)
    {
        try
        {
            var userManager = context.GetSystemService(Context.UserService) as UserManager;
            if (userManager?.IsUserUnlocked == false)
                return;

            var filesDirectory = context.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(filesDirectory))
                return;

            var store = new FileBackgroundSyncSettingsStore(Path.Combine(
                filesDirectory,
                ApplicationFileNames.AppFolderName));
            var settings = await store.ReadAsync();
            if (!settings.IsEnabled)
                return;

            var serviceIntent = new Intent(context, typeof(PasswordManagerBackgroundService));
            context.StartForegroundService(serviceIntent);
        }
        catch
        {
        }
        finally
        {
            pendingResult.Finish();
        }
    }

    private static bool IsSupportedAction(string? action) =>
        action is Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced;
}
