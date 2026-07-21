namespace PasswordManagerLocal.Windows.Ipc.Client;

internal enum IpcRequestSubmissionState
{
    Created,
    Sending,
    Sent,
    Completed
}
