namespace PasswordManagerLocal.Preferences;

public interface IApplicationPreferencesDiagnostics
{
    void ReportFailure(string operation, Exception exception);
}
