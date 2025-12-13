namespace PasswordManagerLocalBackend.Abstractions.Security;

public interface IIntegrityCheckable
{
    byte[] IntegrityHash { get; set; }

    byte[] CalculateIntegrityHash();
    bool IsIntegrityValid();
    void GenerateIntegrityHash();
    void VerifyIntegrity();
}