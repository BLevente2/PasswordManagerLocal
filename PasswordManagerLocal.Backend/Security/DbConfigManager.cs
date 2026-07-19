using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Exceptions;
using System.Buffers.Binary;
using System.Security.Cryptography;
using static PasswordManagerLocal.Backend.Constants.DatabaseConstants;
using static PasswordManagerLocal.Backend.Constants.ApplicationFileNames;
using static PasswordManagerLocal.Backend.Hosting.ApplicationPaths;

namespace PasswordManagerLocal.Backend.Security;

internal static class DbConfigManager
{
    internal static string GetOrCreateSqlCipherPassword(IKeyProtector protector)
    {
        var path = Path.Combine(AppRootFolder, DbConfigFileName);

        if (!File.Exists(path))
            CreateDbConfig(path, protector);

        var protectedBlob = ReadAndValidateDbConfig(path);
        try
        {
            byte[] keyBytes;
            try
            {
                keyBytes = protector.Unprotect(protectedBlob);
            }
            catch (Exception exception) when (exception is CryptographicException or InvalidDataException)
            {
                throw CreateUnsupportedException(null, "The protected database key could not be opened.", exception);
            }

            try
            {
                return Convert.ToBase64String(keyBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBlob);
        }
    }

    private static void CreateDbConfig(string path, IKeyProtector protector)
    {
        var legacyPath = Path.Combine(AppRootFolder, LegacyDbKeyFileName);
        if (File.Exists(legacyPath))
        {
            byte[] legacyProtectedBlob;
            try
            {
                legacyProtectedBlob = File.ReadAllBytes(legacyPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw CreateUnsupportedException(null, "The legacy protected database key could not be read.", exception);
            }

            try
            {
                WriteDbConfig(path, legacyProtectedBlob);
                try
                {
                    File.Delete(legacyPath);
                }
                catch
                {
                }

                return;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(legacyProtectedBlob);
            }
        }

        var databasePath = Path.Combine(AppRootFolder, DbFileName);
        if (File.Exists(databasePath))
            throw CreateUnsupportedException(null, "The database configuration file is missing for the existing database.");

        using var key = EncryptionKey.Create();
        var keyBytes = key.ExportCopy();
        try
        {
            var protectedBlob = protector.Protect(keyBytes);
            try
            {
                WriteDbConfig(path, protectedBlob);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBlob);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static void WriteDbConfig(string path, byte[] protectedBlob)
    {
        if (protectedBlob.Length == 0)
            throw CreateUnsupportedException(null, "The protected database key is missing.");

        var header = CreateHeader();
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(header);
                stream.Write(protectedBlob);
                stream.Flush(true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static byte[] ReadAndValidateDbConfig(string path)
    {
        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CreateUnsupportedException(null, "The database configuration file could not be read.", exception);
        }

        try
        {
            if (fileBytes.Length <= DbConfigHeaderLength)
                throw CreateUnsupportedException(null, "The database configuration header or protected key is incomplete.");

            var header = fileBytes.AsSpan(0, DbConfigHeaderLength);
            if (header[0] != DbConfigMagicFirstByte || header[1] != DbConfigMagicSecondByte)
                throw CreateUnsupportedException(null, "The database configuration header is invalid.");

            var version = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(DbConfigVersionOffset, sizeof(int)));
            var headerLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(DbConfigHeaderLengthOffset, sizeof(int)));

            if (headerLength != DbConfigHeaderLength)
                throw CreateUnsupportedException(version, "The database configuration header length is not supported.");

            if (version < OldestSupportedDbVersion || version > CurrentDbVersion)
                throw CreateUnsupportedException(version);

            return fileBytes.AsSpan(DbConfigHeaderLength).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileBytes);
        }
    }

    private static byte[] CreateHeader()
    {
        var header = new byte[DbConfigHeaderLength];
        header[0] = DbConfigMagicFirstByte;
        header[1] = DbConfigMagicSecondByte;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(DbConfigVersionOffset, sizeof(int)), CurrentDbVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(DbConfigHeaderLengthOffset, sizeof(int)), DbConfigHeaderLength);
        return header;
    }

    private static DatabaseVersionNotSupportedException CreateUnsupportedException(
        int? detectedVersion,
        string? reason = null,
        Exception? innerException = null) =>
        new(
            detectedVersion,
            OldestSupportedDbVersion,
            CurrentDbVersion,
            reason,
            innerException);
}
