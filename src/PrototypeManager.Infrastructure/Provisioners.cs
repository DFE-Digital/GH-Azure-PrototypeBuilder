using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockPrototypeProvisioner(
    IOptions<ProvisioningOptions> provisioningOptions,
    IOptions<CostControlOptions> costControlOptions,
    IAuditService auditService,
    ILogger<MockPrototypeProvisioner> logger) : IPrototypeProvisioner
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;
    private readonly CostControlOptions _costControlOptions = costControlOptions.Value;

    public async Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default)
    {
        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            "Mock mode: generating resource names only (no Azure resources will be created).");

        ApplyResourceNames(prototype);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Assigned resource group {prototype.AzureResourceGroupName}, app service plan {prototype.AzureAppServicePlanName}, web app {prototype.AzureWebAppName}.");

        prototype.PrototypeUrl = $"https://{prototype.AzureWebAppName}.azurewebsites.net";

        return new ProvisioningResult
        {
            ResourceGroupName = prototype.AzureResourceGroupName,
            AppServicePlanName = prototype.AzureAppServicePlanName,
            WebAppName = prototype.AzureWebAppName,
            PrototypeUrl = prototype.PrototypeUrl
        };
    }

    public Task<ProvisioningResult> UpdatePasswordAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProvisioningResult
        {
            ResourceGroupName = prototype.AzureResourceGroupName,
            AppServicePlanName = prototype.AzureAppServicePlanName,
            WebAppName = prototype.AzureWebAppName,
            PrototypeUrl = prototype.PrototypeUrl
        });

    public Task DeletePrototypeAsync(Prototype prototype, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private void ApplyResourceNames(Prototype prototype)
    {
        prototype.Region = string.IsNullOrWhiteSpace(prototype.Region) ? _provisioningOptions.Location : prototype.Region;
        prototype.Sku = string.IsNullOrWhiteSpace(prototype.Sku) ? _costControlOptions.AllowedSkus.First() : prototype.Sku;
        prototype.AzureResourceGroupName = string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName)
            ? PrototypeResourceNaming.CreateResourceGroupName(prototype.Slug)
            : prototype.AzureResourceGroupName;
        prototype.AzureAppServicePlanName = string.IsNullOrWhiteSpace(prototype.AzureAppServicePlanName)
            ? PrototypeResourceNaming.CreateAppServicePlanName(prototype.Slug)
            : prototype.AzureAppServicePlanName;
        prototype.AzureWebAppName = string.IsNullOrWhiteSpace(prototype.AzureWebAppName)
            ? PrototypeResourceNaming.CreateWebAppName(prototype.Slug)
            : prototype.AzureWebAppName;
    }
}

