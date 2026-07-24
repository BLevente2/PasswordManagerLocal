using Android.Content;

namespace PasswordManagerLocal.Android;

public sealed class AndroidUserUnlockedReceiver : BroadcastReceiver
{
    private readonly Func<Task> _restoreAsync;

    public AndroidUserUnlockedReceiver(Func<Task> restoreAsync)
    {
        _restoreAsync = restoreAsync ?? throw new ArgumentNullException(nameof(restoreAsync));
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionUserUnlocked)
            return;

        var pendingResult = GoAsync();
        _ = CompleteAsync(pendingResult);
    }

    private async Task CompleteAsync(BroadcastReceiver.PendingResult pendingResult)
    {
        try
        {
            await _restoreAsync();
        }
        catch
        {
        }
        finally
        {
            pendingResult.Finish();
        }
    }
}
