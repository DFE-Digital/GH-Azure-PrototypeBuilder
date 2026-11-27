using System.Globalization;
using System.Text.Json;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed record GitHubHealthSnapshot
{
    public bool IsConfigured { get; init; }
    public bool UsesGitHubApp { get; init; }
    public bool ValidateWithApi { get; init; }
    public long AppId { get; init; }
    public long InstallationId { get; init; }
    public bool PrivateKeyConfigured { get; init; }
    public bool PersonalAccessTokenConfigured { get; init; }
    public string? AuthenticationError { get; init; }
    public string? AppJson { get; init; }
    public string? InstallationJson { get; init; }
    public string? InstallationReadError { get; init; }
    public string? RateLimitJson { get; init; }
    public string? AuthenticatedLogin { get; init; }
    public string? TokenScopes { get; init; }
    public int? AccessibleRepositoryCount { get; init; }
    public string? RepositoryAccessError { get; init; }
    public bool OrganisationAccessChecked { get; init; }
    public IReadOnlyDictionary<string, GitHubOrganisationAccessResult> OrganisationAccess { get; init; }
        = new Dictionary<string, GitHubOrganisationAccessResult>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<GitHubOrganisation> Organisations { get; init; } = [];
    public IReadOnlyList<string> DefaultCollaborators { get; init; } = [];
    public string DefaultCollaboratorPermission { get; init; } = "admin";
    public string WorkflowBranch { get; init; } = "main";
    public bool AutoConfigureDeployment { get; init; }
    public string ApiBaseUrl { get; init; } = "https://api.github.com";
}

internal sealed record GitHubOrganisationAccessResult
{
    public bool CanAccess { get; init; }
    public string? CanonicalLogin { get; init; }
    public string? HtmlUrl { get; init; }
    public string? Error { get; init; }
}

internal static class GitHubHealthReportBuilder
{
    private static readonly (string Key, string Label, string Required, string Purpose)[] RequiredPermissions =
    [
        ("administration", "Administration", "write", "Create repositories, assign collaborators, and delete repositories when required."),
        ("contents", "Contents", "write", "Commit Prototype Kit files and read package.json."),
        ("workflows", "Workflows", "write", "Write workflow files under .github/workflows."),
        ("actions", "Actions", "write", "List and dispatch workflow runs."),
        ("secrets", "Secrets", "write", "Write repository secrets such as the Azure publish profile."),
        ("metadata", "Metadata", "read", "Read repository metadata.")
    ];

