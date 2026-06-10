using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace PasswordManagerLocalBackend.Persistence;

internal static class AppDatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        await EnsureLocalDeviceIdentitySchemaAsync(db, ct);
        await EnsureDeviceSchemaAsync(db, ct);
    }


    private static async Task EnsureLocalDeviceIdentitySchemaAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
            await connection.OpenAsync(ct);

        try
        {
            if (!await TableExistsAsync(connection, "LocalDeviceIdentity", ct))
            {
                await ExecuteNonQueryAsync(connection, """
                    CREATE TABLE "LocalDeviceIdentity" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_LocalDeviceIdentity" PRIMARY KEY,
                        "AgreementPrivateKeyBlob" BLOB NOT NULL,
                        "SignPrivateKeyBlob" BLOB NOT NULL,
                        "PFXCertificate" BLOB NOT NULL,
                        "DeviceName" TEXT NOT NULL DEFAULT '',
                        "IsSyncOn" INTEGER NOT NULL DEFAULT 1,
                        "CreatedAt" TEXT NOT NULL,
                        "IntegrityHash" BLOB NOT NULL,
                        "SingletonKey" INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT "CK_LocalDeviceIdentity_SingletonKey" CHECK ("SingletonKey" = 1)
                    );
                    """, ct);

                await ExecuteNonQueryAsync(connection, """
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_LocalDeviceIdentity_SingletonKey"
                    ON "LocalDeviceIdentity" ("SingletonKey");
                    """, ct);

                return;
            }

            var columns = await GetColumnNamesAsync(connection, "LocalDeviceIdentity", ct);

            if (!columns.Contains("DeviceName"))
            {
                await ExecuteNonQueryAsync(connection, """
                    ALTER TABLE "LocalDeviceIdentity"
                    ADD COLUMN "DeviceName" TEXT NOT NULL DEFAULT '';
                    """, ct);
            }

            if (!columns.Contains("IsSyncOn"))
            {
                await ExecuteNonQueryAsync(connection, """
                    ALTER TABLE "LocalDeviceIdentity"
                    ADD COLUMN "IsSyncOn" INTEGER NOT NULL DEFAULT 1;
                    """, ct);
            }

            if (!columns.Contains("SingletonKey"))
            {
                await ExecuteNonQueryAsync(connection, """
                    ALTER TABLE "LocalDeviceIdentity"
                    ADD COLUMN "SingletonKey" INTEGER NOT NULL DEFAULT 1;
                    """, ct);
            }

            await ExecuteNonQueryAsync(connection, """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_LocalDeviceIdentity_SingletonKey"
                ON "LocalDeviceIdentity" ("SingletonKey");
                """, ct);
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }


    private static async Task EnsureDeviceSchemaAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
            await connection.OpenAsync(ct);

        try
        {
            if (!await TableExistsAsync(connection, "Devices", ct))
                return;

            var columns = await GetColumnNamesAsync(connection, "Devices", ct);
            if (!columns.Contains("DeviceName"))
            {
                await ExecuteNonQueryAsync(connection, """
                    ALTER TABLE "Devices"
                    ADD COLUMN "DeviceName" TEXT NOT NULL DEFAULT '';
                    """, ct);
            }
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }


    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName;
            """;
        command.Parameters.Add(new SqliteParameter("$tableName", tableName));

        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) > 0;
    }


    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            columns.Add(reader.GetString(1));

        return columns;
    }


    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }


    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
