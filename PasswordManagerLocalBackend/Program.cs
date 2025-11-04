using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants; // PathConstants
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Repositories;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using SQLitePCL;
using System.Text;

namespace PasswordManagerLocal;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // 1) Low-level init for SQLitePCLRaw (SQLCipher bundle)
        Batteries_V2.Init();

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 2) Compute DB path (under AppRootFolder)
                var dbFolder = PathConstants.AppRootFolder;
                var dbPath = Path.Combine(dbFolder, PathConstants.DbFileName);

                // 3) Register platform-specific key protector (singleton)
#if WINDOWS
                services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
#else
                services.AddSingleton<IKeyProtector>(sp =>
                {
                    var cfg = sp.GetRequiredService<IConfiguration>();
                    var passphrase =
                        cfg["Db:MasterPassphrase"] ??
                        Environment.GetEnvironmentVariable("APP_DB_MASTER_PASSPHRASE") ??
                        throw new InvalidOperationException("Provide master passphrase via config (Db:MasterPassphrase) or env (APP_DB_MASTER_PASSPHRASE).");
                    return new PassphraseKeyProtector(Encoding.UTF8.GetBytes(passphrase));
                });
#endif

                // 4) Register EF Core DbContext (derive SQLCipher password via IKeyProtector)
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





                // =====================================
                // 5) Register application services here
                // =====================================
                services.AddScoped<IUserRepository, UserRepository>();
                services.AddScoped<IAuthService, AuthService>();
                services.AddScoped<IUserService, UserService>();
                services.AddScoped<IRememberMeService, RememberMeService>();

                services.AddSingleton<SafeMemoryCache>();
                services.AddSingleton<IDataCachingService, DataCachingService>();
                services.AddSingleton<ITokenService, TokenService>();





            })
            .Build();

        // 6) Ensure database/schema exist
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // 7) Start host if you have background services
        // await host.RunAsync();
    }
}