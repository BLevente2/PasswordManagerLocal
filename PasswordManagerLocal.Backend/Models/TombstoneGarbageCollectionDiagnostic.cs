using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Models;

public sealed record TombstoneGarbageCollectionDiagnostic(
    Guid UserId,
    TombstoneItemType ItemType,
    Guid ItemId,
    SyncVersionStamp DeletionVersion,
    TombstoneGarbageCollectionReason Reason,
    Guid? BlockingDeviceId = null,
    Guid? BlockingOriginInstanceId = null,
    long? BlockingKeyEpoch = null);
