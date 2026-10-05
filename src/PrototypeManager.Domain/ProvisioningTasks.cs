namespace PrototypeManager.Domain;

public static class ProvisioningTasks
{
    public const string CompletedAction = "Provisioning task completed";

    public const string Queued = "queued";
    public const string GitHubCreateRepository = "github-create-repository";
    public const string GitHubScaffoldKit = "github-scaffold-kit";
    public const string AzureResources = "azure-resources";
    public const string GitHubConfigure = "github-configure";
    public const string GitHubDeploy = "github-deploy";
    public const string SiteVerify = "site-verify";

    public static readonly IReadOnlyList<ProvisioningTaskDefinition> FullSetupFlow =
    [
        new(Queued, "Queue prototype for provisioning"),
        new(AzureResources, "Create Azure hosting"),
        new(GitHubConfigure, "Configure GitHub deployment"),
        new(GitHubDeploy, "Deploy prototype from GitHub"),
        new(SiteVerify, "Testing your app", "Checking deployment. This can take a few minutes, please wait.")
    ];

    public static readonly IReadOnlyList<ProvisioningTaskDefinition> ScaffoldSetupFlow =
    [
        new(Queued, "Queue prototype for provisioning"),
        new(GitHubCreateRepository, "Create GitHub repository"),
        new(GitHubScaffoldKit, "Add GOV.UK Prototype Kit code"),
        new(AzureResources, "Create Azure hosting"),
        new(GitHubConfigure, "Configure GitHub deployment"),
        new(GitHubDeploy, "Deploy prototype from GitHub"),
        new(SiteVerify, "Testing your app", "Checking deployment. This can take a few minutes, please wait.")
    ];

    public static readonly IReadOnlyList<ProvisioningTaskDefinition> HostingOnlyFlow =
    [
        new(Queued, "Queue prototype for provisioning"),
        new(AzureResources, "Create Azure hosting")
    ];

    public static readonly IReadOnlyList<ProvisioningTaskDefinition> GitHubSetupFlow =
    [
        new(GitHubConfigure, "Configure GitHub deployment"),
        new(GitHubDeploy, "Deploy prototype from GitHub"),
        new(SiteVerify, "Testing your app", "Checking deployment. This can take a few minutes, please wait.")
    ];
}

public sealed record ProvisioningTaskDefinition(string Id, string Label, string? Description = null);

public enum ProvisioningTaskState
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}

public sealed class ProvisioningProgressSnapshot
{
    public bool IsActive { get; init; }
    public bool IsComplete { get; init; }
    public bool IsFailed { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PrototypeUrl { get; init; }
    public IReadOnlyList<ProvisioningTaskProgressItem> Tasks { get; init; } = [];
}

public sealed class ProvisioningTaskProgressItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required string Status { get; init; }
}
