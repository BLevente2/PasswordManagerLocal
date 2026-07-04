using PasswordManagerLocal.Backend.Exceptions;

namespace PasswordManagerLocal.Backend.Responses;

public enum DeviceEnrollmentState
{
    None,
    Waiting,
    Completed,
    Failed,
    Expired
}
