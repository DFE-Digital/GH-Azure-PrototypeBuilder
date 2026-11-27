namespace PrototypeManager.Application;

public sealed class AzureHealthReport
{
    public DependencyHealthStatus Status { get; init; } = DependencyHealthStatus.Unavailable;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
    public bool IsAzureMode { get; init; }
    public string Mode { get; init; } = "Mock";
    public string SubscriptionId { get; init; } = string.Empty;
    public string? SubscriptionName { get; init; }
    public string? SubscriptionUrl { get; init; }
    public string Location { get; init; } = string.Empty;
    public string PoolResourceGroupName { get; init; } = string.Empty;
    public string? PoolResourceGroupUrl { get; init; }
    public bool PoolResourceGroupExists { get; init; }
    public string? PoolResourceGroupLocation { get; init; }
    public bool SubscriptionReadable { get; init; }
    public bool TemplateFound { get; init; }
    public string TemplatePath { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedRegions { get; init; } = [];
    public IReadOnlyList<string> AllowedSkus { get; init; } = [];
    public int UsedAppSlots { get; init; }
    public int TotalAppSlots { get; init; }
    public int RemainingAppSlots { get; init; }
    public IReadOnlyList<AzurePlanHealth> Plans { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];

    public string StatusLabel => DependencyHealth.Label(Status);
}

public sealed class AzurePlanHealth
{
    public string Name { get; init; } = string.Empty;
    public int AppCount { get; init; }
    public int AppCapacity { get; init; }
    public int SpareApps => Math.Max(0, AppCapacity - AppCount);
}

public interface IAzureHealthService
{
    Task<AzureHealthReport> GetReportAsync(CancellationToken cancellationToken = default);
}
