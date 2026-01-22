namespace PasswordManagerLocalBackend.Models;

public enum SyncChangeType : byte
{
    None = 0,
    Created = 1,
    Updated = 2,
    Deleted = 3
}