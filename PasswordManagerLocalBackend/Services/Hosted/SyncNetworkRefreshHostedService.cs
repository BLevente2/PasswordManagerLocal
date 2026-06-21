using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Utils;
using System.Net.NetworkInformation;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class SyncNetworkRefreshHostedService : ISyncControlledHostedService, IDisposable
{
    private readonly IDeviceIdentityService _identity;
    private readonly IEnrollmentRuntimeState _enrollmentState;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly TcpSyncServerHostedService _tcpServer;
    private readonly MdnsPublisherHostedService _publisher;
    private readonly MdnsBrowserHostedService _browser;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private CancellationTokenSource? _debounceCancellation;
    private bool _started;

    public SyncNetworkRefreshHostedService(
        IDeviceIdentityService identity,
        IEnrollmentRuntimeState enrollmentState,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks,
        TcpSyncServerHostedService tcpServer,
        MdnsPublisherHostedService publisher,
        MdnsBrowserHostedService browser)
    {
        _identity = identity;
        _enrollmentState = enrollmentState;
        _endpointCache = endpointCache;
        _deviceSyncTasks = deviceSyncTasks;
        _tcpServer = tcpServer;
        _publisher = publisher;
        _browser = browser;
    }

    public int StartOrder => 50;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_started || !IsNetworkRuntimeActive())
            return Task.CompletedTask;

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _started = true;
        DeviceEnrollmentTrace.Info("Network-change monitoring started for synchronization/enrollment.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
            return Task.CompletedTask;

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _started = false;

        CancellationTokenSource? debounceCancellation;
        lock (_lock)
        {
            debounceCancellation = _debounceCancellation;
            _debounceCancellation = null;
        }

        debounceCancellation?.Cancel();

        DeviceEnrollmentTrace.Info("Network-change monitoring stopped for synchronization/enrollment.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _started = false;

        CancellationTokenSource? debounceCancellation;
        lock (_lock)
        {
            debounceCancellation = _debounceCancellation;
            _debounceCancellation = null;
        }

        debounceCancellation?.Cancel();
    }

    private bool IsNetworkRuntimeActive() =>
        _identity.IsSyncOn || _enrollmentState.IsActive;

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        ScheduleRefresh();

    private void OnNetworkChanged(object? sender, EventArgs e) =>
        ScheduleRefresh();

    private void ScheduleRefresh()
    {
        if (!IsNetworkRuntimeActive())
            return;

        CancellationTokenSource? previousCancellation;
        CancellationTokenSource debounceCancellation;
        lock (_lock)
        {
            previousCancellation = _debounceCancellation;
            _debounceCancellation = new CancellationTokenSource();
            debounceCancellation = _debounceCancellation;
        }

        previousCancellation?.Cancel();
        _ = Task.Run(() => RefreshAfterDebounceAsync(debounceCancellation), CancellationToken.None);
    }

    private async Task RefreshAfterDebounceAsync(CancellationTokenSource debounceCancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(NetworkRefreshDebounceSeconds), debounceCancellation.Token);

            lock (_lock)
            {
                if (!ReferenceEquals(_debounceCancellation, debounceCancellation))
                    return;

                _debounceCancellation = null;
            }

            if (!IsNetworkRuntimeActive())
                return;

            await RefreshAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Network refresh failed: {ex.Message}", ex);
        }
        finally
        {
            debounceCancellation.Dispose();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!IsNetworkRuntimeActive())
                return;

            DeviceEnrollmentTrace.Info("Network configuration changed. Restarting local synchronization/enrollment networking.");

            await _deviceSyncTasks.StopAllAsync(ct);
            _endpointCache.Clear();

            await _browser.StopAsync(ct);
            await _publisher.StopAsync(ct);
            await _tcpServer.StopAsync(ct);

            if (!IsNetworkRuntimeActive())
                return;

            await _tcpServer.StartAsync(ct);

            if (_identity.IsSyncOn)
            {
                await _publisher.StartAsync(ct);
                await _browser.StartAsync(ct);
            }

            DeviceEnrollmentTrace.Info("Local synchronization/enrollment networking was refreshed after the network change.");
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
