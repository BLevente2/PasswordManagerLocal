using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

using PasswordManagerLocal.Backend.Sync.Tombstones;

namespace PasswordManagerLocal.Backend.Internal.Tombstones;

internal sealed record AuthenticatedTombstoneReceiptLoadResult(
    IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>> Receipts,
    TombstoneGarbageCollectionReason? FailureReason);
