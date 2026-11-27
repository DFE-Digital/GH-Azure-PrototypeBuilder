using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class AzureWebAppResourceResolver(IOptions<ProvisioningOptions> provisioningOptions)
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;
    private readonly ArmClient _armClient = new(new DefaultAzureCredential());

    public async Task<WebSiteResource> GetWebSiteAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        var resourceGroup = await GetResourceGroupAsync(prototype, cancellationToken);

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName))
        {
            throw new InvalidOperationException("Prototype Azure web app is not provisioned yet.");
        }

        var webSite = await resourceGroup.GetWebSites().GetAsync(prototype.AzureWebAppName, cancellationToken);
        return webSite.Value;
    }

    public async Task<AppServicePlanResource> GetAppServicePlanAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prototype.AzureAppServicePlanName))
        {
            throw new InvalidOperationException("Prototype Azure App Service plan is not provisioned yet.");
        }

        // Shared Free plans live in the pool RG; dedicated escalations live in the prototype RG.
        var resourceGroup = PrototypeResourceNaming.IsFreePoolPlanName(prototype.AzureAppServicePlanName)
            ? await GetPoolResourceGroupAsync(prototype, cancellationToken)
            : await GetResourceGroupAsync(prototype, cancellationToken);

        var plan = await resourceGroup.GetAppServicePlans().GetAsync(prototype.AzureAppServicePlanName, cancellationToken);
        return plan.Value;
    }

    public Task<ResourceGroupResource> GetResourceGroupResourceAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default) =>
        GetResourceGroupAsync(prototype, cancellationToken);

    public bool IsAzureConfigured(Prototype prototype) =>
        !string.IsNullOrWhiteSpace(prototype.AzureWebAppName) &&
        !string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) &&
        _provisioningOptions.IsAzureMode &&
        !string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(_provisioningOptions.AzureSubscriptionId);

    private async Task<ResourceGroupResource> GetResourceGroupAsync(
        Prototype prototype,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            throw new InvalidOperationException("Prototype Azure resources are not provisioned yet.");
        }

        return await GetResourceGroupByNameAsync(prototype, prototype.AzureResourceGroupName, cancellationToken);
    }

    private Task<ResourceGroupResource> GetPoolResourceGroupAsync(
        Prototype prototype,
        CancellationToken cancellationToken)
    {
        var poolRg = string.IsNullOrWhiteSpace(_provisioningOptions.AppServicePlanPoolResourceGroupName)
            ? PrototypeResourceNaming.FreePlanPoolResourceGroupName
            : _provisioningOptions.AppServicePlanPoolResourceGroupName.Trim();

        return GetResourceGroupByNameAsync(prototype, poolRg, cancellationToken);
    }

    private async Task<ResourceGroupResource> GetResourceGroupByNameAsync(
        Prototype prototype,
        string resourceGroupName,
        CancellationToken cancellationToken)
    {
        var subscriptionId = ResolveSubscriptionId(prototype);
        var subscription = _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
        var resourceGroup = await subscription.GetResourceGroups().GetAsync(resourceGroupName, cancellationToken);
        return resourceGroup.Value;
    }

    private string ResolveSubscriptionId(Prototype prototype)
    {
        var subscriptionId = string.IsNullOrWhiteSpace(prototype.AzureSubscriptionId) ||
                             string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase)
            ? _provisioningOptions.AzureSubscriptionId
            : prototype.AzureSubscriptionId;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Azure subscription ID is not configured.");
        }

        return subscriptionId;
    }
}
