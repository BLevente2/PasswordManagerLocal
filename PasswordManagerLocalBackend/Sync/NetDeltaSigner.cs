using PasswordManagerLocalBackend.Abstractions.Services;
using System.Buffers.Binary;
using System.Text;

namespace PasswordManagerLocalBackend.Sync;

public static class NetDeltaSigner
{
    public static byte[] CanonicalBytes(NetworkDelta d)
    {
        var entityBytes = Encoding.UTF8.GetBytes(d.Entity);
        var tsBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(tsBytes, d.Ts);

        var result = new byte[entityBytes.Length + 1 + d.Payload.Length + 1 + tsBytes.Length + Encoding.UTF8.GetByteCount(d.DeviceId) + 1 + d.SignPub.Length];
        var o = 0;

        entityBytes.CopyTo(result, o); o += entityBytes.Length;
        result[o++] = 0x00;
        d.Payload.CopyTo(result, o); o += d.Payload.Length;
        result[o++] = 0x00;
        tsBytes.CopyTo(result, o); o += tsBytes.Length;
        var deviceBytes = Encoding.UTF8.GetBytes(d.DeviceId);
        deviceBytes.CopyTo(result, o); o += deviceBytes.Length;
        result[o++] = 0x00;
        d.SignPub.CopyTo(result, o);

        return result;
    }

    public static void FillSignature(NetworkDelta d, IDeviceIdentityService identity)
    {
        var data = CanonicalBytes(d);
        var sig = identity.Sign(data);
        d.Sig = sig;
    }
}