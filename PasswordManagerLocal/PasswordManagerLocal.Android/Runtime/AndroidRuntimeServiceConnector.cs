using Android.Content;

namespace PasswordManagerLocal.Android;

public sealed class AndroidRuntimeServiceConnector
{
    private static readonly TimeSpan ServiceBindTimeout = TimeSpan.FromSeconds(15);
    public async Task<AndroidActivityServiceAttachment> AttachInteractiveClientAsync(
        Context context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bindingContext = context.ApplicationContext ?? context;
        var connection = new AndroidRuntimeServiceConnection();
        var intent = new Intent(bindingContext, typeof(PasswordManagerBackgroundService));
        if (!bindingContext.BindService(intent, connection, Bind.AutoCreate))
        {
            connection.Dispose();
            throw new InvalidOperationException("The Android runtime service could not be bound.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ServiceBindTimeout);

        try
        {
            PasswordManagerBackgroundService service;
            try
            {
                service = await connection.WaitForServiceAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Android runtime service did not bind in time.");
            }
            var backendClient = await service.AttachInteractiveClientAsync(cancellationToken);
            return new AndroidActivityServiceAttachment(
                bindingContext,
                connection,
                service,
                backendClient);
        }
        catch
        {
            try
            {
                bindingContext.UnbindService(connection);
            }
            catch (Java.Lang.IllegalArgumentException)
            {
            }

            connection.Dispose();
            throw;
        }
    }
}
