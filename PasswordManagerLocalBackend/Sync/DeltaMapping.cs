using Google.Protobuf;

namespace PasswordManagerLocalBackend.Sync;

public static class DeltaMapping
{
    public static DeltaChunk ToProto(NetworkDelta d)
    {
        return new DeltaChunk
        {
            Entity = d.Entity,
            Payload = ByteString.CopyFrom(d.Payload ?? []),
            Ts = d.Ts,
            DeviceId = d.DeviceId ?? string.Empty,
            SignPub = ByteString.CopyFrom(d.SignPub ?? []),
            Sig = ByteString.CopyFrom(d.Sig ?? [])
        };
    }

    public static NetworkDelta FromProto(DeltaChunk p)
    {
        return new NetworkDelta
        {
            Entity = p.Entity ?? string.Empty,
            Payload = p.Payload?.ToByteArray() ?? [],
            Ts = p.Ts,
            DeviceId = p.DeviceId ?? string.Empty,
            SignPub = p.SignPub?.ToByteArray() ?? [],
            Sig = p.Sig?.ToByteArray() ?? []
        };
    }
}