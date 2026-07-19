using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using NSecKey = NSec.Cryptography.Key;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class ControllableQueueWriterService : ISyncQueueWriterService
{
    private readonly ISyncQueueWriterService _inner;

    public ControllableQueueWriterService(ISyncQueueWriterService inner) => _inner = inner;

    public bool FailNextEnqueue { get; set; }

    public Task EnqueueAsync(
        SyncItem item,
        long changedAtTs,
        IReadOnlyCollection<Guid> excludedDeviceIds,
        bool touchLocalSyncState,
        bool activateTargets,
        CancellationToken ct = default)
    {
        if (FailNextEnqueue)
        {
            FailNextEnqueue = false;
            throw new InvalidOperationException("Injected recovery queue failure.");
        }

        return _inner.EnqueueAsync(
            item,
            changedAtTs,
            excludedDeviceIds,
            touchLocalSyncState,
            activateTargets,
            ct);
    }

    public Task EnqueueForDeviceAsync(
        SyncItem item,
        Guid targetDeviceId,
        CancellationToken ct = default) =>
        _inner.EnqueueForDeviceAsync(item, targetDeviceId, ct);
}
