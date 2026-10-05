using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PrototypeAppSettingsServiceTests
{
    [Fact]
    public async Task SetSettingAsync_RejectsSystemManagedKey()
    {
        await using var context = await CreateContextAsync();
        var prototype = CreatePrototype();
        context.Prototypes.Add(prototype);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SetSettingAsync(
                prototype.Id,
                "NODE_ENV",
                "production",
                prototype.OwnerEmail,
                isAdmin: true,
                isSupport: false));
    }

    [Fact]
    public async Task SetSettingAsync_PersistsCustomKeyAndQueuesJob()
    {
        await using var context = await CreateContextAsync();
        var prototype = CreatePrototype();
        context.Prototypes.Add(prototype);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await service.SetSettingAsync(
            prototype.Id,
            "api_base_url",
            "https://example.test",
            prototype.OwnerEmail,
            isAdmin: true,
            isSupport: false);

        var setting = await context.PrototypeAppSettings.SingleAsync();
        Assert.Equal("api_base_url", setting.Key);
        Assert.Equal("https://example.test", setting.Value);

        var job = await context.ProvisioningJobs.SingleAsync();
        Assert.Equal(ProvisioningJobType.UpdateAppSettings, job.Type);
        Assert.Equal(ProvisioningJobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task GetSettingValueAsync_ReturnsStoredCustomSetting()
    {
        await using var context = await CreateContextAsync();
        var prototype = CreatePrototype();
        context.Prototypes.Add(prototype);
        context.PrototypeAppSettings.Add(new PrototypeAppSetting
        {
            PrototypeId = prototype.Id,
            Key = "API_BASE_URL",
            Value = "https://example.test",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = prototype.OwnerEmail
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var value = await service.GetSettingValueAsync(
            prototype.Id,
            "api_base_url",
            prototype.OwnerEmail,
            isAdmin: true,
            isSupport: false);

        Assert.Equal("https://example.test", value);
    }

    [Fact]
    public async Task GetSettingsAsync_ReportsPendingSyncOnSqlite()
    {
        await using var context = await CreateContextAsync();
        var prototype = CreatePrototype();
        context.Prototypes.Add(prototype);
        context.PrototypeAppSettings.Add(new PrototypeAppSetting
        {
            PrototypeId = prototype.Id,
            Key = "API_BASE_URL",
            Value = "https://example.test",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = prototype.OwnerEmail
        });
        context.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.UpdateAppSettings,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context, new ProvisioningOptions { Mode = "Azure" });

        var settings = await service.GetSettingsAsync(
            prototype.Id,
            prototype.OwnerEmail,
            isAdmin: true,
            isSupport: false);

        Assert.Contains(settings, setting =>
            setting.Key == "API_BASE_URL" && setting.IsPendingSync);
    }

    private static PrototypeAppSettingsService CreateService(
        PrototypeManagerDbContext context,
        ProvisioningOptions? provisioningOptions = null)
    {
        var options = Options.Create(provisioningOptions ?? new ProvisioningOptions { Mode = "Mock" });
        return new PrototypeAppSettingsService(
            context,
            new AuditService(context),
            new AzureWebAppSettingsSyncService(new AzureWebAppResourceResolver(options)),
            options);
    }

    private static Prototype CreatePrototype() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test prototype",
        Slug = "test-prototype",
        OwnerEmail = "owner@education.gov.uk",
        Status = PrototypeStatus.Live,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "owner@education.gov.uk",
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        AzureWebAppName = "pm-app-test",
        AzureResourceGroupName = "pm-rg-test"
    };

    private static async Task<PrototypeManagerDbContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PrototypeManagerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new PrototypeManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
