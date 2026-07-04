using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync;

public sealed record ValidatedNetworkDelta(Device SourceDevice, SyncDeltaPayload Payload);
