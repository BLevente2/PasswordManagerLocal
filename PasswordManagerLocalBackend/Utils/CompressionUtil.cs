using System.Buffers.Binary;
using System.IO.Compression;

namespace PasswordManagerLocalBackend.Utils;

internal enum CompressionKind : byte
{
    Brotli = 1,
    Gzip = 2
}

internal static class CompressionUtil
{
    private const uint Magic = 0x524D5043; // "CMPR" little-endian
    private const byte Version = 1;

    internal static async Task<Stream> CompressAsync(Stream dataStream, CompressionKind kind = CompressionKind.Brotli, int level = 5)
    {
        if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));

        var output = new MemoryStream();
        await WriteHeaderAsync(output, kind, level);
        await using (var compressor = CreateCompressor(output, kind, level, leaveOpen: true))
        {
            await dataStream.CopyToAsync(compressor);
        }
        output.Position = 0;
        return output;
    }

    internal static async Task WriteCompressedAsync(Stream input, Stream output, CompressionKind kind = CompressionKind.Brotli, int level = 5)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (output == null) throw new ArgumentNullException(nameof(output));

        await WriteHeaderAsync(output, kind, level);
        await using var compressor = CreateCompressor(output, kind, level, leaveOpen: true);
        await input.CopyToAsync(compressor);
    }

    internal static async Task<Stream> DecompressAsync(Stream compressedStream)
    {
        if (compressedStream == null) throw new ArgumentNullException(nameof(compressedStream));

        var (kind, _) = await ReadHeaderAsync(compressedStream);
        var decompressor = CreateDecompressor(compressedStream, kind, leaveOpen: false);
        return decompressor;
    }

    internal static async Task WriteDecompressedAsync(Stream compressedStream, Stream output)
    {
        if (compressedStream == null) throw new ArgumentNullException(nameof(compressedStream));
        if (output == null) throw new ArgumentNullException(nameof(output));

        var (kind, _) = await ReadHeaderAsync(compressedStream);
        await using var decompressor = CreateDecompressor(compressedStream, kind, leaveOpen: true);
        await decompressor.CopyToAsync(output);
    }

    private static async Task WriteHeaderAsync(Stream output, CompressionKind kind, int level)
    {
        var header = new byte[7];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        header[4] = Version;
        header[5] = (byte)kind;
        header[6] = (byte)Math.Clamp(level, 0, 11);
        await output.WriteAsync(header, 0, header.Length);
    }

    private static async Task<(CompressionKind kind, int level)> ReadHeaderAsync(Stream input)
    {
        var header = new byte[7];
        int read = 0;
        while (read < header.Length)
        {
            int r = await input.ReadAsync(header, read, header.Length - read);
            if (r <= 0) throw new EndOfStreamException();
            read += r;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (magic != Magic) throw new InvalidDataException("Invalid compression header.");
        if (header[4] != Version) throw new InvalidDataException("Unsupported compression version.");

        var kind = (CompressionKind)header[5];
        int level = header[6];
        return (kind, level);
    }

    private static CompressionLevel MapBrotliLevel(int q)
    {
        if (q <= 1) return CompressionLevel.Fastest;
        if (q >= 9) return CompressionLevel.SmallestSize;
        return CompressionLevel.Optimal;
    }

    private static CompressionLevel MapGzipLevel(int q)
    {
        if (q <= 1) return CompressionLevel.Fastest;
        if (q >= 9) return CompressionLevel.SmallestSize;
        return CompressionLevel.Optimal;
    }

    private static Stream CreateCompressor(Stream output, CompressionKind kind, int level, bool leaveOpen)
    {
        switch (kind)
        {
            case CompressionKind.Brotli:
                return new BrotliStream(output, MapBrotliLevel(Math.Clamp(level, 0, 11)), leaveOpen);
            case CompressionKind.Gzip:
                return new GZipStream(output, MapGzipLevel(Math.Clamp(level, 0, 9)), leaveOpen);
            default:
                throw new NotSupportedException();
        }
    }

    private static Stream CreateDecompressor(Stream input, CompressionKind kind, bool leaveOpen)
    {
        switch (kind)
        {
            case CompressionKind.Brotli:
                return new BrotliStream(input, CompressionMode.Decompress, leaveOpen);
            case CompressionKind.Gzip:
                return new GZipStream(input, CompressionMode.Decompress, leaveOpen);
            default:
                throw new NotSupportedException();
        }
    }
}