namespace PrototypeManager.Application;

public enum DependencyHealthStatus
{
    Healthy,
    Attention,
    Unavailable
}

public static class DependencyHealth
{
    public static DependencyHealthStatus Combine(DependencyHealthStatus left, DependencyHealthStatus right)
    {
        if (left == DependencyHealthStatus.Unavailable || right == DependencyHealthStatus.Unavailable)
        {
            return DependencyHealthStatus.Unavailable;
        }

        if (left == DependencyHealthStatus.Attention || right == DependencyHealthStatus.Attention)
        {
            return DependencyHealthStatus.Attention;
        }

        return DependencyHealthStatus.Healthy;
    }

    public static string Label(DependencyHealthStatus status) => status switch
    {
        DependencyHealthStatus.Healthy => "Healthy",
        DependencyHealthStatus.Attention => "Needs attention",
        _ => "Unavailable"
    };
}

public sealed class GitHubHealthReport
{
    public DependencyHealthStatus Status { get; init; } = DependencyHealthStatus.Unavailable;
    public string Summary { get; init; } = string.Empty;
    public DateTimeOffset CheckedAt { get; init; }
    public bool IsConfigured { get; init; }
    public bool UsesGitHubApp { get; init; }
    public string AuthenticationMode { get; init; } = "Not configured";
    public bool ValidateWithApi { get; init; }
    public long AppId { get; init; }
    public long InstallationId { get; init; }
    public bool PrivateKeyConfigured { get; init; }
    public bool PersonalAccessTokenConfigured { get; init; }
    public string? AppName { get; init; }
    public string? AppUrl { get; init; }
    public string? AuthenticatedLogin { get; init; }
    public IReadOnlyList<string> TokenScopes { get; init; } = [];
    public GitHubInstallationHealth? Installation { get; init; }
    public bool PermissionsChecked { get; init; }
    public IReadOnlyList<GitHubPermissionCheck> Permissions { get; init; } = [];
    public IReadOnlyList<GitHubGrantedPermission> AdditionalPermissions { get; init; } = [];
    public int? AccessibleRepositoryCount { get; init; }
    public string? RepositoryAccessError { get; init; }
    public GitHubRateLimitHealth? RateLimit { get; init; }
    public IReadOnlyList<GitHubOrganisationHealth> Organisations { get; init; } = [];
    public IReadOnlyList<string> DefaultCollaborators { get; init; } = [];
    public string DefaultCollaboratorPermission { get; init; } = string.Empty;
    public string WorkflowBranch { get; init; } = "main";
    public bool AutoConfigureDeployment { get; init; }
    public string ApiBaseUrl { get; init; } = "https://api.github.com";
    public IReadOnlyList<string> Issues { get; init; } = [];

    public int SatisfiedPermissionCount => Permissions.Count(permission => permission.IsSatisfied);
    public int AccessibleOrganisationCount => Organisations.Count(organisation => organisation.CanAccess);

    public string StatusLabel => Status switch
    {
        DependencyHealthStatus.Healthy => "Healthy",
        DependencyHealthStatus.Attention => "Needs attention",
        _ => "Unavailable"
    };
}

public sealed class GitHubInstallationHealth
{
    public string AccountLogin { get; init; } = string.Empty;
    public string AccountType { get; init; } = string.Empty;
    public string AccountUrl { get; init; } = string.Empty;
    public string RepositorySelectionLabel { get; init; } = string.Empty;
    public bool IsSuspended { get; init; }
    public string? SettingsUrl { get; init; }
}

public sealed class GitHubPermissionCheck
{
    public string Label { get; init; } = string.Empty;
    public string RequiredLabel { get; init; } = string.Empty;
    public string GrantedLabel { get; init; } = string.Empty;
    public bool IsSatisfied { get; init; }
    public string Purpose { get; init; } = string.Empty;
}

public sealed class GitHubGrantedPermission
{
    public string Label { get; init; } = string.Empty;
    public string LevelLabel { get; init; } = string.Empty;
}

public sealed class GitHubRateLimitHealth
{
    public int Limit { get; init; }
    public int Remaining { get; init; }
    public int Used { get; init; }
    public DateTimeOffset ResetsAt { get; init; }
}

public sealed class GitHubOrganisationHealth
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public bool WasChecked { get; init; }
    public bool CanAccess { get; init; }
    public string? CanonicalLogin { get; init; }
    public string? HtmlUrl { get; init; }
    public bool MatchesInstallation { get; init; }
    public string? AccessError { get; init; }
}

public interface IGitHubHealthService
{
    Task<GitHubHealthReport> GetReportAsync(CancellationToken cancellationToken = default);
}
