using Azure;
using Azure.Core;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockAppServicePlanSkuService(
    IFreeAppServicePlanPool freePlanPool,
    ILogger<MockAppServicePlanSkuService> logger) : IAppServicePlanSkuService
{
    public Task EscalateToDedicatedPlanAsync(Prototype prototype, string sku, CancellationToken cancellationToken = default)
    {
        prototype.AzureAppServicePlanName = PrototypeResourceNaming.CreateDedicatedEscalationPlanName(prototype.Slug);
        logger.LogInformation(
            "Mock escalate {PrototypeSlug} to dedicated plan {PlanName} ({Sku}).",
            prototype.Slug,
            prototype.AzureAppServicePlanName,
            sku);
        return Task.CompletedTask;
    }

    public async Task RevertToFreePoolAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        var assignment = await freePlanPool.AllocateAsync(cancellationToken);
        prototype.AzureAppServicePlanName = assignment.PlanName;
        logger.LogInformation(
            "Mock revert {PrototypeSlug} to Free pool plan {PlanName}.",
            prototype.Slug,
            assignment.PlanName);
    }
}

internal sealed class AzureAppServicePlanSkuService(
    AzureWebAppResourceResolver resourceResolver,
    IFreeAppServicePlanPool freePlanPool,
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<AzureAppServicePlanSkuService> logger) : IAppServicePlanSkuService
{
    private readonly ProvisioningOptions _options = provisioningOptions.Value;

    public async Task EscalateToDedicatedPlanAsync(
        Prototype prototype,
        string sku,
        CancellationToken cancellationToken = default)
    {
        EnsureAzureReady(prototype);

        var normalizedSku = sku.Trim().ToUpperInvariant();
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        var prototypeRg = await resourceResolver.GetResourceGroupResourceAsync(prototype, cancellationToken);
        var dedicatedPlanName = PrototypeResourceNaming.CreateDedicatedEscalationPlanName(prototype.Slug);

        var planData = new AppServicePlanData(webSite.Data.Location)
        {
            Kind = "linux",
            IsReserved = true,
            Sku = BuildSku(normalizedSku)
        };

        foreach (var tag in PrototypeResourceTags.Build())
        {
            planData.Tags[tag.Key] = tag.Value;
        }

        planData.Tags["PlanPool"] = "dedicated-escalation";

        var planOperation = await prototypeRg.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed,
            dedicatedPlanName,
            planData,
            cancellationToken);

        await MoveWebAppAsync(webSite, planOperation.Value.Id, cancellationToken);

        var previousPlanName = prototype.AzureAppServicePlanName;
        prototype.AzureAppServicePlanName = dedicatedPlanName;

        logger.LogInformation(
            "Escalated {PrototypeSlug} from plan {PreviousPlan} to dedicated {PlanName} ({Sku}).",
            prototype.Slug,
            previousPlanName,
            dedicatedPlanName,
            normalizedSku);
    }

    public async Task RevertToFreePoolAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        EnsureAzureReady(prototype);

        var previousPlanName = prototype.AzureAppServicePlanName;
        var assignment = await freePlanPool.AllocateAsync(cancellationToken);
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);

        await MoveWebAppAsync(webSite, new ResourceIdentifier(assignment.PlanResourceId), cancellationToken);
        prototype.AzureAppServicePlanName = assignment.PlanName;

        if (!PrototypeResourceNaming.IsFreePoolPlanName(previousPlanName) &&
            !string.IsNullOrWhiteSpace(previousPlanName) &&
            !string.Equals(previousPlanName, assignment.PlanName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var prototypeRg = await resourceResolver.GetResourceGroupResourceAsync(prototype, cancellationToken);
                var dedicatedPlan = await prototypeRg.GetAppServicePlans().GetIfExistsAsync(previousPlanName, cancellationToken);
                if (dedicatedPlan.HasValue && dedicatedPlan.Value is not null)
                {
                    await dedicatedPlan.Value.DeleteAsync(WaitUntil.Completed, cancellationToken: cancellationToken);
                    logger.LogInformation(
                        "Deleted dedicated escalation plan {PlanName} after reverting {PrototypeSlug}.",
                        previousPlanName,
                        prototype.Slug);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Reverted {PrototypeSlug} to Free pool but could not delete dedicated plan {PlanName}.",
                    prototype.Slug,
                    previousPlanName);
            }
        }

        logger.LogInformation(
            "Reverted {PrototypeSlug} to Free pool plan {PlanName}.",
            prototype.Slug,
            assignment.PlanName);
    }

    private void EnsureAzureReady(Prototype prototype)
    {
        if (!_options.IsAzureMode ||
            string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Azure provisioning is not configured for this prototype.");
        }

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            throw new InvalidOperationException("Prototype Azure web app is not provisioned yet.");
        }
    }

    private static async Task MoveWebAppAsync(
        WebSiteResource webSite,
        ResourceIdentifier planId,
        CancellationToken cancellationToken)
    {
        await webSite.UpdateAsync(
            new SitePatchInfo
            {
                ServerFarmId = planId
            },
            cancellationToken);
    }

    private static AppServiceSkuDescription BuildSku(string sku)
    {
        var description = new AppServiceSkuDescription
        {
            Name = sku,
            Tier = ResolveTier(sku),
            Size = sku,
            Family = sku.Length > 0 ? sku[..1] : sku
        };

        if (sku is not ("F1" or "D1"))
        {
            description.Capacity = 1;
        }

        return description;
    }

    private static string ResolveTier(string sku) =>
        sku switch
        {
            "F1" or "D1" => "Free",
            "B1" or "B2" or "B3" => "Basic",
            "S1" or "S2" or "S3" => "Standard",
            _ => "Basic"
        };
}

internal sealed class AppServicePlanSkuService(
    MockAppServicePlanSkuService mockService,
    IServiceProvider serviceProvider,
    IOptions<ProvisioningOptions> provisioningOptions) : IAppServicePlanSkuService
{
    private readonly ProvisioningOptions _options = provisioningOptions.Value;

    // Resolve Azure implementation lazily so Mock mode never constructs ARM clients.
    private IAppServicePlanSkuService Implementation =>
        _options.IsAzureMode
            ? serviceProvider.GetRequiredService<AzureAppServicePlanSkuService>()
            : mockService;

    public Task EscalateToDedicatedPlanAsync(Prototype prototype, string sku, CancellationToken cancellationToken = default) =>
        Implementation.EscalateToDedicatedPlanAsync(prototype, sku, cancellationToken);

    public Task RevertToFreePoolAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.RevertToFreePoolAsync(prototype, cancellationToken);
}
