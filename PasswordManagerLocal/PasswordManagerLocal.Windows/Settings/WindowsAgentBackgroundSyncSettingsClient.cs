using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Settings;

public sealed class WindowsAgentBackgroundSyncSettingsClient : IBackgroundSyncSettingsClient
{
    private const int MaximumReadBackAttempts = 2;
    private readonly IWindowsAgentControlConnection _connection;

    public WindowsAgentBackgroundSyncSettingsClient(
        IWindowsAgentControlConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<BackgroundSyncClientState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _connection.EnsureConnectedAsync(cancellationToken))
            return CreateUnavailableState();

        try
        {
            return Map(await _connection.GetBackgroundSyncStateAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateUnavailableState();
        }
    }

    public async Task<BackgroundSyncChangeResult> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _connection.EnsureConnectedAsync(cancellationToken))
            {
                return new BackgroundSyncChangeResult(
                    CreateUnavailableState(),
                    WasOutcomeUncertain: false);
            }

            var state = await _connection.SetBackgroundSyncEnabledAsync(
                isEnabled,
                cancellationToken);
            return new BackgroundSyncChangeResult(
                Map(state),
                WasOutcomeUncertain: false);
        }
        catch (WindowsAgentControlWriteException exception)
            when (exception.TransmissionState ==
                WindowsAgentControlWriteTransmissionState.DefinitelyNotSent &&
                exception.InnerException is OperationCanceledException &&
                cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WindowsAgentControlWriteException exception)
            when (exception.TransmissionState is
                WindowsAgentControlWriteTransmissionState.Sent or
                WindowsAgentControlWriteTransmissionState.TransmissionUnknown)
        {
            return await ReadBackAfterUncertainWriteAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return await ReadBackAfterUncertainWriteAsync();
        }
    }

    private async Task<BackgroundSyncChangeResult> ReadBackAfterUncertainWriteAsync()
    {
        for (var attempt = 0; attempt < MaximumReadBackAttempts; attempt++)
        {
            try
            {
                await _connection.DisconnectAsync(CancellationToken.None);
                if (!await _connection.EnsureConnectedAsync(CancellationToken.None))
                    continue;

                var state = await _connection.GetBackgroundSyncStateAsync(
                    CancellationToken.None);
                return new BackgroundSyncChangeResult(
                    Map(state),
                    WasOutcomeUncertain: true);
            }
            catch
            {
            }
        }

        return new BackgroundSyncChangeResult(
            CreateUnavailableState(),
            WasOutcomeUncertain: true);
    }

    private BackgroundSyncClientState CreateUnavailableState() => new(
        IsEnabled: false,
        IsAvailable: false,
        IsDegraded: true,
        IsTransitionInProgress: false,
        BackgroundSyncClientFailureKind.Unavailable,
        SafeMessage: null);

    private static BackgroundSyncClientState Map(WindowsBackgroundSyncStateDto state) => new(
        state.IsEnabled,
        IsAvailable: state.Consistency != WindowsBackgroundSyncConsistency.Unavailable,
        IsDegraded: state.Consistency is WindowsBackgroundSyncConsistency.Degraded or
            WindowsBackgroundSyncConsistency.Inconsistent or
            WindowsBackgroundSyncConsistency.Unavailable,
        state.IsTransitionInProgress,
        MapFailureKind(state.FailureKind),
        state.Failure?.SafeMessage);

    private static BackgroundSyncClientFailureKind MapFailureKind(
        WindowsBackgroundSyncFailureKind failureKind) => failureKind switch
        {
            WindowsBackgroundSyncFailureKind.None => BackgroundSyncClientFailureKind.None,
            WindowsBackgroundSyncFailureKind.SettingRead or
                WindowsBackgroundSyncFailureKind.SettingPersistence =>
                BackgroundSyncClientFailureKind.SettingPersistence,
            WindowsBackgroundSyncFailureKind.StartupRegistration =>
                BackgroundSyncClientFailureKind.StartupRegistration,
            WindowsBackgroundSyncFailureKind.RuntimeLease =>
                BackgroundSyncClientFailureKind.Runtime,
            WindowsBackgroundSyncFailureKind.Rollback =>
                BackgroundSyncClientFailureKind.Rollback,
            _ => BackgroundSyncClientFailureKind.Unavailable
        };
}
