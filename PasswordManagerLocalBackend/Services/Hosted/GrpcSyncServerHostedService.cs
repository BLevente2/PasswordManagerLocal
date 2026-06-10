using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Services.Grpc;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class GrpcSyncServerHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly IServiceProvider _root;
    private IHost? _webHost;

    public GrpcSyncServerHostedService(IDeviceIdentityService identity, IServiceProvider root)
    {
        _identity = identity;
        _root = root;
    }




    public int StartOrder => 20;




    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_identity.IsSyncOn)
            return;

        if (_webHost is not null)
            return;

        var builder = Host.CreateDefaultBuilder(Array.Empty<string>())
            .ConfigureServices(services =>
            {
                services.AddGrpc(options =>
                {
                    options.MaxReceiveMessageSize = SyncConstants.MaxIncomingDeltaPayloadBytes + 1024;
                    options.MaxSendMessageSize = 1024 * 1024;
                });

                services.AddSingleton(new GrpcRootServiceProvider(_root));
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.ConfigureKestrel(kestrel =>
                {
                    kestrel.ListenAnyIP(SyncConstants.SyncPort, listen =>
                    {
                        listen.Protocols = HttpProtocols.Http2;
                        listen.UseHttps(_identity.Certificate, https =>
                        {
                            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                            https.AllowAnyClientCertificate();
                        });
                    });
                });

                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<SyncGrpcServiceImpl>();
                    });
                });
            })
            .Build();

        _webHost = builder;
        await _webHost.StartAsync(cancellationToken);
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webHost is null)
            return;

        await _webHost.StopAsync(cancellationToken);
        _webHost.Dispose();
        _webHost = null;
    }
}
