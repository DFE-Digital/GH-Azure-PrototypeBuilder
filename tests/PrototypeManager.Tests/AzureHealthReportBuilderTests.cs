using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class AzureHealthReportBuilderTests
{
    [Fact]
    public void Build_IsHealthyInDevelopmentMockMode()
    {
        var report = AzureHealthReportBuilder.Build(new AzureHealthSnapshot
        {
            IsDevelopment = true,
            Mode = "Mock",
            Location = "westeurope",
            PoolResourceGroupName = PrototypeResourceNaming.FreePlanPoolResourceGroupName,
            TemplateFound = true,
            TemplatePath = "infra/compiled/prototype-hosting.json"
        });

        Assert.Equal(DependencyHealthStatus.Healthy, report.Status);
        Assert.False(report.IsAzureMode);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Build_NeedsAttentionWhenProductionIsStillInMockMode()
    {
        var report = AzureHealthReportBuilder.Build(new AzureHealthSnapshot
        {
            IsDevelopment = false,
            Mode = "Mock"
        });

        Assert.Equal(DependencyHealthStatus.Attention, report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("Mock mode", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_IsHealthyWhenTheSubscriptionAndPlanPoolHaveCapacity()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var report = AzureHealthReportBuilder.Build(AzureSnapshot(subscriptionId) with
        {
            SubscriptionReadable = true,
            SubscriptionName = "DfE Digital",
            PoolResourceGroupExists = true,
            PoolResourceGroupLocation = "westeurope",
            Plans =
            [
                new AzurePlanSnapshot(PrototypeResourceNaming.CreateFreePoolPlanName(1), 3)
            ]
        });

        Assert.Equal(DependencyHealthStatus.Healthy, report.Status);
        Assert.Equal("DfE Digital", report.SubscriptionName);
        Assert.Equal(3, report.UsedAppSlots);
        Assert.Equal(100, report.TotalAppSlots);
        Assert.Equal(97, report.RemainingAppSlots);
        Assert.Contains(subscriptionId, report.SubscriptionUrl);
        Assert.NotNull(report.PoolResourceGroupUrl);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Build_NeedsAttentionWhenTheFreePoolIsFull()
    {
        var plans = Enumerable.Range(1, PrototypeResourceNaming.MaxFreePlansPerRegion)
            .Select(index => new AzurePlanSnapshot(
                PrototypeResourceNaming.CreateFreePoolPlanName(index),
                PrototypeResourceNaming.FreeAppsPerPlan))
            .ToList();

        var report = AzureHealthReportBuilder.Build(AzureSnapshot(Guid.NewGuid().ToString()) with
        {
            SubscriptionReadable = true,
            PoolResourceGroupExists = true,
            PoolResourceGroupLocation = "westeurope",
            Plans = plans
        });

        Assert.Equal(DependencyHealthStatus.Attention, report.Status);
        Assert.Equal(0, report.RemainingAppSlots);
        Assert.Contains(report.Issues, issue => issue.Contains("no spare", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_NeedsAttentionWhenThePoolResourceGroupIsMissing()
    {
        var report = AzureHealthReportBuilder.Build(AzureSnapshot(Guid.NewGuid().ToString()) with
        {
            SubscriptionReadable = true,
            PoolResourceGroupExists = false
        });

        Assert.Equal(DependencyHealthStatus.Attention, report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains(PrototypeResourceNaming.FreePlanPoolResourceGroupName, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_IsUnavailableWhenAzureModeHasNoSubscription()
    {
        var report = AzureHealthReportBuilder.Build(new AzureHealthSnapshot
        {
            IsAzureMode = true,
            Mode = "Azure",
            Location = "westeurope",
            TemplateFound = true
        });

        Assert.Equal(DependencyHealthStatus.Unavailable, report.Status);
        Assert.Contains(report.Issues, issue => issue.Contains("subscription", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_IsUnavailableWhenAzureAuthenticationFails()
    {
        var report = AzureHealthReportBuilder.Build(AzureSnapshot(Guid.NewGuid().ToString()) with
        {
            AuthenticationError = "Azure authentication failed."
        });

        Assert.Equal(DependencyHealthStatus.Unavailable, report.Status);
        Assert.Contains("Azure authentication failed.", report.Issues);
    }

    [Fact]
    public void Combine_UsesTheWorseDependencyStatus()
    {
        Assert.Equal(
            DependencyHealthStatus.Unavailable,
            DependencyHealth.Combine(DependencyHealthStatus.Healthy, DependencyHealthStatus.Unavailable));
        Assert.Equal(
            DependencyHealthStatus.Attention,
            DependencyHealth.Combine(DependencyHealthStatus.Healthy, DependencyHealthStatus.Attention));
        Assert.Equal(
            DependencyHealthStatus.Healthy,
            DependencyHealth.Combine(DependencyHealthStatus.Healthy, DependencyHealthStatus.Healthy));
    }

    [Fact]
    public async Task GetReportAsync_DoesNotCallAzureInMockMode()
    {
        var service = new AzureHealthService(
            Options.Create(new ProvisioningOptions { Mode = "Mock", Location = "westeurope" }),
            Options.Create(new CostControlOptions()),
            new TestEnvironment(Path.GetTempPath(), isDevelopment: true),
            NullLogger<AzureHealthService>.Instance);

        var report = await service.GetReportAsync();

        Assert.Equal(DependencyHealthStatus.Healthy, report.Status);
        Assert.False(report.SubscriptionReadable);
    }

    private static AzureHealthSnapshot AzureSnapshot(string subscriptionId) => new()
    {
        IsAzureMode = true,
        Mode = "Azure",
        SubscriptionId = subscriptionId,
        Location = "westeurope",
        PoolResourceGroupName = PrototypeResourceNaming.FreePlanPoolResourceGroupName,
        TemplateFound = true,
        TemplatePath = "infra/compiled/prototype-hosting.json",
        AllowedRegions = ["westeurope"],
        AllowedSkus = ["F1"]
    };

    private sealed class TestEnvironment(string contentRoot, bool isDevelopment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = isDevelopment ? "Development" : "Production";
        public string ApplicationName { get; set; } = "PrototypeManager.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
