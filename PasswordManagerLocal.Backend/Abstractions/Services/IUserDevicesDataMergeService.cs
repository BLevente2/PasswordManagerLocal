using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDevicesDataMergeService
{
    bool Merge(UserDevicesData local, UserDevicesData incoming);
}