    public static GitHubHealthReport Build(GitHubHealthSnapshot snapshot)
    {
        var issues = new List<string>();
        var installation = ReadInstallation(snapshot.InstallationJson, snapshot.InstallationId);
        var app = ReadApp(snapshot.AppJson);
        var rateLimit = ReadRateLimit(snapshot.RateLimitJson);
        var permissions = ReadPermissionMap(snapshot.InstallationJson);
        var installationRead = TryParse(snapshot.InstallationJson, out _);
        var permissionsChecked = snapshot.UsesGitHubApp &&
                                 string.IsNullOrWhiteSpace(snapshot.AuthenticationError) &&
                                 installationRead;

        if (!snapshot.IsConfigured)
        {
            issues.Add("Set a GitHub App (AppId, InstallationId, and private key) or a personal access token.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AuthenticationError))
        {
            issues.Add(snapshot.AuthenticationError);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.InstallationReadError) &&
            string.IsNullOrWhiteSpace(snapshot.AuthenticationError))
        {
            issues.Add(snapshot.InstallationReadError);
        }

        if (installation?.IsSuspended == true)
        {
            var account = string.IsNullOrWhiteSpace(installation.AccountLogin)
                ? "the installation"
                : installation.AccountLogin;
            issues.Add($"The GitHub App installation on {account} is suspended.");
        }

        var permissionChecks = new List<GitHubPermissionCheck>();
        if (permissionsChecked)
        {
            foreach (var required in RequiredPermissions)
            {
                permissions.TryGetValue(required.Key, out var granted);
                var satisfied = Satisfies(granted, required.Required);
                permissionChecks.Add(new GitHubPermissionCheck
                {
                    Label = required.Label,
                    RequiredLabel = DisplayLevel(required.Required),
                    GrantedLabel = string.IsNullOrWhiteSpace(granted) ? "Not granted" : DisplayLevel(granted),
                    IsSatisfied = satisfied,
                    Purpose = required.Purpose
                });

                if (!satisfied)
                {
                    issues.Add(string.IsNullOrWhiteSpace(granted)
                        ? $"{required.Label} is not granted. Prototype Manager needs {DisplayLevel(required.Required).ToLowerInvariant()}."
                        : $"{required.Label} is {DisplayLevel(granted).ToLowerInvariant()}. Prototype Manager needs {DisplayLevel(required.Required).ToLowerInvariant()}.");
                }
            }
        }

        var requiredKeys = RequiredPermissions
            .Select(permission => permission.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additionalPermissions = permissions
            .Where(permission => !requiredKeys.Contains(permission.Key))
            .OrderBy(permission => permission.Key, StringComparer.OrdinalIgnoreCase)
            .Select(permission => new GitHubGrantedPermission
            {
                Label = DisplayPermissionKey(permission.Key),
                LevelLabel = DisplayLevel(permission.Value)
            })
            .ToList();

        var installationLogin = installation?.AccountLogin;
        var organisationHealth = snapshot.Organisations
            .OrderBy(organisation => organisation.Name, StringComparer.OrdinalIgnoreCase)
            .Select(organisation =>
            {
                snapshot.OrganisationAccess.TryGetValue(organisation.Name, out var access);
                var canonical = access?.CanonicalLogin;
                var matchesInstallation = !string.IsNullOrWhiteSpace(installationLogin) &&
                    (string.Equals(organisation.Name, installationLogin, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(canonical, installationLogin, StringComparison.OrdinalIgnoreCase));

                if (snapshot.OrganisationAccessChecked && access is { CanAccess: false })
                {
                    var detail = string.IsNullOrWhiteSpace(access.Error) ? string.Empty : $" {access.Error}";
                    issues.Add($"{organisation.Name} is not accessible.{detail}");
                }

                return new GitHubOrganisationHealth
                {
                    Id = organisation.Id,
                    Name = organisation.Name,
                    CreatedBy = organisation.CreatedBy,
                    CreatedAt = organisation.CreatedAt,
                    WasChecked = snapshot.OrganisationAccessChecked,
                    CanAccess = access?.CanAccess == true,
                    CanonicalLogin = canonical,
                    HtmlUrl = access?.HtmlUrl,
                    MatchesInstallation = matchesInstallation,
                    AccessError = access?.Error
                };
            })
            .ToList();

        var authenticated = snapshot.IsConfigured && string.IsNullOrWhiteSpace(snapshot.AuthenticationError);
        if (authenticated && snapshot.Organisations.Count == 0)
        {
            issues.Add("No GitHub organisations are allowed, so creating and hosting repositories will be rejected.");
        }

        if (authenticated &&
            snapshot.UsesGitHubApp &&
            installation is not null &&
            !string.IsNullOrWhiteSpace(installationLogin) &&
            snapshot.Organisations.Count > 0 &&
            organisationHealth.All(organisation => !organisation.MatchesInstallation))
        {
            issues.Add($"The GitHub App is installed on {installationLogin}, which is not an allowed organisation.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RepositoryAccessError) && authenticated)
        {
            issues.Add(snapshot.RepositoryAccessError);
        }

        var rateExhausted = rateLimit is { Remaining: 0 };
        if (rateLimit is not null && authenticated)
        {
            if (rateExhausted)
            {
                issues.Add("GitHub API rate limit is exhausted.");
            }
            else if (rateLimit.Remaining < 100)
            {
                issues.Add($"GitHub API rate limit is nearly exhausted ({rateLimit.Remaining} requests remaining).");
            }
        }

        var unavailable = !snapshot.IsConfigured ||
                          !string.IsNullOrWhiteSpace(snapshot.AuthenticationError) ||
                          installation?.IsSuspended == true ||
                          rateExhausted;
        var status = unavailable
            ? GitHubHealthStatus.Unavailable
            : issues.Count > 0
                ? GitHubHealthStatus.Attention
                : GitHubHealthStatus.Healthy;

        return new GitHubHealthReport
        {
            Status = status,
            Summary = SummaryFor(status, snapshot.UsesGitHubApp, installationLogin, issues.Count, snapshot.IsConfigured),
            CheckedAt = DateTimeOffset.UtcNow,
            IsConfigured = snapshot.IsConfigured,
            UsesGitHubApp = snapshot.UsesGitHubApp,
            AuthenticationMode = snapshot.UsesGitHubApp
                ? "GitHub App"
                : snapshot.IsConfigured
                    ? "Personal access token"
                    : "Not configured",
            ValidateWithApi = snapshot.ValidateWithApi,
            AppId = snapshot.AppId,
            InstallationId = snapshot.InstallationId,
            PrivateKeyConfigured = snapshot.PrivateKeyConfigured,
            PersonalAccessTokenConfigured = snapshot.PersonalAccessTokenConfigured,
            AppName = app.Name,
            AppUrl = app.Url,
            AuthenticatedLogin = snapshot.AuthenticatedLogin,
            TokenScopes = SplitScopes(snapshot.TokenScopes),
            Installation = installation,
            PermissionsChecked = permissionsChecked,
            Permissions = permissionChecks,
            AdditionalPermissions = additionalPermissions,
            AccessibleRepositoryCount = snapshot.AccessibleRepositoryCount,
            RepositoryAccessError = snapshot.RepositoryAccessError,
            RateLimit = rateLimit,
            Organisations = organisationHealth,
            DefaultCollaborators = snapshot.DefaultCollaborators,
            DefaultCollaboratorPermission = snapshot.DefaultCollaboratorPermission,
            WorkflowBranch = snapshot.WorkflowBranch,
            AutoConfigureDeployment = snapshot.AutoConfigureDeployment,
            ApiBaseUrl = snapshot.ApiBaseUrl,
            Issues = issues
        };
    }

    internal static bool Satisfies(string? granted, string required) => Rank(granted) >= Rank(required);

    private static string SummaryFor(
        GitHubHealthStatus status,
        bool usesGitHubApp,
        string? installationLogin,
        int issueCount,
        bool isConfigured)
    {
        return status switch
        {
            GitHubHealthStatus.Healthy when usesGitHubApp && !string.IsNullOrWhiteSpace(installationLogin) =>
                $"GitHub is healthy. The app installation on {installationLogin} has the access Prototype Manager needs.",
            GitHubHealthStatus.Healthy =>
                "GitHub is healthy. The configured credentials can access the allowed organisations.",
            GitHubHealthStatus.Attention when issueCount == 1 =>
                "GitHub needs attention. 1 check failed.",
            GitHubHealthStatus.Attention =>
                $"GitHub needs attention. {issueCount} checks failed.",
            _ when !isConfigured =>
                "GitHub is not configured.",
            _ =>
                "GitHub is unavailable."
        };
    }

    private static GitHubInstallationHealth? ReadInstallation(string? json, long installationId)
    {
        if (!TryParse(json, out var root))
        {
            return null;
        }

        var login = string.Empty;
        var accountType = string.Empty;
        var accountUrl = string.Empty;
        if (root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
        {
            login = ReadString(account, "login");
            accountType = ReadString(account, "type");
            accountUrl = ReadString(account, "html_url");
        }

        var selection = ReadString(root, "repository_selection");
        var settingsUrl = ReadString(root, "html_url");
        if (string.IsNullOrWhiteSpace(settingsUrl) && installationId > 0)
        {
            settingsUrl = string.Equals(accountType, "Organization", StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrWhiteSpace(login)
                ? $"https://github.com/organizations/{login}/settings/installations/{installationId}"
                : $"https://github.com/settings/installations/{installationId}";
        }

        var suspended = root.TryGetProperty("suspended_at", out var suspendedAt) &&
                        suspendedAt.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(suspendedAt.GetString());

        return new GitHubInstallationHealth
        {
            AccountLogin = login,
            AccountType = string.IsNullOrWhiteSpace(accountType) ? "Unknown" : accountType,
            AccountUrl = accountUrl,
            RepositorySelectionLabel = selection.ToLowerInvariant() switch
            {
                "all" => "All repositories",
                "selected" => "Selected repositories",
                "" => "Unknown",
                _ => selection
            },
            IsSuspended = suspended,
            SettingsUrl = string.IsNullOrWhiteSpace(settingsUrl) ? null : settingsUrl
        };
    }

    private static (string? Name, string? Url) ReadApp(string? json)
    {
        if (!TryParse(json, out var root))
        {
            return (null, null);
        }

        var name = ReadString(root, "name");
        var url = ReadString(root, "html_url");
        return (string.IsNullOrWhiteSpace(name) ? null : name, string.IsNullOrWhiteSpace(url) ? null : url);
    }

    private static Dictionary<string, string> ReadPermissionMap(string? json)
    {
        var permissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryParse(json, out var root) ||
            !root.TryGetProperty("permissions", out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return permissions;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                permissions[property.Name] = property.Value.GetString()!;
            }
        }

        return permissions;
    }

    private static GitHubRateLimitHealth? ReadRateLimit(string? json)
    {
        if (!TryParse(json, out var root))
        {
            return null;
        }

        JsonElement core = default;
        var found = root.TryGetProperty("resources", out var resources) &&
                    resources.ValueKind == JsonValueKind.Object &&
                    resources.TryGetProperty("core", out core);
        if (!found && root.TryGetProperty("rate", out var rate) && rate.ValueKind == JsonValueKind.Object)
        {
            core = rate;
            found = true;
        }

        if (!found ||
            !core.TryGetProperty("limit", out var limitElement) ||
            !limitElement.TryGetInt32(out var limit) ||
            !core.TryGetProperty("remaining", out var remainingElement) ||
            !remainingElement.TryGetInt32(out var remaining))
        {
            return null;
        }

        var resetsAt = core.TryGetProperty("reset", out var resetElement) && resetElement.TryGetInt64(out var reset)
            ? DateTimeOffset.FromUnixTimeSeconds(reset)
            : DateTimeOffset.UnixEpoch;
        var used = core.TryGetProperty("used", out var usedElement) && usedElement.TryGetInt32(out var usedValue)
            ? usedValue
            : 0;

        return new GitHubRateLimitHealth
        {
            Limit = limit,
            Remaining = remaining,
            Used = used,
            ResetsAt = resetsAt
        };
    }

    private static bool TryParse(string? json, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> SplitScopes(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes))
        {
            return [];
        }

        return scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToList();
    }

    private static int Rank(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "admin" => 3,
        "write" => 2,
        "read" => 1,
        _ => 0
    };

    private static string DisplayLevel(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return "Not granted";
        }

        var trimmed = level.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }

    private static string DisplayPermissionKey(string key)
    {
        var words = key.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(
            ' ',
            words.Select(word => word.Length == 0
                ? word
                : char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..]));
    }
}
