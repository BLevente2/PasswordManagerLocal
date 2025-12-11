using PasswordManagerLocalBackend.Security;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PasswordManagerLocalBackend.Utils;

internal static class DataCodec
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = false };



    internal static async Task<byte[]> SerializeCompressEncryptAsync<T>(T value, EncryptionKey key, int level = 11, byte[]? associatedData = null, int aesFrameSize = AES256.DefaultFrameSize, CancellationToken ct = default) where T : class
    {
        var producer = new ChannelStream();
        var cipherOut = new MemoryStream();

        var encTask = AES256.EncryptToStreamAsync(producer, cipherOut, key, associatedData, aesFrameSize);

        await using (var comp = await CompressionUtil.OpenWriteAsync(producer, level, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(comp, value, JsonOpts, ct);
            await comp.FlushAsync(ct);
        }

        producer.CompleteWriting();
        await encTask;

        var res = cipherOut.ToArray();
        cipherOut.Dispose();
        return res;
    }



    internal static async Task<byte[]> SerializeCompressEncryptAsync<T>(T value, EncryptionKey key, JsonTypeInfo<T> typeInfo, int level = 11, byte[]? associatedData = null, int aesFrameSize = AES256.DefaultFrameSize, CancellationToken ct = default) where T : class
    {
        var producer = new ChannelStream();
        var cipherOut = new MemoryStream();

        var encTask = AES256.EncryptToStreamAsync(producer, cipherOut, key, associatedData, aesFrameSize);

        await using (var comp = await CompressionUtil.OpenWriteAsync(producer, level, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(comp, value, typeInfo, ct);
            await comp.FlushAsync(ct);
        }

        producer.CompleteWriting();
        await encTask;

        var res = cipherOut.ToArray();
        cipherOut.Dispose();
        return res;
    }

    internal static async Task<T?> DecryptDecompressDeserializeAsync<T>(byte[] blob, EncryptionKey key, byte[]? associatedData = null, CancellationToken ct = default) where T : class
    {
        var input = new MemoryStream(blob, writable: false);
        var plainPipe = new ChannelStream();

        var decTask = AES256.DecryptToStreamAsync(input, plainPipe, key, associatedData)
            .ContinueWith(t =>
            {
                if (t.Exception != null) plainPipe.CompleteWriting(t.Exception.InnerException ?? t.Exception);
                else plainPipe.CompleteWriting();
            }, ct);

        try
        {
            await using var decStream = await CompressionUtil.OpenReadAsync(plainPipe, leaveOpen: false);
            return await JsonSerializer.DeserializeAsync<T>(decStream, JsonOpts, ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            await decTask;
        }
    }

    internal static async Task<T?> DecryptDecompressDeserializeAsync<T>(byte[] blob, EncryptionKey key, JsonTypeInfo<T> typeInfo, byte[]? associatedData = null, CancellationToken ct = default) where T : class
    {
        var input = new MemoryStream(blob, writable: false);
        var plainPipe = new ChannelStream();

        var decTask = AES256.DecryptToStreamAsync(input, plainPipe, key, associatedData)
            .ContinueWith(t =>
            {
                if (t.Exception != null) plainPipe.CompleteWriting(t.Exception.InnerException ?? t.Exception);
                else plainPipe.CompleteWriting();
            }, ct);

        try
        {
            await using var decStream = await CompressionUtil.OpenReadAsync(plainPipe, leaveOpen: false);
            return await JsonSerializer.DeserializeAsync(decStream, typeInfo, ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            await decTask;
        }
    }
}