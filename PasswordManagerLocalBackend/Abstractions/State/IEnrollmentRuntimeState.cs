namespace PasswordManagerLocalBackend.Abstractions.State;

public interface IEnrollmentRuntimeState
{
    bool IsActive { get; }
    void Activate();
    void Deactivate();
}
