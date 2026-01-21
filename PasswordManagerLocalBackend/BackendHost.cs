using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Repositories;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Services.Hosted;
using SQLitePCL;

namespace PasswordManagerLocalBackend
{
    public static class BackendHost
    {
        private static IHost? _host;
        private static readonly object _lock = new();

        public static IServiceProvider Services
            => _host?.Services ?? throw new InvalidOperationException("BackendHost is not initialized.");

        public static void Initialize(IKeyProtector? platformKeyProtector = null)
        {
            if (_host != null)
                return;

            lock (_lock)
            {
                if (_host != null)
                {
                    return;
                }

                Batteries_V2.Init();

                _host = Host.CreateDefaultBuilder(Array.Empty<string>())
                    .ConfigureServices((context, services) =>
                    {
                        var dbFolder = PathConstants.AppRootFolder;
                        var dbPath = Path.Combine(dbFolder, PathConstants.DbFileName);

                        if (platformKeyProtector is not null)
                        {
                            services.AddSingleton<IKeyProtector>(platformKeyProtector);
                        }
                        else
                        {
                            services.AddSingleton<IKeyProtector>(sp =>
                            {
                                var cfg = sp.GetRequiredService<IConfiguration>();
                                var passphrase =
                                    cfg["Db:MasterPassphrase"] ??
                                    Environment.GetEnvironmentVariable("APP_DB_MASTER_PASSPHRASE") ??
                                    throw new InvalidOperationException(
                                        "Provide master passphrase via config (Db:MasterPassphrase) or env (APP_DB_MASTER_PASSPHRASE).");

                                return new PassphraseKeyProtector(System.Text.Encoding.UTF8.GetBytes(passphrase));
                            });
                        }

                        services.AddDbContext<AppDbContext>((sp, opts) =>
                        {
                            var protector = sp.GetRequiredService<IKeyProtector>();
                            var dbPassword = DbKeyManager.GetOrCreateSqlCipherPassword(protector);

                            var connStr = new SqliteConnectionStringBuilder
                            {
                                DataSource = dbPath,
                                Password = dbPassword
                            }.ToString();

                            opts.UseSqlite(connStr);
                        });

                        services.AddScoped<IEndpoints, Endpoints>();

                        services.AddScoped<IUnitOfWork, AppUnitOfWork>();

                        services.AddScoped<IUserRepository, UserRepository>();
                        services.AddScoped<IUserPasswordsService, UserPasswordsService>();
                        services.AddScoped<IPasswordService, PasswordService>();
                        services.AddScoped<IGroupService, GroupService>();
                        services.AddScoped<IGroupPasswordsService, GroupPasswordsService>();
                        services.AddScoped<IAuthService, AuthService>();
                        services.AddScoped<IUserService, UserService>();
                        services.AddScoped<IRememberMeService, RememberMeService>();
                        services.AddScoped<IUserProfileService, UserProfileService>();

                        services.AddSingleton<IKeyVaultService, KeyVaultService>();

                        services.AddMemoryCache();

                        services.AddSingleton<SafeMemoryCache>();
                        services.AddSingleton<IDataCachingService, DataCachingService>();
                        services.AddSingleton<ITokenService, TokenService>();

                        services.AddHostedService<ExpiredEntriesPurgeHostedService>();
                    })
                    .Build();

                using var scope = _host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            }
        }
    }
}