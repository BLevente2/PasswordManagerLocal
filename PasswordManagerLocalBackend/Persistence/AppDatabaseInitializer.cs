using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Constants;

namespace PasswordManagerLocalBackend.Persistence;

internal static class AppDatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (await CleanupSyncTombstonesAsync(db, DateTimeOffset.UtcNow, ct))
            await db.SaveChangesAsync(ct);
    }


    private static async Task<bool> CleanupSyncTombstonesAsync(AppDbContext db, DateTimeOffset utcNow, CancellationToken ct)
    {
        var tombstones = await db.SyncTombstones
            .OrderBy(tombstone => tombstone.DeletedAtTs)
            .ThenBy(tombstone => tombstone.Id)
            .ToListAsync(ct);
        if (tombstones.Count == 0)
            return false;

        var cutoffTs = utcNow
            .ToUniversalTime()
            .AddMonths(-TombstoneConstants.TombstoneRetentionMonths)
            .ToUnixTimeMilliseconds();

        var removed = tombstones
            .Where(tombstone => tombstone.DeletedAtTs < cutoffTs)
            .ToList();

        var kept = tombstones
            .Where(tombstone => tombstone.DeletedAtTs >= cutoffTs)
            .ToList();

        if (TombstoneConstants.MaxSyncTombstones < 1)
        {
            removed.AddRange(kept);
        }
        else
        {
            var overflow = kept.Count - TombstoneConstants.MaxSyncTombstones;
            if (overflow > 0)
                removed.AddRange(kept.Take(overflow));
        }

        if (removed.Count == 0)
            return false;

        db.SyncTombstones.RemoveRange(removed.DistinctBy(tombstone => tombstone.Id));
        return true;
    }
}
