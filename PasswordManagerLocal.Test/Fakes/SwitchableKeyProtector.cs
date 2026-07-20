using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class SwitchableKeyProtector : IKeyProtector
{
    private readonly TestKeyProtector _inner = new();

    public bool IsTemporarilyUnavailable { get; set; }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        ThrowIfUnavailable();
        return _inner.Protect(plaintext);
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedBlob)
    {
        ThrowIfUnavailable();
        return _inner.Unprotect(protectedBlob);
    }

    private void ThrowIfUnavailable()
    {
        if (IsTemporarilyUnavailable)
        {
            throw new KeyProtectorUnavailableException(
                KeyProtectorUnavailableReason.DeviceLocked);
        }
    }
}
