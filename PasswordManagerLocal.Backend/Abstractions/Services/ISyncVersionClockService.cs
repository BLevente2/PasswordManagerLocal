using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncVersionClockService
{
    SyncVersionStamp Next();
    void Observe(IEnumerable<SyncVersionStamp> stamps);
}
