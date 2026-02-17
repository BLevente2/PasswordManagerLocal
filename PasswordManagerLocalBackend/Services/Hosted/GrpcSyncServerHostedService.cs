using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class GrpcSyncServerHostedService : IHostedService
{
    private readonly IDeviceCertificateStore _certs;
    private readonly IServiceProvider _root;
    private IHost? _webHost;

    public GrpcSyncServerHostedService(IDeviceCertificateStore certs, IServiceProvider root)
    {
        _certs = certs;
        _root = root;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var connStr = new SqliteConnectionStringBuilder
                {
                    DataSource = "app.db",
                    Password = "ErősJelszó123!"
                }.ToString();

                services.AddGrpc();
                services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(connStr));
                services.AddScoped<IDeviceService, DeviceService>();
                services.AddScoped<IIncomingDeltaApplier, IncomingDeltaApplier>();
                services.AddSingleton(_certs);
            })
            .ConfigureLogging(_ => { })
            .ConfigureWebHost(web =>
            {
                web.UseKestrel(k =>
                {
                    k.ListenAnyIP(NetworkConfig.SyncPort, listen =>
                    {
                        listen.Protocols = HttpProtocols.Http2;
                        listen.UseHttps(_certs.Certificate, https =>
                        {
                            https.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
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
        if (_webHost != null)
        {
            await _webHost.StopAsync(cancellationToken);
            _webHost.Dispose();
        }
    }
}