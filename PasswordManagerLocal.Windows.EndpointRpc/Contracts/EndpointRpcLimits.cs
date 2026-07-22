namespace PasswordManagerLocal.Windows.EndpointRpc.Contracts;

public static class EndpointRpcLimits
{
    public const int MaximumRequestPayloadSize = 512 * 1024;
    public const int MaximumResponsePayloadSize = 1536 * 1024;
    public const int MaximumSmallRequestPayloadSize = 64 * 1024;
    public const int MaximumBulkRequestPayloadSize = MaximumRequestPayloadSize;
    public const int MaximumSmallResponsePayloadSize = 128 * 1024;
    public const int MaximumCollectionResponsePayloadSize = 768 * 1024;
    public const int MaximumCollectionItems = 1000;
    public const int MaximumSensitiveBinaryFieldSize = 300;
    public const int MaximumSafeErrorMessageLength = 512;
    public const int MaximumErrorPayloadSize = 4 * 1024;
    public const int MaximumEnrollmentCodeLength = 4096;
    public const int MaximumFingerprintLength = 256;
    public const int MaximumTimeZoneIdLength = 256;
}
