namespace PasswordManagerLocalBackend.Requests;

public sealed class DeviceEnrollmentCodeRequest
{
    public Guid Token { get; set; }
    public string Code { get; set; } = string.Empty;
}
