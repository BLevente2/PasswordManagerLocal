using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class RecoveryTestVersionClock : ISyncVersionClockService
{
    public SyncVersionStamp Next() => throw new InvalidOperationException("Recovery must not create new deterministic item versions.");

    public void Observe(IEnumerable<SyncVersionStamp> stamps)
    {
        foreach (var stamp in stamps)
            SyncVersionStampComparer.Validate(stamp);
    }
}
