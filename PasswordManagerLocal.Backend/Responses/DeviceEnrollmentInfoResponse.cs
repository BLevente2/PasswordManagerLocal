using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Responses;

public sealed class DeviceEnrollmentInfoResponse
{
    public bool Ok { get; set; }
    public DeviceEnrollmentErrorCode ErrorCode { get; set; } = DeviceEnrollmentErrorCode.Unknown;
    public string? Error { get; set; }
    public Guid DeviceId { get; set; }
    public Guid OriginInstanceId { get; set; }
    public DeviceType DeviceType { get; set; }
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public byte[] SignPublicKey { get; set; } = [];
    public byte[] AgreementPublicKey { get; set; } = [];
}
