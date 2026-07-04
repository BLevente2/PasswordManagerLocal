using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync;

public sealed class GroupSyncPayload
{
    public Guid Id { get; set; }
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> UserIds { get; set; } = [];
}
