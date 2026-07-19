using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Internal.Recovery;

internal sealed class CanonicalSalvage : IDisposable
{
    public CanonicalSalvage(
        UserData root,
        GeneralUserData? general,
        UserPasswordsData? passwords,
        UserDevicesData? devices,
        UserDataBlobKind failedComponents,
        DateTimeOffset lastModifiedAt)
    {
        Root = root;
        General = general;
        Passwords = passwords;
        Devices = devices;
        FailedComponents = failedComponents;
        LastModifiedAt = lastModifiedAt;
    }

    public UserData Root { get; }
    public GeneralUserData? General { get; }
    public UserPasswordsData? Passwords { get; }
    public UserDevicesData? Devices { get; }
    public UserDataBlobKind FailedComponents { get; }
    public DateTimeOffset LastModifiedAt { get; }

    public void Dispose()
    {
        Root.Dispose();
        General?.Dispose();
        Passwords?.Dispose();
        Devices?.Dispose();
    }
}
