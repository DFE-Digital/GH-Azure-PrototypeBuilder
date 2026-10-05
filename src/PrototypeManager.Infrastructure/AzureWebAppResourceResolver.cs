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
        if (string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) ||
            string.IsNullOrWhiteSpace(prototype.AzureWebAppName))
        {
            throw new InvalidOperationException("Prototype Azure resources are not provisioned yet.");
        }

        var subscriptionId = string.IsNullOrWhiteSpace(prototype.AzureSubscriptionId)
            ? _provisioningOptions.AzureSubscriptionId
            : prototype.AzureSubscriptionId;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Azure subscription ID is not configured.");
        }

        var subscription = _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
        var resourceGroup = await subscription.GetResourceGroups().GetAsync(prototype.AzureResourceGroupName, cancellationToken);
        var webSite = await resourceGroup.Value.GetWebSites().GetAsync(prototype.AzureWebAppName, cancellationToken);
        return webSite.Value;
    }

    public bool IsAzureConfigured(Prototype prototype) =>
        !string.IsNullOrWhiteSpace(prototype.AzureWebAppName) &&
        !string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) &&
        _provisioningOptions.IsAzureMode &&
        !string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(_provisioningOptions.AzureSubscriptionId);
}
