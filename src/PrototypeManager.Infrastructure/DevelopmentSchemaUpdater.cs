using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace PrototypeManager.Infrastructure;

internal static class DevelopmentSchemaUpdater
{
    public static async Task ApplyDevelopmentSchemaUpdatesAsync(
        this PrototypeManagerDbContext dbContext,
        IHostEnvironment environment,
        string connectionString)
    {
        if (!dbContext.Database.IsSqlite())
        {
            return;
        }

        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ProvisioningJobSecrets" (
                "Reference" TEXT NOT NULL CONSTRAINT "PK_ProvisioningJobSecrets" PRIMARY KEY,
                "Secret" TEXT NOT NULL
            );
            """);

        await ImportLegacyFileSecretsAsync(dbContext, environment, connectionString);

        if (!await ColumnExistsAsync(connection, "Prototypes", "BusinessAreaId"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Prototypes" ADD COLUMN "BusinessAreaId" TEXT NULL;""");
        }

        if (!await ColumnExistsAsync(connection, "Prototypes", "RequiresRepositoryScaffold"))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""ALTER TABLE "Prototypes" ADD COLUMN "RequiresRepositoryScaffold" INTEGER NOT NULL DEFAULT 0;""");
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BusinessAreas" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BusinessAreas" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Slug" TEXT NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BusinessAreas_Name" ON "BusinessAreas" ("Name");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BusinessAreas_Slug" ON "BusinessAreas" ("Slug");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AppUsers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AppUsers" PRIMARY KEY,
                "Email" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "IsAdmin" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_Email" ON "AppUsers" ("Email");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AppUserBusinessAreaAssignments" (
                "AppUserId" TEXT NOT NULL,
                "BusinessAreaId" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_AppUserBusinessAreaAssignments" PRIMARY KEY ("AppUserId", "BusinessAreaId")
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PrototypeContacts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PrototypeContacts" PRIMARY KEY,
                "PrototypeId" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrototypeContacts_PrototypeId_Email"
            ON "PrototypeContacts" ("PrototypeId", "Email");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PrototypeAppSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PrototypeAppSettings" PRIMARY KEY,
                "PrototypeId" TEXT NOT NULL,
                "Key" TEXT NOT NULL,
                "Value" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                "UpdatedBy" TEXT NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PrototypeAppSettings_PrototypeId_Key"
            ON "PrototypeAppSettings" ("PrototypeId", "Key");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GitHubOrganisations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GitHubOrganisations" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GitHubOrganisations_Name"
            ON "GitHubOrganisations" ("Name");
            """);
    }

    private static async Task<bool> ColumnExistsAsync(System.Data.Common.DbConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""PRAGMA table_info("{tableName}");""";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ImportLegacyFileSecretsAsync(
        PrototypeManagerDbContext dbContext,
        IHostEnvironment environment,
        string connectionString)
    {
        var legacyFilePath = JobSecretStorePaths.ResolveFilePath(connectionString, environment.ContentRootPath);

        if (!File.Exists(legacyFilePath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(legacyFilePath);
        var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        if (secrets is null || secrets.Count == 0)
        {
            File.Delete(legacyFilePath);
            return;
        }

        foreach (var (reference, secret) in secrets)
        {
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(secret))
            {
                continue;
            }

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT OR IGNORE INTO "ProvisioningJobSecrets" ("Reference", "Secret")
                VALUES ({0}, {1});
                """,
                reference,
                secret);
        }

        File.Delete(legacyFilePath);
    }
}
