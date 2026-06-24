namespace PasswordManagerLocal.ViewModels;

internal enum DatabaseRecoveryStage
{
    None,
    CompatibilityError,
    FinalConfirmation,
    Declined,
    ResetFailed
}
