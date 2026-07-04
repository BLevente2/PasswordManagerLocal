using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Exceptions;

namespace PasswordManagerLocal.Backend.Security;

public abstract class IntegrityCheckableBase : IIntegrityCheckable
{
    public byte[] IntegrityHash { get; set; } = [];

    public abstract byte[] CalculateIntegrityHash();

    public virtual bool IsIntegrityValid()
    {
        if (IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
            return false;

        var calculatedHash = CalculateIntegrityHash();
        return calculatedHash.Length == Hashing.SHA256HashSizeInBytes &&
               Hashing.Verify(IntegrityHash, calculatedHash);
    }

    public virtual void GenerateIntegrityHash()
    {
        var calculatedHash = CalculateIntegrityHash();
        if (calculatedHash.Length != Hashing.SHA256HashSizeInBytes)
            throw new InvalidOperationException($"Integrity hashes must be SHA-256 hashes ({Hashing.SHA256HashSizeInBytes} bytes).");

        IntegrityHash = calculatedHash;
    }

    public virtual void VerifyIntegrity()
    {
        if (!IsIntegrityValid())
            throw new InvalidDataIntegrityException(this.GetType());
    }
}