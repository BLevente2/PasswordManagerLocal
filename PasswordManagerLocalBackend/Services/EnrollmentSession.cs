using Makaretu.Dns;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Services;

internal sealed class EnrollmentSession
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Secret { get; set; } = [];
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DeviceEnrollmentState State { get; set; } = DeviceEnrollmentState.Waiting;
    public string? ErrorMessage { get; set; }
    public DeviceEnrollmentErrorCode ErrorCode { get; set; } = DeviceEnrollmentErrorCode.Unknown;
    public int FailedValidationAttempts { get; set; }
    public ServiceDiscovery? Discovery { get; set; }
    public ServiceProfile? Profile { get; set; }
}
