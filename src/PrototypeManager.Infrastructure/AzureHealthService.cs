using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class AzureHealthService(
    IOptions<ProvisioningOptions> provisioningOptions,
    IOptions<CostControlOptions> costControlOptions,
    IHostEnvironment environment,
    ILogger<AzureHealthService> logger) : IAzureHealthService
{
    public async Task<AzureHealthReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var provisioning = provisioningOptions.Value;
        var costControl = costControlOptions.Value;
        var templatePath = provisioning.CompiledTemplatePath.Trim();
        var snapshot = new AzureHealthSnapshot
        {
            IsAzureMode = provisioning.IsAzureMode,
            IsDevelopment = environment.IsDevelopment(),
            Mode = string.IsNullOrWhiteSpace(provisioning.Mode) ? "Mock" : provisioning.Mode.Trim(),
            SubscriptionId = provisioning.AzureSubscriptionId.Trim(),
            Location = string.IsNullOrWhiteSpace(provisioning.Location) ? "westeurope" : provisioning.Location.Trim(),
            PoolResourceGroupName = string.IsNullOrWhiteSpace(provisioning.AppServicePlanPoolResourceGroupName)
                ? PrototypeResourceNaming.FreePlanPoolResourceGroupName
                : provisioning.AppServicePlanPoolResourceGroupName.Trim(),
            TemplateFound = File.Exists(Path.Combine(environment.ContentRootPath, templatePath)),
            TemplatePath = templatePath,
            AllowedRegions = costControl.AllowedRegions
                .Where(region => !string.IsNullOrWhiteSpace(region))
                .Select(region => region.Trim())
                .ToList(),
            AllowedSkus = costControl.AllowedSkus
                .Where(sku => !string.IsNullOrWhiteSpace(sku))
                .Select(sku => sku.Trim())
                .ToList()
        };

        if (!provisioning.IsAzureMode || string.IsNullOrWhiteSpace(snapshot.SubscriptionId))
        {
            return Finish(snapshot);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            snapshot = await ProbeAsync(snapshot, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Azure health check timed out.");
            snapshot = snapshot with { AuthenticationError = "Azure did not respond in time." };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Azure health check failed.");
            snapshot = snapshot with { AuthenticationError = SafeMessage(exception) };
        }

        return Finish(snapshot);
    }

    private async Task<AzureHealthSnapshot> ProbeAsync(AzureHealthSnapshot snapshot, CancellationToken cancellationToken)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });
        var armClient = new ArmClient(credential);
        var subscription = armClient.GetSubscriptionResource(
            new ResourceIdentifier($"/subscriptions/{snapshot.SubscriptionId}"));

        Response<SubscriptionResource> subscriptionResponse;
        try
        {
            subscriptionResponse = await subscription.GetAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return snapshot with { AuthenticationError = SafeMessage(exception) };
        }

        var subscriptionName = subscriptionResponse.Value.Data.DisplayName;
        var readable = snapshot with
        {
            SubscriptionReadable = true,
            SubscriptionName = string.IsNullOrWhiteSpace(subscriptionName) ? null : subscriptionName
        };

        try
        {
            var resourceGroup = await subscription.GetResourceGroupAsync(readable.PoolResourceGroupName, cancellationToken);
            var plans = new List<AzurePlanSnapshot>();
            await foreach (var plan in resourceGroup.Value.GetAppServicePlans().GetAllAsync(cancellationToken: cancellationToken))
            {
                if (!PrototypeResourceNaming.IsFreePoolPlanName(plan.Data.Name))
                {
                    continue;
                }

                plans.Add(new AzurePlanSnapshot(plan.Data.Name, plan.Data.NumberOfSites ?? 0));
            }

            return readable with
            {
                PoolResourceGroupExists = true,
                PoolResourceGroupLocation = resourceGroup.Value.Data.Location.Name,
                Plans = plans
            };
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return readable with { PoolResourceGroupExists = false };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return readable with { AuthenticationError = SafeMessage(exception) };
        }
    }

    private AzureHealthReport Finish(AzureHealthSnapshot snapshot)
    {
        var report = AzureHealthReportBuilder.Build(snapshot);
        logger.LogInformation(
            "Azure health check completed with status {Status}. Issues {IssueCount}.",
            report.Status,
            report.Issues.Count);
        return report;
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        if (message.Contains("BEGIN ", StringComparison.Ordinal) ||
            message.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "Azure authentication failed. Check the credential used by this environment.";
        }

        return message.Length > 400 ? message[..400] : message;
    }
}
