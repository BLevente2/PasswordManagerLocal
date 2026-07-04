using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Repositories;
using SQLitePCL;

namespace PasswordManagerLocal.Test.TestInfrastructure;

public sealed class SqliteIntegrationTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    static SqliteIntegrationTestDatabase() =>
        Batteries_V2.Init();

    private SqliteIntegrationTestDatabase(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
        UnitOfWork = new AppUnitOfWork(db);
        Users = new UserRepository(db);
        Groups = new GroupRepository(db);
        Devices = new DeviceRepository(db);
        UserDevices = new UserDeviceRepository(db);
        LocalUserDevices = new LocalUserDeviceRepository(db);
        SyncItems = new SyncItemRepository(db);
        SyncQueue = new SyncQueueRepository(db);
        Tombstones = new SyncTombstoneRepository(db);
    }

    public AppDbContext Db { get; }
    public AppUnitOfWork UnitOfWork { get; }
    public UserRepository Users { get; }
    public GroupRepository Groups { get; }
    public DeviceRepository Devices { get; }
    public UserDeviceRepository UserDevices { get; }
    public LocalUserDeviceRepository LocalUserDevices { get; }
    public SyncItemRepository SyncItems { get; }
    public SyncQueueRepository SyncQueue { get; }
    public SyncTombstoneRepository Tombstones { get; }

    public static async Task<SqliteIntegrationTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteIntegrationTestDatabase(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
