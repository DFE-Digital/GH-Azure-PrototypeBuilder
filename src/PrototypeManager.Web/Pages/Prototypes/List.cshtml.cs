using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class ListModel(
    IPrototypeService prototypeService,
    IPrototypeWebAppRuntimeService webAppRuntimeService) : PageModel
{
    public IReadOnlyList<Prototype> Prototypes { get; private set; } = [];

    public IReadOnlyDictionary<Guid, WebAppRuntimeStatus> AppRuntimeByPrototypeId { get; private set; } =
        new Dictionary<Guid, WebAppRuntimeStatus>();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Prototypes = await prototypeService.GetVisibleAsync(User.GetEmail(), User.IsAdmin(), User.IsSupport());

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
            prototype.OwnerEmail,
            GetRowBadgeLabel(prototype, appRuntime)
        ]).ToLowerInvariant();
}
