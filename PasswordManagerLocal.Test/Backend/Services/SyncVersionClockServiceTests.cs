using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class SyncVersionClockServiceTests
{
    [TestMethod]
    public async Task Next_SameMillisecondConcurrentAndRestart_RemainsUniqueAndMonotonic()
    {
        await using var fixture = await ClockFixture.CreateAsync(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
        var first = fixture.Clock.Next();
        var generated = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(fixture.Clock.Next)));
        var restarted = new SyncVersionClockService(fixture.ScopeFactory, fixture.Identity, fixture.Time);
        var afterRestart = restarted.Next();

        MSTestAssert.AreEqual(33, new[] { first }.Concat(generated).Select(Format).Distinct().Count());
        MSTestAssert.IsTrue(generated.All(stamp => stamp.PhysicalTimeUnixMilliseconds == first.PhysicalTimeUnixMilliseconds));
        MSTestAssert.IsTrue(generated.Max(stamp => stamp.LogicalCounter) > first.LogicalCounter);
        MSTestAssert.IsTrue(afterRestart.LogicalCounter > generated.Max(stamp => stamp.LogicalCounter));
    }

    [TestMethod]
    public async Task Next_WhenClockMovesBack_DoesNotDecrease()
    {
        await using var fixture = await ClockFixture.CreateAsync(DateTimeOffset.FromUnixTimeMilliseconds(20_000));
        var first = fixture.Clock.Next();
        fixture.Time.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_000);
        var second = fixture.Clock.Next();

        MSTestAssert.AreEqual(first.PhysicalTimeUnixMilliseconds, second.PhysicalTimeUnixMilliseconds);
        MSTestAssert.IsTrue(second.LogicalCounter > first.LogicalCounter);
    }

    [TestMethod]
    public async Task Observe_RemoteMaximum_AdvancesSubsequentLocalMutation()
    {
        await using var fixture = await ClockFixture.CreateAsync(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
        fixture.Clock.Observe([
            new()
            {
                PhysicalTimeUnixMilliseconds = 50_000,
                LogicalCounter = 7,
                OriginDeviceId = Guid.NewGuid(),
                OriginInstanceId = Guid.NewGuid()
            }
        ]);

        var next = fixture.Clock.Next();
        MSTestAssert.AreEqual(50_000, next.PhysicalTimeUnixMilliseconds);
        MSTestAssert.AreEqual(8, next.LogicalCounter);
    }

    private static string Format(PasswordManagerLocal.Backend.Models.Encrypted.SyncVersionStamp stamp) =>
        $"{stamp.PhysicalTimeUnixMilliseconds}:{stamp.LogicalCounter}:{stamp.OriginDeviceId:N}:{stamp.OriginInstanceId:N}";

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ClockFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        private ClockFixture(
            SqliteConnection connection,
            ServiceProvider provider,
            FakeDeviceIdentityService identity,
            ManualTimeProvider time,
            SyncVersionClockService clock)
        {
            _connection = connection;
            _provider = provider;
            Identity = identity;
            Time = time;
            Clock = clock;
        }

        public FakeDeviceIdentityService Identity { get; }
        public ManualTimeProvider Time { get; }
        public SyncVersionClockService Clock { get; }
        public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

        public static async Task<ClockFixture> CreateAsync(DateTimeOffset utcNow)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();

            var identity = new FakeDeviceIdentityService();
            var time = new ManualTimeProvider { UtcNow = utcNow };
            var clock = new SyncVersionClockService(provider.GetRequiredService<IServiceScopeFactory>(), identity, time);
            return new ClockFixture(connection, provider, identity, time, clock);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
