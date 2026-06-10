using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IDeviceIdentityRepository
{
    Task<LocalDeviceIdentity?> Get(CancellationToken ct = default);
    Task Create(LocalDeviceIdentity identity, CancellationToken ct = default);
    void Update(LocalDeviceIdentity identity);
}