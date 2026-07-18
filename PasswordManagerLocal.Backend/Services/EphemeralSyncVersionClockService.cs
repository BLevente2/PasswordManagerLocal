using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>Test-only fallback for directly constructed mutation services. Production DI uses the durable clock.</summary>
internal sealed class EphemeralSyncVersionClockService : ISyncVersionClockService
{
    private static readonly object Gate = new();
    private static readonly Guid DeviceId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InstanceId = new("22222222-2222-2222-2222-222222222222");
    private static long _physical;
    private static long _logical;

    public SyncVersionStamp Next()
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now > _physical)
            {
                _physical = now;
                _logical = 0;
            }
            else
            {
                _logical++;
            }
            return new SyncVersionStamp
            {
                PhysicalTimeUnixMilliseconds = _physical,
                LogicalCounter = _logical,
                OriginDeviceId = DeviceId,
                OriginInstanceId = InstanceId
            };
        }
    }

    public void Observe(IEnumerable<SyncVersionStamp> stamps)
    {
        lock (Gate)
        {
            foreach (var stamp in stamps)
            {
                SyncVersionStampComparer.Validate(stamp);
                if (stamp.PhysicalTimeUnixMilliseconds > _physical)
                {
                    _physical = stamp.PhysicalTimeUnixMilliseconds;
                    _logical = stamp.LogicalCounter;
                }
                else if (stamp.PhysicalTimeUnixMilliseconds == _physical)
                {
                    _logical = Math.Max(_logical, stamp.LogicalCounter);
                }
            }
        }
    }
}
