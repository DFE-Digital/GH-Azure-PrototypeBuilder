using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockFreeAppServicePlanPool : IFreeAppServicePlanPool
{
    private int _nextIndex = 1;

    public Task<AppServicePlanAssignment> AllocateAsync(CancellationToken cancellationToken = default)
    {
        var index = Math.Min(_nextIndex, PrototypeResourceNaming.MaxFreePlansPerRegion);
        _nextIndex = Math.Min(_nextIndex + 1, PrototypeResourceNaming.MaxFreePlansPerRegion + 1);
        var planName = PrototypeResourceNaming.CreateFreePoolPlanName(index);
        var rg = PrototypeResourceNaming.FreePlanPoolResourceGroupName;

        return Task.FromResult(new AppServicePlanAssignment
        {
            PlanName = planName,
            PlanResourceGroupName = rg,
            PlanResourceId = $"/subscriptions/local-development/resourceGroups/{rg}/providers/Microsoft.Web/serverfarms/{planName}"
        });
    }
}

internal sealed class AzureFreeAppServicePlanPool(
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<AzureFreeAppServicePlanPool> logger) : IFreeAppServicePlanPool
{
    private readonly ProvisioningOptions _options = provisioningOptions.Value;
    private readonly ArmClient _armClient = new(new DefaultAzureCredential());
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppServicePlanAssignment> AllocateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var subscription = GetSubscription();
            var poolRg = await EnsurePoolResourceGroupAsync(subscription, cancellationToken);
            var existingPlans = await ListFreePoolPlansAsync(poolRg, cancellationToken);

            foreach (var plan in existingPlans.OrderBy(plan => plan.Data.Name, StringComparer.OrdinalIgnoreCase))
            {
                var siteCount = plan.Data.NumberOfSites ?? 0;
                if (siteCount < PrototypeResourceNaming.FreeAppsPerPlan)
                {
                    logger.LogInformation(
                        "Allocating Free App Service plan {PlanName} ({SiteCount}/{MaxApps} apps).",
                        plan.Data.Name,
                        siteCount,
                        PrototypeResourceNaming.FreeAppsPerPlan);

                    return ToAssignment(plan);
                }
            }

            if (existingPlans.Count >= PrototypeResourceNaming.MaxFreePlansPerRegion)
            {
                throw new ValidationException(
                    $"All {PrototypeResourceNaming.MaxFreePlansPerRegion} Free App Service plans in {_options.Location} are full " +
                    $"({PrototypeResourceNaming.FreeAppsPerPlan} apps each). Delete unused prototypes before creating more, " +
                    "or escalate capacity with DesignOps.");
            }

            var nextIndex = existingPlans.Count + 1;
            for (var index = 1; index <= PrototypeResourceNaming.MaxFreePlansPerRegion; index++)
            {
                var candidateName = PrototypeResourceNaming.CreateFreePoolPlanName(index);
                if (!existingPlans.Any(plan =>
                        string.Equals(plan.Data.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
                {
                    nextIndex = index;
                    break;
                }
            }

            var created = await CreateFreePlanAsync(poolRg, nextIndex, cancellationToken);
            logger.LogInformation("Created Free App Service plan pool member {PlanName}.", created.Data.Name);
            return ToAssignment(created);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SubscriptionResource GetSubscription()
    {
        if (string.IsNullOrWhiteSpace(_options.AzureSubscriptionId))
        {
            throw new InvalidOperationException("Provisioning:AzureSubscriptionId must be configured in Azure mode.");
        }

        return _armClient.GetSubscriptionResource(
            new ResourceIdentifier($"/subscriptions/{_options.AzureSubscriptionId}"));
    }

    private async Task<ResourceGroupResource> EnsurePoolResourceGroupAsync(
        SubscriptionResource subscription,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(_options.AppServicePlanPoolResourceGroupName)
            ? PrototypeResourceNaming.FreePlanPoolResourceGroupName
            : _options.AppServicePlanPoolResourceGroupName.Trim();

        var data = new ResourceGroupData(_options.Location);
        foreach (var tag in PrototypeResourceTags.Build())
        {
            data.Tags[tag.Key] = tag.Value;
        }

        data.Tags["Purpose"] = "Shared Free App Service plans for Prototype Manager";

        var operation = await subscription.GetResourceGroups().CreateOrUpdateAsync(
            WaitUntil.Completed,
            name,
            data,
            cancellationToken);

        return operation.Value;
    }

    private static async Task<List<AppServicePlanResource>> ListFreePoolPlansAsync(
        ResourceGroupResource poolRg,
        CancellationToken cancellationToken)
    {
        var plans = new List<AppServicePlanResource>();
        await foreach (var plan in poolRg.GetAppServicePlans().GetAllAsync(cancellationToken: cancellationToken))
        {
            if (PrototypeResourceNaming.IsFreePoolPlanName(plan.Data.Name))
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    private async Task<AppServicePlanResource> CreateFreePlanAsync(
        ResourceGroupResource poolRg,
        int index,
        CancellationToken cancellationToken)
    {
        var planName = PrototypeResourceNaming.CreateFreePoolPlanName(index);
        var data = new AppServicePlanData(new AzureLocation(_options.Location))
        {
            Kind = "linux",
            IsReserved = true,
            Sku = new AppServiceSkuDescription
            {
                Name = "F1",
                Tier = "Free",
                Size = "F1",
                Family = "F",
                Capacity = 1
            }
        };

        foreach (var tag in PrototypeResourceTags.Build())
        {
            data.Tags[tag.Key] = tag.Value;
        }

        data.Tags["PlanPool"] = "free";

        var operation = await poolRg.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed,
            planName,
            data,
            cancellationToken);

        return operation.Value;
    }

    private AppServicePlanAssignment ToAssignment(AppServicePlanResource plan) =>
        new()
        {
            PlanName = plan.Data.Name,
            PlanResourceGroupName = plan.Id.ResourceGroupName
                ?? (string.IsNullOrWhiteSpace(_options.AppServicePlanPoolResourceGroupName)
                    ? PrototypeResourceNaming.FreePlanPoolResourceGroupName
                    : _options.AppServicePlanPoolResourceGroupName),
            PlanResourceId = plan.Id.ToString()
        };
}

internal sealed class FreeAppServicePlanPool(
    MockFreeAppServicePlanPool mockPool,
    IServiceProvider serviceProvider,
    IOptions<ProvisioningOptions> provisioningOptions) : IFreeAppServicePlanPool
{
    private readonly ProvisioningOptions _options = provisioningOptions.Value;

    // Resolve Azure implementation lazily so Mock mode never constructs DefaultAzureCredential.
    private IFreeAppServicePlanPool Implementation =>
        _options.IsAzureMode
            ? serviceProvider.GetRequiredService<AzureFreeAppServicePlanPool>()
            : mockPool;

    public Task<AppServicePlanAssignment> AllocateAsync(CancellationToken cancellationToken = default) =>
        Implementation.AllocateAsync(cancellationToken);
}
