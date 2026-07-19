using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

public sealed class SyncVersionClockService : ISyncVersionClockService
{
    private const int MaxPersistenceAttempts = 8;
    private static readonly object Gate = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly TimeProvider _timeProvider;

    public SyncVersionClockService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _timeProvider = timeProvider;
    }

    public SyncVersionStamp Next()
    {
        lock (Gate)
        {
            EnsureIdentityInitialized();
            for (var attempt = 0; attempt < MaxPersistenceAttempts; attempt++)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var state = GetOrCreateState(db);
                var now = _timeProvider.GetUtcNow();
                var nowMilliseconds = now.ToUnixTimeMilliseconds();

                if (nowMilliseconds > state.LastPhysicalTimeUnixMilliseconds)
                {
                    state.LastPhysicalTimeUnixMilliseconds = nowMilliseconds;
                    state.LastLogicalCounter = 0;
                }
                else
                {
                    checked { state.LastLogicalCounter++; }
                }

                state.LastUpdatedAtUtc = now;
                checked { state.Version++; }
                try
                {
                    db.SaveChanges();
                    return new SyncVersionStamp
                    {
                        PhysicalTimeUnixMilliseconds = state.LastPhysicalTimeUnixMilliseconds,
                        LogicalCounter = state.LastLogicalCounter,
                        OriginDeviceId = _identity.LocalDeviceId,
                        OriginInstanceId = _identity.OriginInstanceId
                    };
                }
                catch (DbUpdateConcurrencyException) when (attempt + 1 < MaxPersistenceAttempts)
                {
                }
                catch (DbUpdateException) when (attempt + 1 < MaxPersistenceAttempts)
                {
                    // A second process may have inserted the singleton row first.
                }
            }
        }

        throw new InvalidOperationException("The synchronization item clock could not be advanced atomically.");
    }

    public void Observe(IEnumerable<SyncVersionStamp> stamps)
    {
        ArgumentNullException.ThrowIfNull(stamps);
        var maximum = stamps.Aggregate<SyncVersionStamp, SyncVersionStamp?>(
            null,
            (current, stamp) =>
            {
                SyncVersionStampComparer.Validate(stamp);
                return current is null || SyncVersionStampComparer.Instance.Compare(stamp, current) > 0
                    ? stamp
                    : current;
            });
        if (maximum is null)
            return;

        lock (Gate)
        {
            for (var attempt = 0; attempt < MaxPersistenceAttempts; attempt++)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var state = GetOrCreateState(db);

                if (maximum.PhysicalTimeUnixMilliseconds > state.LastPhysicalTimeUnixMilliseconds)
                {
                    state.LastPhysicalTimeUnixMilliseconds = maximum.PhysicalTimeUnixMilliseconds;
                    state.LastLogicalCounter = maximum.LogicalCounter;
                }
                else if (maximum.PhysicalTimeUnixMilliseconds == state.LastPhysicalTimeUnixMilliseconds &&
                         maximum.LogicalCounter > state.LastLogicalCounter)
                {
                    state.LastLogicalCounter = maximum.LogicalCounter;
                }
                else
                {
                    return;
                }

                state.LastUpdatedAtUtc = _timeProvider.GetUtcNow();
                checked { state.Version++; }
                try
                {
                    db.SaveChanges();
                    return;
                }
                catch (DbUpdateConcurrencyException) when (attempt + 1 < MaxPersistenceAttempts)
                {
                }
                catch (DbUpdateException) when (attempt + 1 < MaxPersistenceAttempts)
                {
                    // A second process may have inserted the singleton row first.
                }
            }
        }

        throw new InvalidOperationException("The synchronization item clock could not observe a remote version atomically.");
    }

    private void EnsureIdentityInitialized()
    {
        if (!_identity.IsInitialized || _identity.LocalDeviceId == Guid.Empty || _identity.OriginInstanceId == Guid.Empty)
            throw new InvalidOperationException("The local device identity must be initialized before issuing item versions.");
    }

    private SyncVersionClockState GetOrCreateState(AppDbContext db)
    {
        var state = db.SyncVersionClockStates.SingleOrDefault(row => row.Id == SyncVersionClockState.SingletonId);
        if (state is not null)
            return state;

        state = new SyncVersionClockState();
        db.SyncVersionClockStates.Add(state);
        return state;
    }
}
