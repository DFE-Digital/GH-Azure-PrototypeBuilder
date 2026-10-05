using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PrototypeAzureDeletionPolicyTests
{
    [Fact]
    public async Task ShouldDeleteAzureResourcesAsync_ReturnsFalse_ForSeedData()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var prototype = CreatePrototype(azureSubscriptionId: "local-development", resourceGroupName: "pm-rg-test");

        context.Prototypes.Add(prototype);
        context.ProvisioningJobs.Add(SucceededCreateJob(prototype.Id));
        await context.SaveChangesAsync();

        var policy = new PrototypeAzureDeletionPolicy(context);

        Assert.False(await policy.ShouldDeleteAzureResourcesAsync(prototype));
    }

    [Fact]
    public async Task ShouldDeleteAzureResourcesAsync_ReturnsFalse_WhenCreateNeverSucceeded()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var prototype = CreatePrototype(
            azureSubscriptionId: "76c4c747-87a5-47c6-97f3-7248276192f1",
            resourceGroupName: "pm-rg-test");

        context.Prototypes.Add(prototype);
        context.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.Create,
            Status = ProvisioningJobStatus.Failed,
            AttemptCount = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var policy = new PrototypeAzureDeletionPolicy(context);

        Assert.False(await policy.ShouldDeleteAzureResourcesAsync(prototype));
    }

    [Fact]
    public async Task ShouldDeleteAzureResourcesAsync_ReturnsTrue_WhenHostingWasProvisioned()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var prototype = CreatePrototype(
            azureSubscriptionId: "76c4c747-87a5-47c6-97f3-7248276192f1",
            resourceGroupName: "pm-rg-test");

        context.Prototypes.Add(prototype);
        context.ProvisioningJobs.Add(SucceededCreateJob(prototype.Id));
        await context.SaveChangesAsync();

        var policy = new PrototypeAzureDeletionPolicy(context);

        Assert.True(await policy.ShouldDeleteAzureResourcesAsync(prototype));
    }

    private static ProvisioningJob SucceededCreateJob(Guid prototypeId) => new()
    {
        PrototypeId = prototypeId,
        Type = ProvisioningJobType.Create,
        Status = ProvisioningJobStatus.Succeeded,
        AttemptCount = 1,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    private static Prototype CreatePrototype(string azureSubscriptionId, string resourceGroupName) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test prototype",
        Slug = "test-prototype",
        OwnerEmail = "owner@education.gov.uk",
        AzureSubscriptionId = azureSubscriptionId,
        AzureResourceGroupName = resourceGroupName,
        AzureAppServicePlanName = "pm-asp-test",
        AzureWebAppName = "pm-app-test",
        Status = PrototypeStatus.Live,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "owner@education.gov.uk",
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
    };

    private static PrototypeManagerDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<PrototypeManagerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new PrototypeManagerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
