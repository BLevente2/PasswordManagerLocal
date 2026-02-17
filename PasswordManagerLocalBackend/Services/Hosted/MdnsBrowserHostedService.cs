using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using System.Collections.Concurrent;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsBrowserHostedService : IHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly IDeviceService _devices;
    private readonly ISyncService _sync;
    private ServiceDiscovery? _sd;
    private readonly ConcurrentDictionary<string, DateTime> _last = new();

    public MdnsBrowserHostedService(IDeviceIdentityService identity, IDeviceService devices, ISyncService sync)
    {
        _identity = identity;
        _devices = devices;
        _sync = sync;
    }




    public Task StartAsync(CancellationToken ct = default)
    {
        if (_sd is not null)
            return Task.CompletedTask;

        _sd = new ServiceDiscovery();
        _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        _sd.QueryServiceInstances(MdnsServiceType);
        return Task.CompletedTask;
    }




    public Task StopAsync(CancellationToken ct = default)
    {
        if (_sd != null)
        {
            _sd.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
            _sd.Dispose();
        }
        return Task.CompletedTask;
    }




    private async void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        try
        {
            var mdns = _sd?.Mdns;
            if (mdns == null)
                return;

            var instance = e.ServiceInstanceName;

            using var cts = new CancellationTokenSource(2000);

            var qTxt = new Message();
            qTxt.Questions.Add(new Question { Name = instance, Type = DnsType.TXT });
            var txtResp = await mdns.ResolveAsync(qTxt, cts.Token);
            var txt = txtResp.Answers.OfType<TXTRecord>().FirstOrDefault();
            if (txt == null)
                return;

            var props = txt.Strings
                .Where(s => s.Contains('='))
                .ToDictionary(s => s[..s.IndexOf('=')], s => s[(s.IndexOf('=') + 1)..]);

            if (!props.TryGetValue("signpub", out var signHex))
                return;

            if (!props.TryGetValue("tlsfp", out var tlsfp))
                return;

            var signPub = Convert.FromHexString(signHex);
            if (_identity.SignPublicKey.SequenceEqual(signPub)) return;

            var now = DateTime.UtcNow;
            var key = $"{signHex}:{tlsfp}";
            if (_last.TryGetValue(key, out var last) && (now - last).TotalSeconds < MdnsThrottleSeconds)
                return;
            _last[key] = now;

            await _devices.GetOrCreateByDiscoveryAsync(signPub, tlsfp);

            var qSrv = new Message();
            qSrv.Questions.Add(new Question { Name = instance, Type = DnsType.SRV });
            var srvResp = await mdns.ResolveAsync(qSrv, cts.Token);
            var srv = srvResp.Answers.OfType<SRVRecord>().FirstOrDefault();
            if (srv == null)
                return;

            var port = srv.Port;
            var target = srv.Target;

            var qA = new Message();
            qA.Questions.Add(new Question { Name = target, Type = DnsType.A });
            var aResp = await mdns.ResolveAsync(qA, cts.Token);
            var a = aResp.Answers.OfType<ARecord>().FirstOrDefault();

            string? host = a?.Address.ToString();
            if (host == null)
            {
                var qAAAA = new Message();
                qAAAA.Questions.Add(new Question { Name = target, Type = DnsType.AAAA });
                var aaaaResp = await mdns.ResolveAsync(qAAAA, cts.Token);
                var aaaa = aaaaResp.Answers.OfType<AAAARecord>().FirstOrDefault();
                host = aaaa?.Address.ToString();
            }
            if (host == null)
                return;

            var dev = await _devices.GetBySignPublicKeyAsync(signPub);
            if (dev == null || !dev.IsTrusted || dev.IsBlocked) return;

            await _sync.TriggerSyncToAsync(host, port, tlsfp);
        }
        catch
        {
        }
    }
}