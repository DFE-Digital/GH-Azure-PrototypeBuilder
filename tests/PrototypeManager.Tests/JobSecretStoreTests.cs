using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class JobSecretStoreTests
{
    [Fact]
    public async Task DbContextJobSecretStore_PersistsSecretsAcrossContexts()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PrototypeManagerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupContext = new PrototypeManagerDbContext(options))
        {
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "ProvisioningJobSecrets" (
                    "Reference" TEXT NOT NULL CONSTRAINT "PK_ProvisioningJobSecrets" PRIMARY KEY,
                    "Secret" TEXT NOT NULL
                );
                """);
        }

        string reference;

        await using (var writeContext = new PrototypeManagerDbContext(options))
        {
            var store = new DbContextJobSecretStore(writeContext);
            reference = await store.SaveAsync("very-secure-password");
            await writeContext.SaveChangesAsync();
        }

        await using (var readContext = new PrototypeManagerDbContext(options))
        {
            var store = new DbContextJobSecretStore(readContext);
            var secret = await store.GetAsync(reference);

            Assert.Equal("very-secure-password", secret);
        }
    }

    [Fact]
    public void ResolveFilePath_UsesSqliteDatabaseLocation()
    {
        var path = JobSecretStorePaths.ResolveFilePath(
            "Data Source=prototype-manager.db",
            "/tmp/prototype-manager-web");

        Assert.Equal("/tmp/prototype-manager-web/prototype-manager.job-secrets.json", path);
    }

    [Fact]
    public void Resolve_UsesAbsoluteDatabasePath()
    {
        var connectionString = SqliteConnectionStringResolver.Resolve(
            "Data Source=prototype-manager.db",
            "/tmp/prototype-manager-web");

        Assert.Equal("Data Source=/tmp/prototype-manager-web/prototype-manager.db", connectionString);
    }
}
