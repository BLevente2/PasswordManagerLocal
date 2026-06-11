using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using System.Net.NetworkInformation;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class SyncNetworkRefreshHostedService : IHostedService, IDisposable
{
    private readonly IDeviceIdentityService _identity;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceSyncTaskService _deviceSyncTasks;
    private readonly MdnsPublisherHostedService _publisher;
    private readonly MdnsBrowserHostedService _browser;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCancellation;
    private bool _started;

    public SyncNetworkRefreshHostedService(
        IDeviceIdentityService identity,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceSyncTaskService deviceSyncTasks,
        MdnsPublisherHostedService publisher,
        MdnsBrowserHostedService browser)
    {
        _identity = identity;
        _endpointCache = endpointCache;
        _deviceSyncTasks = deviceSyncTasks;
        _publisher = publisher;
        _browser = browser;
    }




    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
            return Task.CompletedTask;

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _started = true;

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

        if (debounceCancellation is not null)
        {
            debounceCancellation.Cancel();
            debounceCancellation.Dispose();
        }

        return Task.CompletedTask;
    }


    public void Dispose()
    {
        CancellationTokenSource? debounceCancellation;
        lock (_lock)
        {
            debounceCancellation = _debounceCancellation;
            _debounceCancellation = null;
        }

        if (debounceCancellation is not null)
        {
            debounceCancellation.Cancel();
            debounceCancellation.Dispose();
        }
    }


    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        ScheduleRefresh();


    private void OnNetworkChanged(object? sender, EventArgs e) =>
        ScheduleRefresh();


    private void ScheduleRefresh()
    {
        if (!_identity.IsSyncOn)
            return;

        CancellationTokenSource debounceCancellation;
        lock (_lock)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = new CancellationTokenSource();
            debounceCancellation = _debounceCancellation;
        }

        _ = Task.Run(() => RefreshAfterDebounceAsync(debounceCancellation));
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

            if (!_identity.IsSyncOn)
                return;

            await RefreshAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            debounceCancellation.Dispose();
        }
    }


    private async Task RefreshAsync(CancellationToken ct)
    {
        await _deviceSyncTasks.StopAllAsync(ct);
        _endpointCache.Clear();

        await _browser.StopAsync(ct);
        await _publisher.StopAsync(ct);

        if (!_identity.IsSyncOn)
            return;

        await _publisher.StartAsync(ct);
        await _browser.StartAsync(ct);
    }
}
