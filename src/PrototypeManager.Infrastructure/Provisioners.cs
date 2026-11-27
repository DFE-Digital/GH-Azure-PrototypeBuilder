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
    IFreeAppServicePlanPool freePlanPool,
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

        await ApplyResourceNamesAsync(prototype, cancellationToken);

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

    private async Task ApplyResourceNamesAsync(Prototype prototype, CancellationToken cancellationToken)
    {
        prototype.Region = string.IsNullOrWhiteSpace(prototype.Region) ? _provisioningOptions.Location : prototype.Region;
        prototype.Sku = string.IsNullOrWhiteSpace(prototype.Sku) ? _costControlOptions.AllowedSkus.First() : prototype.Sku;
        prototype.AzureResourceGroupName = string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName)
            ? PrototypeResourceNaming.CreateResourceGroupName(prototype.Slug)
            : prototype.AzureResourceGroupName;
        prototype.AzureWebAppName = string.IsNullOrWhiteSpace(prototype.AzureWebAppName)
            ? PrototypeResourceNaming.CreateWebAppName(prototype.Slug)
            : prototype.AzureWebAppName;

        if (string.IsNullOrWhiteSpace(prototype.AzureAppServicePlanName))
        {
            var assignment = await freePlanPool.AllocateAsync(cancellationToken);
            prototype.AzureAppServicePlanName = assignment.PlanName;
        }
    }
}

internal sealed class AzureBicepPrototypeProvisioner(
    IOptions<ProvisioningOptions> provisioningOptions,
    IOptions<CostControlOptions> costControlOptions,
    IHostEnvironment environment,
    ICostControlPolicy costControlPolicy,
    IFreeAppServicePlanPool freePlanPool,
    IAuditService auditService,
    IPrototypeAzureDeletionPolicy deletionPolicy,
    AzureWebAppSettingsSyncService azureWebAppSettingsSyncService,
    ILogger<AzureBicepPrototypeProvisioner> logger) : IPrototypeProvisioner
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;
    private readonly CostControlOptions _costControlOptions = costControlOptions.Value;
    private readonly ArmClient _armClient = new(new DefaultAzureCredential());

    public async Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default)
    {
        var planAssignment = await EnsureNamesAndPlanAsync(prototype, cancellationToken);
        await costControlPolicy.ValidatePrototypeAsync(prototype);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Azure mode: preparing resources in {_provisioningOptions.Location} (subscription {_provisioningOptions.AzureSubscriptionId}).");

        var subscription = GetSubscription();
        var resourceGroup = await CreateOrUpdateResourceGroupAsync(subscription, prototype, cancellationToken);
        await DeployTemplateAsync(resourceGroup, prototype, planAssignment.PlanResourceId, prototypePassword, cancellationToken);

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
        // Patch PASSWORD only — do not redeploy the ARM template, which would wipe custom app settings.
        await azureWebAppSettingsSyncService.SetApplicationSettingAsync(
            prototype,
            PrototypeAppSettingCatalog.PasswordKey,
            prototypePassword,
            cancellationToken);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            "Updated PASSWORD application setting on the Azure web app.");

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
            // Deletes the prototype web app (and any dedicated escalation plan in this RG).
            // Shared Free pool plans live in a separate RG and are left in place for reuse.
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

    private async Task DeployTemplateAsync(
        ResourceGroupResource resourceGroup,
        Prototype prototype,
        string appServicePlanId,
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
            appServicePlanId = new { value = appServicePlanId },
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

    private async Task<AppServicePlanAssignment> EnsureNamesAndPlanAsync(
        Prototype prototype,
        CancellationToken cancellationToken)
    {
        prototype.Region = string.IsNullOrWhiteSpace(prototype.Region) ? _provisioningOptions.Location : prototype.Region;
        prototype.Sku = string.IsNullOrWhiteSpace(prototype.Sku) ? _costControlOptions.AllowedSkus.First() : prototype.Sku;
        prototype.AzureResourceGroupName = string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName)
            ? PrototypeResourceNaming.CreateResourceGroupName(prototype.Slug)
            : prototype.AzureResourceGroupName;
        prototype.AzureWebAppName = string.IsNullOrWhiteSpace(prototype.AzureWebAppName)
            ? PrototypeResourceNaming.CreateWebAppName(prototype.Slug)
            : prototype.AzureWebAppName;
        prototype.PrototypeUrl = string.IsNullOrWhiteSpace(prototype.PrototypeUrl)
            ? $"https://{prototype.AzureWebAppName}.azurewebsites.net"
            : prototype.PrototypeUrl;

        if (!string.IsNullOrWhiteSpace(prototype.AzureAppServicePlanName))
        {
            return new AppServicePlanAssignment
            {
                PlanName = prototype.AzureAppServicePlanName,
                PlanResourceGroupName = ResolvePlanResourceGroupName(prototype),
                PlanResourceId = BuildPlanResourceId(prototype)
            };
        }

        var assignment = await freePlanPool.AllocateAsync(cancellationToken);
        prototype.AzureAppServicePlanName = assignment.PlanName;

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            $"Assigned shared Free App Service plan {assignment.PlanName}.");

        return assignment;
    }

    private string ResolvePlanResourceGroupName(Prototype prototype)
    {
        if (PrototypeResourceNaming.IsFreePoolPlanName(prototype.AzureAppServicePlanName))
        {
            return string.IsNullOrWhiteSpace(_provisioningOptions.AppServicePlanPoolResourceGroupName)
                ? PrototypeResourceNaming.FreePlanPoolResourceGroupName
                : _provisioningOptions.AppServicePlanPoolResourceGroupName.Trim();
        }

        return prototype.AzureResourceGroupName;
    }

    private string BuildPlanResourceId(Prototype prototype)
    {
        var subscriptionId = string.IsNullOrWhiteSpace(prototype.AzureSubscriptionId) ||
                             string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase)
            ? _provisioningOptions.AzureSubscriptionId
            : prototype.AzureSubscriptionId;

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            throw new InvalidOperationException("Provisioning:AzureSubscriptionId must be configured in Azure mode.");
        }

        var planRg = ResolvePlanResourceGroupName(prototype);
        return $"/subscriptions/{subscriptionId}/resourceGroups/{planRg}/providers/Microsoft.Web/serverfarms/{prototype.AzureAppServicePlanName}";
    }
}
