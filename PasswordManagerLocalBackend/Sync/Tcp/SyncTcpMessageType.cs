namespace PasswordManagerLocalBackend.Sync.Tcp;

public enum SyncTcpMessageType : byte
{
    HelloRequest = 1,
    HelloReply = 2,
    PushDeltaStart = 10,
    DeltaChunk = 11,
    PushDeltaEnd = 12,
    Ack = 13,
    GetDeviceEnrollmentInfoRequest = 20,
    GetDeviceEnrollmentInfoReply = 21,
    CompleteDeviceEnrollmentStart = 30,
    CompleteDeviceEnrollmentChunk = 31,
    CompleteDeviceEnrollmentEnd = 32,
    CompleteDeviceEnrollmentReply = 33,
    Error = 100
}
