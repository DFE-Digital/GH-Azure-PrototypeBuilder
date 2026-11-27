using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed record AzureHealthSnapshot
{
    public bool IsAzureMode { get; init; }
    public bool IsDevelopment { get; init; }
    public string Mode { get; init; } = "Mock";
    public string SubscriptionId { get; init; } = string.Empty;
    public string? SubscriptionName { get; init; }
    public bool SubscriptionReadable { get; init; }
    public string? AuthenticationError { get; init; }
    public string Location { get; init; } = string.Empty;
    public string PoolResourceGroupName { get; init; } = string.Empty;
    public bool PoolResourceGroupExists { get; init; }
    public string? PoolResourceGroupLocation { get; init; }
    public bool TemplateFound { get; init; }
    public string TemplatePath { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedRegions { get; init; } = [];
    public IReadOnlyList<string> AllowedSkus { get; init; } = [];
    public IReadOnlyList<AzurePlanSnapshot> Plans { get; init; } = [];
}

internal sealed record AzurePlanSnapshot(string Name, int SiteCount);

internal static class AzureHealthReportBuilder
{
    public static AzureHealthReport Build(AzureHealthSnapshot snapshot)
    {
        var issues = new List<string>();
        var plans = snapshot.Plans
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .Select(plan => new AzurePlanHealth
            {
                Name = plan.Name,
                AppCount = plan.SiteCount,
                AppCapacity = PrototypeResourceNaming.FreeAppsPerPlan
            })
            .ToList();
        var totalSlots = PrototypeResourceNaming.MaxFreePlansPerRegion * PrototypeResourceNaming.FreeAppsPerPlan;
        var usedSlots = plans.Sum(plan => plan.AppCount);
        var remainingSlots = Math.Max(0, totalSlots - usedSlots);
        var subscriptionUrl = PortalUrl(snapshot.SubscriptionId, resourceGroupName: null);
        var poolUrl = snapshot.PoolResourceGroupExists
            ? PortalUrl(snapshot.SubscriptionId, snapshot.PoolResourceGroupName)
            : null;

        if (!snapshot.IsAzureMode)
        {
            if (!snapshot.IsDevelopment)
            {
                issues.Add("Provisioning is in Mock mode, so prototypes are not created in Azure.");
            }

            var mockStatus = issues.Count > 0
                ? DependencyHealthStatus.Attention
                : DependencyHealthStatus.Healthy;
            return Report(
                snapshot,
                mockStatus,
                mockStatus == DependencyHealthStatus.Healthy
                    ? "Azure provisioning is in Mock mode. Hosting is simulated and Azure is not contacted."
                    : "Azure needs attention. Provisioning is not using Azure.",
                issues,
                plans,
                usedSlots,
                totalSlots,
                remainingSlots,
                subscriptionUrl,
                poolUrl);
        }

        if (string.IsNullOrWhiteSpace(snapshot.SubscriptionId))
        {
            issues.Add("Azure mode is on, but no subscription ID is configured.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AuthenticationError))
        {
            issues.Add(snapshot.AuthenticationError);
        }

        if (!snapshot.TemplateFound)
        {
            issues.Add("The compiled hosting template was not found, so new prototypes cannot be deployed.");
        }

        if (snapshot.AllowedRegions.Count > 0 &&
            !snapshot.AllowedRegions.Any(region =>
                string.Equals(region, snapshot.Location, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(
                $"Location {snapshot.Location} is not in the allowed regions ({string.Join(", ", snapshot.AllowedRegions)}).");
        }

        var canInspect = snapshot.SubscriptionReadable && string.IsNullOrWhiteSpace(snapshot.AuthenticationError);
        if (canInspect && !snapshot.PoolResourceGroupExists)
        {
            issues.Add(
                $"Free plan pool resource group {snapshot.PoolResourceGroupName} was not found. It is created the first time a prototype is provisioned.");
        }

        if (canInspect &&
            snapshot.PoolResourceGroupExists &&
            !string.IsNullOrWhiteSpace(snapshot.PoolResourceGroupLocation) &&
            !string.Equals(snapshot.PoolResourceGroupLocation, snapshot.Location, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                $"The plan pool is in {snapshot.PoolResourceGroupLocation}, but provisioning is configured for {snapshot.Location}.");
        }

        if (canInspect && snapshot.PoolResourceGroupExists && remainingSlots == 0)
        {
            issues.Add("The Free App Service plan pool has no spare app slots.");
        }
        else if (canInspect && snapshot.PoolResourceGroupExists && remainingSlots <= PrototypeResourceNaming.FreeAppsPerPlan)
        {
            issues.Add($"The Free plan pool is nearly full ({remainingSlots} app slots remaining).");
        }

        var unavailable = string.IsNullOrWhiteSpace(snapshot.SubscriptionId) ||
                          !string.IsNullOrWhiteSpace(snapshot.AuthenticationError);
        var status = unavailable
            ? DependencyHealthStatus.Unavailable
            : issues.Count > 0
                ? DependencyHealthStatus.Attention
                : DependencyHealthStatus.Healthy;
        var summary = status switch
        {
            DependencyHealthStatus.Healthy =>
                $"Azure is healthy. The subscription can host prototypes in {snapshot.Location}.",
            DependencyHealthStatus.Attention when issues.Count == 1 =>
                "Azure needs attention. 1 check failed.",
            DependencyHealthStatus.Attention =>
                $"Azure needs attention. {issues.Count} checks failed.",
            _ when string.IsNullOrWhiteSpace(snapshot.SubscriptionId) =>
                "Azure is not configured.",
            _ =>
                "Azure is unavailable."
        };

        return Report(snapshot, status, summary, issues, plans, usedSlots, totalSlots, remainingSlots, subscriptionUrl, poolUrl);
    }

    private static AzureHealthReport Report(
        AzureHealthSnapshot snapshot,
        DependencyHealthStatus status,
        string summary,
        IReadOnlyList<string> issues,
        IReadOnlyList<AzurePlanHealth> plans,
        int usedSlots,
        int totalSlots,
        int remainingSlots,
        string? subscriptionUrl,
        string? poolUrl) => new()
    {
        Status = status,
        Summary = summary,
        CheckedAt = DateTimeOffset.UtcNow,
        IsAzureMode = snapshot.IsAzureMode,
        Mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? "Mock" : snapshot.Mode,
        SubscriptionId = snapshot.SubscriptionId,
        SubscriptionName = snapshot.SubscriptionName,
        SubscriptionUrl = subscriptionUrl,
        Location = snapshot.Location,
        PoolResourceGroupName = snapshot.PoolResourceGroupName,
        PoolResourceGroupUrl = poolUrl,
        PoolResourceGroupExists = snapshot.PoolResourceGroupExists,
        PoolResourceGroupLocation = snapshot.PoolResourceGroupLocation,
        SubscriptionReadable = snapshot.SubscriptionReadable,
        TemplateFound = snapshot.TemplateFound,
        TemplatePath = snapshot.TemplatePath,
        AllowedRegions = snapshot.AllowedRegions,
        AllowedSkus = snapshot.AllowedSkus,
        UsedAppSlots = usedSlots,
        TotalAppSlots = totalSlots,
        RemainingAppSlots = remainingSlots,
        Plans = plans,
        Issues = issues
    };

    private static string? PortalUrl(string subscriptionId, string? resourceGroupName)
    {
        if (!Guid.TryParse(subscriptionId, out var subscription))
        {
            return null;
        }

        var subscriptionPath = $"https://portal.azure.com/#@/resource/subscriptions/{subscription}";
        return string.IsNullOrWhiteSpace(resourceGroupName)
            ? subscriptionPath
            : $"{subscriptionPath}/resourceGroups/{Uri.EscapeDataString(resourceGroupName)}/overview";
    }
}
