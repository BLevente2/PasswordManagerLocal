namespace PasswordManagerLocalBackend.Models;

public enum SyncChangeType : byte
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}