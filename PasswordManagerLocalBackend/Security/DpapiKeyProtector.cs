#if WINDOWS
using System.Runtime.Versioning;
using System.Security.Cryptography;
using PasswordManagerLocalBackend.Abstractions.Security;

namespace PasswordManagerLocalBackend.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    => ProtectedData.Protect(plaintext.ToArray(), null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
        => ProtectedData.Unprotect(protectedBlob.ToArray(), null, DataProtectionScope.CurrentUser);


}
#endif