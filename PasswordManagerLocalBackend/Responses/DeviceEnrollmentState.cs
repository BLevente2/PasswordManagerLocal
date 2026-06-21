using PasswordManagerLocalBackend.Exceptions;

namespace PasswordManagerLocalBackend.Responses;

public enum DeviceEnrollmentState
{
    None,
    Waiting,
    Completed,
    Failed,
    Expired
}
