using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class ListModel(
    IPrototypeService prototypeService,
    IPrototypeWebAppRuntimeService webAppRuntimeService) : PageModel
{
    public const string ScopeMine = "mine";
    public const string ScopeAll = "all";
    public const string ScopeTeam = "team";

    public IReadOnlyList<Prototype> Prototypes { get; private set; } = [];

    public IReadOnlyList<PrototypeListTeamNavItem> Teams { get; private set; } = [];

    public IReadOnlyDictionary<Guid, WebAppRuntimeStatus> AppRuntimeByPrototypeId { get; private set; } =
        new Dictionary<Guid, WebAppRuntimeStatus>();

    public string ActiveScope { get; private set; } = ScopeMine;

    public Guid? ActiveTeamId { get; private set; }

    public int YourPrototypeCount { get; private set; }

    public int AllPrototypeCount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? scope, Guid? team)
    {
        await LoadListAsync(scope, team, includeRuntime: true);
    }

    public async Task<IActionResult> OnGetSnapshotAsync(string? scope, Guid? team)
    {
        await LoadListAsync(scope, team, includeRuntime: false);

        var items = Prototypes.Select(prototype =>
        {
            var hasOpenUrl = !string.IsNullOrWhiteSpace(prototype.PrototypeUrl);
            return new
            {
                id = prototype.Id,
                name = prototype.Name,
                status = prototype.Status.ToString(),
                statusFilter = GetStatusFilter(prototype.Status),
                badgeClass = GetStatusBadgeClass(prototype.Status),
                badgeLabel = GetStatusLabel(prototype.Status),
                businessArea = prototype.BusinessArea?.Name ?? "No business area",
                repositoryName = PrototypeGitHubRepositoryUrls.TryGetRepositoryName(prototype.GitHubRepositoryUrl),
                repositoryUrl = string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl)
                    ? null
                    : PrototypeGitHubRepositoryUrls.Normalize(prototype.GitHubRepositoryUrl),
                ownerEmail = prototype.OwnerEmail,
                searchText = GetSearchText(prototype, appRuntime: null),
                manageUrl = Url.Page("/Prototypes/Details", new { id = prototype.Id }),
                openUrl = hasOpenUrl
                    ? Url.Page("/Prototypes/Open", new { id = prototype.Id })
                    : null,
                hasOpenUrl
            };
        }).ToList();

        return new JsonResult(new
        {
            prototypes = items,
            yourPrototypeCount = YourPrototypeCount,
            allPrototypeCount = AllPrototypeCount,
            liveCount = LiveCount,
            activeJobCount = ActiveJobCount,
            failedCount = FailedCount,
            archivedCount = ArchivedCount,
            hasActiveJobs = ActiveJobCount > 0,
            teams = Teams.Select(team => new { id = team.Id, name = team.Name, count = team.Count })
        });
    }

    private async Task LoadListAsync(string? scope, Guid? team, bool includeRuntime)
    {
        var email = User.GetEmail() ?? string.Empty;
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var allVisible = await prototypeService.GetVisibleAsync(email, User.IsAdmin(), User.IsSupport());

        AllPrototypeCount = allVisible.Count;
        YourPrototypeCount = allVisible.Count(prototype => IsNamedContact(prototype, normalizedEmail));

        Teams = allVisible
            .Where(prototype => prototype.BusinessArea is not null)
            .GroupBy(prototype => prototype.BusinessArea!.Id)
            .Select(group =>
            {
                var businessArea = group.First().BusinessArea!;
                return new PrototypeListTeamNavItem(
                    businessArea.Id,
                    businessArea.Name,
                    group.Count());
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ActiveTeamId = team;
        ActiveScope = ResolveScope(scope, team, Teams);

        Prototypes = ActiveScope switch
        {
            ScopeTeam when ActiveTeamId is Guid teamId =>
                allVisible.Where(prototype => prototype.BusinessAreaId == teamId).ToList(),
            ScopeAll => allVisible,
            _ => allVisible.Where(prototype => IsNamedContact(prototype, normalizedEmail)).ToList()
        };

        if (!includeRuntime)
        {
            AppRuntimeByPrototypeId = new Dictionary<Guid, WebAppRuntimeStatus>();
            return;
        }

        var livePrototypes = Prototypes.Where(prototype =>
            prototype.Status == PrototypeStatus.Live &&
            !string.IsNullOrWhiteSpace(prototype.AzureWebAppName));

        var runtimeResults = await Task.WhenAll(livePrototypes.Select(async prototype =>
        {
            var runtime = await webAppRuntimeService.GetStatusAsync(prototype);
            return KeyValuePair.Create(prototype.Id, runtime);
        }));

        AppRuntimeByPrototypeId = runtimeResults.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public WebAppRuntimeStatus? GetAppRuntime(Prototype prototype) =>
        AppRuntimeByPrototypeId.TryGetValue(prototype.Id, out var runtime) ? runtime : null;

    public int LiveCount => Prototypes.Count(prototype => prototype.Status == PrototypeStatus.Live);

    public int ActiveJobCount => Prototypes.Count(prototype =>
        prototype.Status is PrototypeStatus.Requested
            or PrototypeStatus.Queued
            or PrototypeStatus.Provisioning
            or PrototypeStatus.Deleting);

    public int FailedCount => Prototypes.Count(prototype => prototype.Status == PrototypeStatus.Failed);

    public int ArchivedCount => Prototypes.Count(prototype => prototype.Status == PrototypeStatus.Archived);

    public string PageHeading => ActiveScope switch
    {
        ScopeAll => "All prototypes",
        ScopeTeam when Teams.FirstOrDefault(item => item.Id == ActiveTeamId) is { } team => team.Name,
        _ => "Your prototypes"
    };

    public static bool IsExpiringSoon(Prototype prototype) =>
        prototype.Status != PrototypeStatus.Deleted &&
        prototype.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(14);

    public static string GetRuntimeBadgeClass(WebAppPowerState state) => state switch
    {
        WebAppPowerState.Running => "dfe-c-fe-badge dfe-c-fe-badge--success",
        WebAppPowerState.Stopped => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        WebAppPowerState.Starting => "dfe-c-fe-badge dfe-c-fe-badge--info",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--warning"
    };

    public static bool ShouldShowAppRuntime(Prototype prototype, WebAppRuntimeStatus? appRuntime) =>
        prototype.Status == PrototypeStatus.Live &&
        appRuntime is not null &&
        appRuntime.PowerState != WebAppPowerState.Unknown;

    public static string GetRowBadgeClass(Prototype prototype, WebAppRuntimeStatus? appRuntime) =>
        ShouldShowAppRuntime(prototype, appRuntime)
            ? GetRuntimeBadgeClass(appRuntime!.PowerState)
            : GetStatusBadgeClass(prototype.Status);

    public static string GetRowBadgeLabel(Prototype prototype, WebAppRuntimeStatus? appRuntime) =>
        ShouldShowAppRuntime(prototype, appRuntime)
            ? appRuntime!.Label
            : GetStatusLabel(prototype.Status);

    public static string GetStatusBadgeClass(PrototypeStatus status) => status switch
    {
        PrototypeStatus.Live => "dfe-c-fe-badge dfe-c-fe-badge--success",
        PrototypeStatus.Failed => "dfe-c-fe-badge dfe-c-fe-badge--danger",
        PrototypeStatus.Archived => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        PrototypeStatus.Deleted => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        PrototypeStatus.Deleting => "dfe-c-fe-badge dfe-c-fe-badge--warning",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--info"
    };

    public static string GetStatusClass(PrototypeStatus status) => status switch
    {
        PrototypeStatus.Live => "govuk-tag govuk-tag--green",
        PrototypeStatus.Failed => "govuk-tag govuk-tag--red",
        PrototypeStatus.Archived => "govuk-tag govuk-tag--grey",
        PrototypeStatus.Deleted => "govuk-tag govuk-tag--grey",
        PrototypeStatus.Deleting => "govuk-tag govuk-tag--orange",
        _ => "govuk-tag govuk-tag--blue"
    };

    public static string GetStatusLabel(PrototypeStatus status) => status switch
    {
        PrototypeStatus.Live => "Live",
        PrototypeStatus.Failed => "Failed",
        PrototypeStatus.Archived => "Archived",
        PrototypeStatus.Deleting => "Deleting",
        PrototypeStatus.Provisioning => "Provisioning",
        PrototypeStatus.Queued => "Queued",
        PrototypeStatus.Requested => "Requested",
        _ => status.ToString()
    };

    public static string GetStatusFilter(PrototypeStatus status) => status switch
    {
        PrototypeStatus.Live => "live",
        PrototypeStatus.Failed => "failed",
        PrototypeStatus.Archived => "archived",
        PrototypeStatus.Deleting or PrototypeStatus.Provisioning or PrototypeStatus.Queued or PrototypeStatus.Requested => "active",
        _ => "other"
    };

    public static string GetSearchText(Prototype prototype, WebAppRuntimeStatus? appRuntime) =>
        string.Join(' ',
        [
            prototype.Name,
            prototype.ServiceName,
            prototype.BusinessArea?.Name ?? string.Empty,
            PrototypeGitHubRepositoryUrls.TryGetRepositoryName(prototype.GitHubRepositoryUrl) ?? string.Empty,
            prototype.OwnerEmail,
            GetRowBadgeLabel(prototype, appRuntime)
        ]).ToLowerInvariant();

    private static string ResolveScope(string? scope, Guid? team, IReadOnlyList<PrototypeListTeamNavItem> teams)
    {
        if (team.HasValue && teams.Any(item => item.Id == team.Value))
        {
            return ScopeTeam;
        }

        if (string.Equals(scope, ScopeAll, StringComparison.OrdinalIgnoreCase))
        {
            return ScopeAll;
        }

        return ScopeMine;
    }

    private static bool IsNamedContact(Prototype prototype, string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        if (string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return prototype.Contacts.Any(contact =>
            string.Equals(contact.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record PrototypeListTeamNavItem(Guid Id, string Name, int Count);