internal sealed class AzureBicepPrototypeProvisioner(
    IOptions<ProvisioningOptions> provisioningOptions,
    IOptions<CostControlOptions> costControlOptions,
    IHostEnvironment environment,
    ICostControlPolicy costControlPolicy,
    IAuditService auditService,
    IPrototypeAzureDeletionPolicy deletionPolicy,
    ILogger<AzureBicepPrototypeProvisioner> logger) : IPrototypeProvisioner
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;
    private readonly CostControlOptions _costControlOptions = costControlOptions.Value;
    private readonly ArmClient _armClient = new(new DefaultAzureCredential());

    public async Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default)
    {
        EnsureNames(prototype);
        await costControlPolicy.ValidatePrototypeAsync(prototype);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Azure mode: preparing resources in {_provisioningOptions.Location} (subscription {_provisioningOptions.AzureSubscriptionId}).");

        var subscription = GetSubscription();
        var resourceGroup = await CreateOrUpdateResourceGroupAsync(subscription, prototype, cancellationToken);
        await DeployTemplateAsync(resourceGroup, prototype, prototypePassword, cancellationToken);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Provisioning complete. Web app available at https://{prototype.AzureWebAppName}.azurewebsites.net.");

        return new ProvisioningResult
        {
            ResourceGroupName = prototype.AzureResourceGroupName,
            AppServicePlanName = prototype.AzureAppServicePlanName,
            WebAppName = prototype.AzureWebAppName,
            PrototypeUrl = $"https://{prototype.AzureWebAppName}.azurewebsites.net"
        };
    }

    public async Task<ProvisioningResult> UpdatePasswordAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default)
    {
        var subscription = GetSubscription();
        var resourceGroup = await GetResourceGroupAsync(subscription, prototype.AzureResourceGroupName, cancellationToken);
        await DeployTemplateAsync(resourceGroup, prototype, prototypePassword, cancellationToken);

        return new ProvisioningResult
        {
            ResourceGroupName = prototype.AzureResourceGroupName,
            AppServicePlanName = prototype.AzureAppServicePlanName,
            WebAppName = prototype.AzureWebAppName,
            PrototypeUrl = prototype.PrototypeUrl
        };
    }

    public async Task DeletePrototypeAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        if (!await deletionPolicy.ShouldDeleteAzureResourcesAsync(prototype, cancellationToken))
        {
            await ProvisioningActivityRecorder.RecordStepAsync(
                auditService,
                logger,
                prototype.Id,
                prototype.Slug,
                "No Azure resources to delete for this prototype.");
            return;
        }

        var subscription = GetSubscription();
        var resourceGroup = await subscription.GetResourceGroups().GetIfExistsAsync(prototype.AzureResourceGroupName, cancellationToken);

        var resourceGroupValue = resourceGroup.Value;

        if (resourceGroupValue is not null)
        {
            await resourceGroupValue.DeleteAsync(WaitUntil.Completed);
        }
    }

    private SubscriptionResource GetSubscription()
    {
        var subscriptionId = string.IsNullOrWhiteSpace(_provisioningOptions.AzureSubscriptionId)
            ? throw new InvalidOperationException("Provisioning:AzureSubscriptionId must be configured in Azure mode.")
            : _provisioningOptions.AzureSubscriptionId;

        return _armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
    }

    private async Task<ResourceGroupResource> CreateOrUpdateResourceGroupAsync(
        SubscriptionResource subscription,
        Prototype prototype,
        CancellationToken cancellationToken)
    {
        var collection = subscription.GetResourceGroups();
        var data = new ResourceGroupData(_provisioningOptions.Location);

        foreach (var tag in PrototypeResourceTags.Build())
        {
            data.Tags[tag.Key] = tag.Value;
        }

        var operation = await collection.CreateOrUpdateAsync(
            WaitUntil.Completed,
            prototype.AzureResourceGroupName,
            data,
            cancellationToken);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Created resource group {prototype.AzureResourceGroupName} in {_provisioningOptions.Location}.");

        return operation.Value;
    }

    private async Task<ResourceGroupResource> GetResourceGroupAsync(
        SubscriptionResource subscription,
        string resourceGroupName,
        CancellationToken cancellationToken)
    {
        var resourceGroup = await subscription.GetResourceGroups().GetAsync(resourceGroupName, cancellationToken);
        return resourceGroup.Value;
    }

    private async Task DeployTemplateAsync(
        ResourceGroupResource resourceGroup,
        Prototype prototype,
        string prototypePassword,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(environment.ContentRootPath, _provisioningOptions.CompiledTemplatePath);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Compiled ARM template not found.", templatePath);
        }

        var templateJson = await File.ReadAllTextAsync(templatePath, cancellationToken);
        var parameters = new
        {
            location = new { value = _provisioningOptions.Location },
            appServicePlanName = new { value = prototype.AzureAppServicePlanName },
            webAppName = new { value = prototype.AzureWebAppName },
            nodeRuntime = new { value = _costControlOptions.AllowedRuntime },
            prototypePassword = new { value = prototypePassword },
            tags = new { value = PrototypeResourceTags.Build() }
        };

        var deploymentName = $"prototype-{prototype.Id:N}"[..18];
        var properties = new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
        {
            Template = BinaryData.FromString(templateJson),
            Parameters = BinaryData.FromString(JsonSerializer.Serialize(parameters))
        };

        var deployment = new ArmDeploymentContent(properties);
        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Deploying ARM template {deploymentName} (app service plan {prototype.AzureAppServicePlanName}, web app {prototype.AzureWebAppName}).");

        await resourceGroup.GetArmDeployments().CreateOrUpdateAsync(
            WaitUntil.Completed,
            deploymentName,
            deployment,
            cancellationToken);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"ARM deployment {deploymentName} completed.");
    }

    private void EnsureNames(Prototype prototype)
    {
        prototype.Region = string.IsNullOrWhiteSpace(prototype.Region) ? _provisioningOptions.Location : prototype.Region;
        prototype.Sku = string.IsNullOrWhiteSpace(prototype.Sku) ? _costControlOptions.AllowedSkus.First() : prototype.Sku;
        prototype.AzureResourceGroupName = string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName)
            ? PrototypeResourceNaming.CreateResourceGroupName(prototype.Slug)
            : prototype.AzureResourceGroupName;
        prototype.AzureAppServicePlanName = string.IsNullOrWhiteSpace(prototype.AzureAppServicePlanName)
            ? PrototypeResourceNaming.CreateAppServicePlanName(prototype.Slug)
            : prototype.AzureAppServicePlanName;
        prototype.AzureWebAppName = string.IsNullOrWhiteSpace(prototype.AzureWebAppName)
            ? PrototypeResourceNaming.CreateWebAppName(prototype.Slug)
            : prototype.AzureWebAppName;
        prototype.PrototypeUrl = string.IsNullOrWhiteSpace(prototype.PrototypeUrl)
            ? $"https://{prototype.AzureWebAppName}.azurewebsites.net"
            : prototype.PrototypeUrl;
    }
}
