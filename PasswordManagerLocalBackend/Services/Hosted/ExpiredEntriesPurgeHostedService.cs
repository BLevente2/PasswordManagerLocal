using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using static PasswordManagerLocalBackend.Constants.EntryExpirationConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class ExpiredEntriesPurgeHostedService : BackgroundService
{
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;

    public ExpiredEntriesPurgeHostedService(ITokenService tokens, IKeyVaultService keys)
    {
        _tokens = tokens;
        _keys = keys;
    }



    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PurgeExpiredPeriod);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _tokens.PurgeExpired();
            _keys.PurgeExpired();
        }
    }
}