using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Exceptions;

namespace PasswordManagerLocalBackend.Security;

public abstract class IntegrityCheckableBase : IIntegrityCheckable
{
    public byte[] IntegrityHash { get; set; } = [];

    public abstract byte[] CalculateIntegrityHash();

    public virtual bool IsIntegrityValid() =>
        Hashing.Verify(IntegrityHash, CalculateIntegrityHash());

    public virtual void GenerateIntegrityHash() =>
        IntegrityHash = CalculateIntegrityHash();

    public virtual void VerifyIntegrity()
    {
        if (!IsIntegrityValid())
            throw new InvalidDataIntegrityException(this.GetType());
    }
}