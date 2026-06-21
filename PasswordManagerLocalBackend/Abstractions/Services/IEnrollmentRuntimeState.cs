namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IEnrollmentRuntimeState
{
    bool IsActive { get; }
    void Activate();
    void Deactivate();
}
