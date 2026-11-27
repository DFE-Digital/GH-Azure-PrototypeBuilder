using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class DetailsModel(
    IPrototypeService prototypeService,
    IPrototypeAppSettingsService appSettingsService,
    IAccessManagementService accessManagementService,
    IPrototypeWebAppRuntimeService webAppRuntimeService,
    IDeploymentWorkflowGenerator workflowGenerator,
    IProvisioningProgressService provisioningProgressService,
    ICompassCatalogService compassCatalogService,
    IOptions<GitHubOptions> githubOptions,
    IOptions<CostControlOptions> costControlOptions) : PageModel
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;
    private readonly CostControlOptions _costControlOptions = costControlOptions.Value;
    public const string PrototypeSection = "prototype";
    public const string GithubSecuritySection = "github-security";
    public const string SettingsSection = "settings";
    public const string ContactsSection = "contacts";
    public const string HistorySection = "history";

    private static readonly HashSet<string> ValidSections =
    [
        PrototypeSection,
        GithubSecuritySection,
        SettingsSection,
        ContactsSection,
        HistorySection
    ];

    public Prototype? Prototype { get; private set; }
    public IReadOnlyList<PrototypeContact> Contacts { get; private set; } = [];
    public IReadOnlyList<ProvisioningJob> Jobs { get; private set; } = [];
    public IReadOnlyList<AuditEvent> AuditEvents { get; private set; } = [];
    public IReadOnlyList<PrototypeAppSettingView> AppSettings { get; private set; } = [];
    public string DeploymentWorkflow { get; private set; } = string.Empty;
    public string? RevealedPassword { get; private set; }
    public WebAppRuntimeStatus WebAppRuntime { get; private set; } = new();
    public IReadOnlyList<BusinessArea> AvailableBusinessAreas { get; private set; } = [];
    public IReadOnlyList<CompassWorkItemOption> CompassWorkItems { get; private set; } = [];
    public IReadOnlyList<CompassProductOption> CompassProducts { get; private set; } = [];
    public bool CompassConfigured { get; private set; }
    public IReadOnlyList<PrototypeActivityItem> ActivityItems { get; private set; } = [];
    public IReadOnlyList<PrototypeActivityItem> PagedActivityItems { get; private set; } = [];
    public IReadOnlyList<Prototype> SwitchablePrototypes { get; private set; } = [];
    public IReadOnlyList<ExceptionRequest> ExceptionRequests { get; private set; } = [];

    public const int ActivityPageSize = 10;

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ActivityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public int ActivityPage { get; set; } = 1;

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public string? ManagerEmail { get; set; }

    [BindProperty]
    public string? DeputyOwnerEmail { get; set; }

    [BindProperty]
    public string? NewOwnerEmail { get; set; }

    [BindProperty]
    public DateOnly NewExpiryDate { get; set; }

    [BindProperty]
    public Guid? BusinessAreaId { get; set; }

    [BindProperty]
    public int? SelectedCompassWorkItemId { get; set; }

    [BindProperty]
    public string? SelectedCompassProductId { get; set; }

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfigVarKey { get; set; }

    [BindProperty]
    public string? ConfigVarValue { get; set; }

    [BindProperty]
    public string? RequestedSku { get; set; }

    [BindProperty]
    public string? ExceptionReason { get; set; }

    public IEnumerable<PrototypeAppSettingView> OrderedAppSettings =>
        AppSettings.OrderBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<PrototypeAppSettingView> UserAppSettings =>
        AppSettings.Where(setting => setting.IsEditable);

    public string GetAppSettingDisplayValue(PrototypeAppSettingView setting)
    {
        if (PrototypeAppSettingCatalog.IsPasswordKey(setting.Key))
        {
            if (!CanViewPassword)
            {
                return "Restricted";
            }

            return string.IsNullOrWhiteSpace(RevealedPassword) ? "Not set" : RevealedPassword;
        }

        return setting.Value;
    }

    public IEnumerable<PrototypeAppSettingView> SystemAppSettings =>
        AppSettings.Where(setting => !setting.IsEditable);

    public string TabLabel(string section) => section switch
    {
        PrototypeSection => "Overview",
        GithubSecuritySection => "Deploy",
        ContactsSection => "Access",
        HistorySection => "Activity",
        SettingsSection => "Settings",
        _ => section
    };

    public string SectionDescription(string section) => section switch
    {
        PrototypeSection => string.Empty,
        GithubSecuritySection => "Repository connection and automated deployment.",
        ContactsSection => "People who can access and manage this prototype.",
        HistorySection => "Setup, deployment, password, and management actions.",
        SettingsSection => "Environment variables and prototype configuration.",
        _ => string.Empty
    };

    public string ActiveSection =>
        !string.IsNullOrWhiteSpace(Section) && ValidSections.Contains(Section)
            ? Section
            : PrototypeSection;

    public bool CanManage =>
        Prototype is not null &&
        PrototypeAccess.CanManagePrototype(Prototype, Contacts, User.GetEmail(), User.IsAdmin(), User.IsSupport());

    public bool CanManageContacts =>
        Prototype is not null &&
        PrototypeAccess.CanManageContacts(Prototype, User.GetEmail(), User.IsAdmin(), User.IsSupport());

    public bool CanDeletePrototype =>
        Prototype is not null &&
        PrototypeAccess.CanDeletePrototype(Prototype, User.GetEmail(), User.IsAdmin(), User.IsSupport());

    public bool CanViewPassword =>
        Prototype is not null &&
        PrototypeAccess.CanViewPrototypePassword(
            Prototype,
            Contacts,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport());

    public string PasswordFetchUrl =>
        Prototype is null
            ? string.Empty
            : $"{Url.Page("/Prototypes/Details", new { id = Prototype.Id })}?handler=PrototypePassword";

    public string AppSettingsStatusUrl =>
        Prototype is null
            ? string.Empty
            : $"{Url.Page("/Prototypes/Details", new { id = Prototype.Id })}?handler=AppSettings";

    public string AppSettingValueUrl =>
        Prototype is null
            ? string.Empty
            : $"{Url.Page("/Prototypes/Details", new { id = Prototype.Id })}?handler=AppSettingValue";

    public bool HasPendingAppSettingsSync =>
        AppSettings.Any(setting => setting.IsPendingSync);

    public bool CanRetry =>
        Prototype is not null &&
        Prototype.Status == PrototypeStatus.Failed &&
        CanManage;

    public ProvisioningJob? LatestGitHubDeploymentJob =>
        Jobs
            .Where(job => job.Type == ProvisioningJobType.ConfigureGitHubDeployment)
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

    public bool CanConfigureGitHub =>
        Prototype is not null &&
        GitHubTokenConfigured &&
        Prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName) &&
        CanManage &&
        LatestGitHubDeploymentJob?.Status is not (ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

    public bool GitHubDeploymentConfigured =>
        LatestGitHubDeploymentJob?.Status == ProvisioningJobStatus.Succeeded;

    public bool GitHubAutoConfigureEnabled => _githubOptions.AutoConfigureDeployment;

    public bool GitHubTokenConfigured => _githubOptions.IsConfigured;

    public bool IsProvisioningActive =>
        Prototype is not null &&
        Prototype.Status is PrototypeStatus.Requested
            or PrototypeStatus.Queued
            or PrototypeStatus.Provisioning
            or PrototypeStatus.Deleting;

    public bool IsDeploymentActive =>
        IsProvisioningActive ||
        Jobs.Any(job => job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

    public IReadOnlyList<ProvisioningJob> RecentJobs => Jobs.Take(8).ToList();

    public bool CanControlWebApp =>
        Prototype is not null &&
        Prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName) &&
        CanManage;

    public DateOnly MaxAllowedExpiryDate =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(_costControlOptions.MaximumExpiryDays).Date);

    public string MaxAllowedExpiryDateHint =>
        MaxAllowedExpiryDate.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));

    public bool CanViewApp =>
        Prototype is not null &&
        Prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(Prototype.PrototypeUrl);

    public IEnumerable<Prototype> OtherSwitchablePrototypes =>
        SwitchablePrototypes.Where(prototype => prototype.Id != Prototype?.Id);

    public string PrototypeDetailsUrl(Guid id) =>
        Url.Page("/Prototypes/Details", new { id, section = ActiveSection }) ?? string.Empty;

    public static string GetJobTypeLabel(ProvisioningJobType type) => type switch
    {
        ProvisioningJobType.Create => "Provision hosting",
        ProvisioningJobType.UpdatePassword => "Update password",
        ProvisioningJobType.Delete => "Delete hosting",
        ProvisioningJobType.Retry => "Retry provisioning",
        ProvisioningJobType.ConfigureGitHubDeployment => "Configure GitHub deployment",
        ProvisioningJobType.ScaffoldGitHubRepository => "Set up GitHub repository",
        ProvisioningJobType.UpdateAppSettings => "Sync config vars",
        _ => type.ToString()
    };

    public static string FormatActivityActor(string actorEmail) =>
        string.Equals(actorEmail, "System", StringComparison.OrdinalIgnoreCase)
            ? "System"
            : PrototypeContactPresentation.GetDisplayName(actorEmail);

    public static string GetActivityStatusBadgeClass(string status) =>
        Enum.TryParse<ProvisioningJobStatus>(status, out var jobStatus)
            ? GetJobStatusClass(jobStatus)
            : "dfe-c-fe-badge dfe-c-fe-badge--muted";

    public static string GetActivityStatusLabel(string status) =>
        Enum.TryParse<ProvisioningJobStatus>(status, out var jobStatus)
            ? GetJobStatusLabel(jobStatus)
            : "Logged";

    public static string GetActivityItemClass(string status) => status switch
    {
        nameof(ProvisioningJobStatus.Failed) => "activity-feed__item--failed",
        nameof(ProvisioningJobStatus.Running) => "activity-feed__item--running",
        nameof(ProvisioningJobStatus.Queued) => "activity-feed__item--queued",
        nameof(ProvisioningJobStatus.Succeeded) => "activity-feed__item--succeeded",
        _ => "activity-feed__item--recorded"
    };

    public static string GetJobStatusLabel(ProvisioningJobStatus status) => status switch
    {
        ProvisioningJobStatus.Succeeded => "Complete",
        ProvisioningJobStatus.Failed => "Failed",
        ProvisioningJobStatus.Running => "In progress",
        ProvisioningJobStatus.Queued => "Queued",
        _ => status.ToString()
    };

    public static string GetRuntimeBadgeClass(WebAppPowerState state) => state switch
    {
        WebAppPowerState.Running => "dfe-c-fe-badge dfe-c-fe-badge--success",
        WebAppPowerState.Stopped => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        WebAppPowerState.Starting => "dfe-c-fe-badge dfe-c-fe-badge--info",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--warning"
    };

    public static string FormatStackSummary(Prototype prototype) =>
        $"{prototype.Sku}, {GetStackLocation(prototype)}, {GetStackType()}";

    public static string GetStackLocation(Prototype prototype) =>
        string.IsNullOrWhiteSpace(prototype.Region) ? "westeurope" : prototype.Region;

    public static string GetStackType() => "Linux";

    private static DateTimeOffset HostingStaleCutoff => DateTimeOffset.UtcNow.AddMonths(-2);

    public HostingHealthStatus HostingHealth => ResolveHostingHealth();

    public sealed record HostingHealthStatus(string Label, string BadgeClass, string? Detail);

    private HostingHealthStatus ResolveHostingHealth()
    {
        if (Prototype is null)
        {
            return new("Unknown", "dfe-c-fe-badge dfe-c-fe-badge--muted", null);
        }

        return Prototype.Status switch
        {
            PrototypeStatus.Failed => new(
                "Failed",
                "dfe-c-fe-badge dfe-c-fe-badge--danger",
                "Provisioning or hosting has failed."),
            PrototypeStatus.Expired => new(
                "Expired",
                "dfe-c-fe-badge dfe-c-fe-badge--danger",
                "This prototype has passed its expiry date."),
            PrototypeStatus.Archived => new(
                "Archived",
                "dfe-c-fe-badge dfe-c-fe-badge--muted",
                null),
            PrototypeStatus.Deleted => new(
                "Deleted",
                "dfe-c-fe-badge dfe-c-fe-badge--muted",
                null),
            PrototypeStatus.Deleting => new(
                "Deleting",
                "dfe-c-fe-badge dfe-c-fe-badge--warning",
                "Hosting is being removed."),
            PrototypeStatus.Requested
                or PrototypeStatus.Queued
                or PrototypeStatus.Provisioning => new(
                ListModel.GetStatusLabel(Prototype.Status),
                "dfe-c-fe-badge dfe-c-fe-badge--info",
                "Hosting is still being set up."),
            PrototypeStatus.Live => ResolveLiveHostingHealth(),
            _ => new(
                ListModel.GetStatusLabel(Prototype.Status),
                ListModel.GetStatusBadgeClass(Prototype.Status),
                null)
        };
    }

    private HostingHealthStatus ResolveLiveHostingHealth()
    {
        var latestOverallJob = Jobs
            .OrderByDescending(job => job.CompletedAt ?? job.StartedAt ?? job.CreatedAt)
            .FirstOrDefault();

        if (latestOverallJob?.Status == ProvisioningJobStatus.Failed)
        {
            return new(
                "Unhealthy",
                "dfe-c-fe-badge dfe-c-fe-badge--danger",
                "The latest hosting or deployment job failed.");
        }

        if (Jobs.Any(job => job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return new(
                "Updating",
                "dfe-c-fe-badge dfe-c-fe-badge--info",
                "A hosting or deployment job is in progress.");
        }

        var lastDeploymentAt = Jobs
            .Where(job =>
                job.Status == ProvisioningJobStatus.Succeeded &&
                IsDeploymentRelatedJob(job.Type))
            .Select(job => job.CompletedAt ?? job.StartedAt ?? job.CreatedAt)
            .DefaultIfEmpty(Prototype!.CreatedAt)
            .Max();

        if (lastDeploymentAt < HostingStaleCutoff)
        {
            return new(
                "Stale",
                "dfe-c-fe-badge dfe-c-fe-badge--danger",
                "No successful deployments for over 2 months.");
        }

        if (!IsOnSupportedHostingPlan())
        {
            return new(
                "Plan issue",
                "dfe-c-fe-badge dfe-c-fe-badge--warning",
                "This prototype is not on a supported app plan.");
        }

        return new(
            "Healthy",
            "dfe-c-fe-badge dfe-c-fe-badge--success",
            "Supported plan, recent deployment, and no recent failures.");
    }

    private bool IsOnSupportedHostingPlan()
    {
        if (Prototype is null || string.IsNullOrWhiteSpace(Prototype.Sku))
        {
            return false;
        }

        if (_costControlOptions.AllowedSkus.Contains(Prototype.Sku, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return ExceptionRequests.Any(request =>
            request.Status == ExceptionRequestStatus.Approved &&
            string.Equals(request.RequestedSku, Prototype.Sku, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDeploymentRelatedJob(ProvisioningJobType type) => type is
        ProvisioningJobType.Create
        or ProvisioningJobType.Retry
        or ProvisioningJobType.ConfigureGitHubDeployment
        or ProvisioningJobType.ScaffoldGitHubRepository;

    public bool CanManageAppRuntime =>
        CanManage &&
        Prototype is not null &&
        Prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName);

    public bool CanAssignCompass =>
        CanManage && CompassConfigured;

    public bool HasGitHubRepository =>
        Prototype is not null &&
        !string.IsNullOrWhiteSpace(Prototype.GitHubRepositoryUrl);

    public string? GitHubRepositoryDisplayName
    {
        get
        {
            if (!HasGitHubRepository)
            {
                return null;
            }

            var owner = PrototypeGitHubRepositoryUrls.TryGetOwner(Prototype!.GitHubRepositoryUrl);
            var repository = PrototypeGitHubRepositoryUrls.TryGetRepositoryName(Prototype.GitHubRepositoryUrl);
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                return PrototypeGitHubRepositoryUrls.Normalize(Prototype.GitHubRepositoryUrl);
            }

            return $"{owner}/{repository}";
        }
    }

    public string? GitHubRepositoryNormalizedUrl =>
        HasGitHubRepository
            ? PrototypeGitHubRepositoryUrls.Normalize(Prototype!.GitHubRepositoryUrl)
            : null;

    public string? GitHubCloneHttpsCommand =>
        HasGitHubRepository
            ? $"git clone {GitHubRepositoryNormalizedUrl}.git"
            : null;

    public string? GitHubCloneSshCommand
    {
        get
        {
            if (!HasGitHubRepository || string.IsNullOrWhiteSpace(GitHubRepositoryDisplayName))
            {
                return null;
            }

            var owner = PrototypeGitHubRepositoryUrls.TryGetOwner(Prototype!.GitHubRepositoryUrl);
            var repository = PrototypeGitHubRepositoryUrls.TryGetRepositoryName(Prototype.GitHubRepositoryUrl);
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            {
                return null;
            }

            return $"git clone git@github.com:{owner}/{repository}.git";
        }
    }

    public ExceptionRequest? OpenHigherPlanRequest =>
        ExceptionRequests.FirstOrDefault(request => request.Status == ExceptionRequestStatus.Requested);

    public IReadOnlyList<PrototypeHostingPlanOption> AvailableHigherPlans =>
        PrototypeHostingPlans.HigherPlans
            .Where(plan =>
                Prototype is null ||
                !string.Equals(plan.Sku, Prototype.Sku, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public bool CanRequestHigherPlan =>
        CanManage &&
        Prototype is not null &&
        Prototype.Status is not (PrototypeStatus.Archived or PrototypeStatus.Deleted or PrototypeStatus.Deleting) &&
        OpenHigherPlanRequest is null &&
        AvailableHigherPlans.Count > 0;

    public bool ShowRequestHigherPlanModal { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype.DeputyOwnerEmail;
        NewExpiryDate = DateOnly.FromDateTime(Prototype.ExpiresAt.DateTime);

        var progress = provisioningProgressService.Build(Prototype, Jobs, AuditEvents);
        var hasLiveHosting = Prototype.Status == PrototypeStatus.Live
            || (!string.IsNullOrWhiteSpace(Prototype.PrototypeUrl)
                && Prototype.PrototypeUrl.Contains("azurewebsites.net", StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName);

        // Keep users in Setup while creation is actively running. Once hosting exists, allow Manage
        // even if a later setup step failed — they should not need to delete and recreate.
        if (progress.IsActive
            || (provisioningProgressService.ShouldShowProgress(Prototype, Jobs, AuditEvents)
                && !progress.IsComplete
                && !hasLiveHosting))
        {
            return RedirectToPage("/Prototypes/Setup", new { id });
        }

        return Page();
    }

    public IReadOnlyList<AuditEvent> ProvisioningSteps =>
        AuditEvents
            .Where(item => string.Equals(item.Action, ProvisioningActivity.StepAction, StringComparison.Ordinal))
            .OrderBy(item => item.CreatedAt)
            .ToList();

    public string SectionUrl(string section) =>
        Url.Page("/Prototypes/Details", new { id = Prototype?.Id, section }) ?? string.Empty;

    public string ActiveActivityType => PrototypeActivityTypes.Normalize(ActivityType);

    public int ActivityTotalCount =>
        ActiveActivityType == PrototypeActivityTypes.All
            ? ActivityItems.Count
            : ActivityItems.Count(item =>
                string.Equals(item.ActivityType, ActiveActivityType, StringComparison.OrdinalIgnoreCase));

    public int ActivityTotalPages =>
        Math.Max(1, (int)Math.Ceiling(ActivityTotalCount / (double)ActivityPageSize));

    public int CurrentActivityPage =>
        Math.Clamp(ActivityPage <= 0 ? 1 : ActivityPage, 1, ActivityTotalPages);

    public bool HasPreviousActivityPage => CurrentActivityPage > 1;

    public bool HasNextActivityPage => CurrentActivityPage < ActivityTotalPages;

    public int ActivityTypeCount(string type) =>
        string.Equals(type, PrototypeActivityTypes.All, StringComparison.OrdinalIgnoreCase)
            ? ActivityItems.Count
            : ActivityItems.Count(item =>
                string.Equals(item.ActivityType, type, StringComparison.OrdinalIgnoreCase));

    public string ActivityUrl(string? type = null, int? page = null)
    {
        var normalizedType = PrototypeActivityTypes.Normalize(type ?? ActiveActivityType);
        var pageNumber = page ?? 1;

        return Url.Page(
                   "/Prototypes/Details",
                   new
                   {
                       id = Prototype?.Id,
                       section = HistorySection,
                       activityType = normalizedType == PrototypeActivityTypes.All ? null : normalizedType,
                       activityPage = pageNumber <= 1 ? null : (int?)pageNumber
                   })
               ?? string.Empty;
    }

    private void ApplyActivityPaging()
    {
        ActivityType = ActiveActivityType;
        ActivityPage = CurrentActivityPage;

        var filtered = ActiveActivityType == PrototypeActivityTypes.All
            ? ActivityItems
            : ActivityItems
                .Where(item =>
                    string.Equals(item.ActivityType, ActiveActivityType, StringComparison.OrdinalIgnoreCase))
                .ToList();

        PagedActivityItems = filtered
            .Skip((CurrentActivityPage - 1) * ActivityPageSize)
            .Take(ActivityPageSize)
            .ToList();
    }

    public async Task<IActionResult> OnGetPrototypePasswordAsync(Guid id)
    {
        try
        {
            var password = await prototypeService.GetPrototypePasswordAsync(
                id,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            return new JsonResult(new { password = password ?? string.Empty });
        }
        catch (ValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnGetAppSettingsAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return Forbid();
        }

        var settings = await appSettingsService.GetSettingsAsync(
            id,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport());

        var hasPendingSync = settings.Any(setting => setting.IsPendingSync);
        var hasFailedSync = settings.Any(setting =>
            string.Equals(setting.SyncStatus, AppSettingSyncStatuses.Failed, StringComparison.Ordinal));

        return new JsonResult(new
        {
            hasPendingSync,
            hasFailedSync,
            syncComplete = !hasPendingSync && !hasFailedSync,
            settings = settings.Select(setting => new
            {
                key = setting.Key,
                displayValue = setting.IsSecret ? null : setting.Value,
                setting.IsSecret,
                setting.IsPendingSync,
                setting.SyncStatus,
                setting.IsEditable
            })
        });
    }

    public async Task<IActionResult> OnGetAppSettingValueAsync(Guid id, string key)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return Forbid();
        }

        try
        {
            var value = await appSettingsService.GetSettingValueAsync(
                id,
                key,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            if (value is null)
            {
                if (PrototypeAppSettingCatalog.IsPasswordKey(key))
                {
                    var password = await prototypeService.GetPrototypePasswordAsync(
                        id,
                        User.GetEmail(),
                        User.IsAdmin(),
                        User.IsSupport());

                    if (string.IsNullOrWhiteSpace(password))
                    {
                        return NotFound(new { error = "Prototype password is not available." });
                    }

                    return new JsonResult(new { key = PrototypeAppSettingCatalog.PasswordKey, value = password });
                }

                return NotFound(new { error = "App setting not found." });
            }

            return new JsonResult(new { key = PrototypeAppSettingCatalog.NormaliseKey(key), value });
        }
        catch (ValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    public async Task<IActionResult> OnPostUpdateBusinessAreaAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.UpdateBusinessAreaAsync(
                id,
                BusinessAreaId ?? Guid.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Business area updated.";
            return RedirectToPage(new { id, section = PrototypeSection });
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            await LoadPageDataAsync(id);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateCompassWorkItemAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.UpdateCompassWorkItemAsync(
                id,
                SelectedCompassWorkItemId,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = SelectedCompassWorkItemId is null
                ? "COMPASS work item cleared."
                : "COMPASS work item updated.";
            return RedirectToPage(new { id, section = PrototypeSection });
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            await LoadPageDataAsync(id);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateCompassProductAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.UpdateCompassProductAsync(
                id,
                SelectedCompassProductId,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = string.IsNullOrWhiteSpace(SelectedCompassProductId)
                ? "COMPASS product cleared."
                : "COMPASS product updated.";
            return RedirectToPage(new { id, section = PrototypeSection });
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            await LoadPageDataAsync(id);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostExtendExpiryAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.ExtendExpiryAsync(
                id,
                new DateTimeOffset(NewExpiryDate, TimeOnly.MinValue, TimeSpan.Zero),
                User.GetEmail());

            StatusMessage = "Expiry date updated.";
            return RedirectToPage(new { id, section = PrototypeSection });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(NewExpiryDate), exception.Message);
        }

        await LoadPageDataAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = string.Equals(Section, SettingsSection, StringComparison.Ordinal)
            ? SettingsSection
            : PrototypeSection;

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = $"Prototype password must be at least {PrototypePasswordRules.MinimumLength} characters.";
            await LoadPageDataAsync(id);
            return Page();
        }

        try
        {
            PrototypePasswordRules.Validate(NewPassword);
            await prototypeService.ChangePasswordAsync(id, NewPassword, User.GetEmail());
            StatusMessage = "Password change queued.";
            NewPassword = string.Empty;
            return RedirectToPage(new { id, section = Section });
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            await LoadPageDataAsync(id);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRequestHigherPlanAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);
        ExceptionRequests = await prototypeService.GetExceptionRequestsForPrototypeAsync(id);

        if (!CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.RequestHigherPlanAsync(
                id,
                RequestedSku ?? string.Empty,
                ExceptionReason ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Higher plan request submitted for DesignOps review.";
            RequestedSku = string.Empty;
            ExceptionReason = string.Empty;
            return RedirectToPage(new { id, section = PrototypeSection });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(ResolveHigherPlanErrorField(exception.Message), exception.Message);
            ShowRequestHigherPlanModal = true;
            await LoadPageDataAsync(id);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostStartAppAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (Prototype!.Status != PrototypeStatus.Live ||
            string.IsNullOrWhiteSpace(Prototype.AzureWebAppName) ||
            !CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.StartWebAppAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());
            StatusMessage = "App start requested.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage(new { id, section = PrototypeSection });
    }

    public async Task<IActionResult> OnPostStopAppAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Contacts = await prototypeService.GetContactsAsync(id);

        if (Prototype!.Status != PrototypeStatus.Live ||
            string.IsNullOrWhiteSpace(Prototype.AzureWebAppName) ||
            !CanManage)
        {
            return NotFound();
        }

        Section = PrototypeSection;

        try
        {
            await prototypeService.StopWebAppAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());
            StatusMessage = "App stop requested.";
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }

        return RedirectToPage(new { id, section = PrototypeSection });
    }

    public async Task<IActionResult> OnPostRevealPasswordAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        try
        {
            RevealedPassword = await prototypeService.GetPrototypePasswordAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());
            Section = PrototypeSection;
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
            Section = PrototypeSection;
        }

        await LoadPageDataAsync(id);

        return Page();
    }

    public async Task<IActionResult> OnPostAddManagerAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = ContactsSection;

        try
        {
            await prototypeService.AddManagerAsync(
                id,
                ManagerEmail ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Prototype manager added.";
            return RedirectToContacts(id);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(ManagerEmail), exception.Message);
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype!.DeputyOwnerEmail;
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveManagerAsync(Guid id, Guid contactId)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = ContactsSection;

        try
        {
            await prototypeService.RemoveManagerAsync(
                id,
                contactId,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Prototype manager removed.";
            return RedirectToContacts(id);
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype!.DeputyOwnerEmail;
        return Page();
    }

    public async Task<IActionResult> OnPostTransferOwnershipAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = ContactsSection;

        try
        {
            await prototypeService.TransferOwnershipAsync(
                id,
                NewOwnerEmail ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Ownership transferred.";
            return RedirectToContacts(id);
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype!.DeputyOwnerEmail;
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveDeputyAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = ContactsSection;

        try
        {
            await prototypeService.UpdateDeputyOwnerAsync(
                id,
                string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Deputy owner removed.";
            return RedirectToContacts(id);
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype!.DeputyOwnerEmail;
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDeputyOwnerAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = ContactsSection;

        try
        {
            await prototypeService.UpdateDeputyOwnerAsync(
                id,
                DeputyOwnerEmail ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = string.IsNullOrWhiteSpace(DeputyOwnerEmail)
                ? "Deputy owner removed."
                : "Deputy owner updated.";

            return RedirectToContacts(id);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(DeputyOwnerEmail), exception.Message);
        }

        await LoadPageDataAsync(id);
        DeputyOwnerEmail = Prototype!.DeputyOwnerEmail;
        return Page();
    }

    public async Task<IActionResult> OnPostSetConfigVarAsync(Guid id)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = SettingsSection;

        try
        {
            if (PrototypeAppSettingCatalog.IsPasswordKey(ConfigVarKey ?? string.Empty))
            {
                if (string.IsNullOrWhiteSpace(ConfigVarValue))
                {
                    ModelState.AddModelError(nameof(ConfigVarValue), $"Prototype password must be at least {PrototypePasswordRules.MinimumLength} characters.");
                    await LoadPageDataAsync(id);
                    return Page();
                }

                PrototypePasswordRules.Validate(ConfigVarValue);
                await prototypeService.ChangePasswordAsync(id, ConfigVarValue, User.GetEmail());
                StatusMessage = "Password change queued.";
                return RedirectToSettings(id);
            }

            ConfigVarKey = PrototypeAppSettingCatalog.NormaliseKey(ConfigVarKey ?? string.Empty);

            await appSettingsService.SetSettingAsync(
                id,
                ConfigVarKey,
                ConfigVarValue ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Config var saved and queued for sync.";
            return RedirectToSettings(id);
        }
        catch (ValidationException exception)
        {
            ConfigVarKey = PrototypeAppSettingCatalog.NormaliseKey(ConfigVarKey ?? string.Empty);
            ModelState.AddModelError(ResolveConfigVarErrorField(exception.Message), exception.Message);
        }

        await LoadPageDataAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveConfigVarAsync(Guid id, string key)
    {
        if (!await TryLoadPrototypeAsync(id))
        {
            return NotFound();
        }

        Section = SettingsSection;

        if (PrototypeAppSettingCatalog.IsPasswordKey(key))
        {
            StatusMessage = "The prototype password cannot be removed.";
            return RedirectToSettings(id);
        }

        try
        {
            await appSettingsService.RemoveSettingAsync(
                id,
                key,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Config var removed and queued for sync.";
            return RedirectToSettings(id);
        }
        catch (ValidationException exception)
        {
            StatusMessage = exception.Message;
        }

        await LoadPageDataAsync(id);
        return Page();
    }

    private async Task<bool> TryLoadPrototypeAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());
        return Prototype is not null;
    }

    private async Task LoadPageDataAsync(Guid id)
    {
        Jobs = await prototypeService.GetJobsAsync(id);
        AuditEvents = await prototypeService.GetAuditEventsAsync(id);
        Contacts = await prototypeService.GetContactsAsync(id);

        if (PrototypeAccess.CanManagePrototype(
                Prototype!,
                Contacts,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport()))
        {
            AppSettings = await appSettingsService.GetSettingsAsync(
                id,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());
        }

        DeploymentWorkflow = workflowGenerator.Generate(
            Prototype!,
            new DeploymentWorkflowOptions { UsePublishProfile = true });

        if (Prototype!.Status == PrototypeStatus.Live && !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName))
        {
            WebAppRuntime = await webAppRuntimeService.GetStatusAsync(Prototype);
        }

        if (CanManage)
        {
            AvailableBusinessAreas = await accessManagementService.GetVisibleBusinessAreasAsync(
                User.GetEmail(),
                User.IsAdmin() || User.IsSupport());

            CompassConfigured = compassCatalogService.IsConfigured;
            if (CompassConfigured)
            {
                CompassWorkItems = await compassCatalogService.GetActiveWorkItemsAsync();
                CompassProducts = await compassCatalogService.GetProductsAsync();
                SelectedCompassWorkItemId ??= Prototype!.CompassWorkItemId;
                SelectedCompassProductId ??= Prototype.CompassProductId;
            }
        }

        if (CanViewPassword)
        {
            try
            {
                RevealedPassword = await prototypeService.GetPrototypePasswordAsync(
                    id,
                    User.GetEmail(),
                    User.IsAdmin(),
                    User.IsSupport());
            }
            catch (ValidationException)
            {
                RevealedPassword = null;
            }
        }

        ActivityItems = PrototypeActivityFeedBuilder.Build(Jobs, AuditEvents);
        ApplyActivityPaging();
        ExceptionRequests = await prototypeService.GetExceptionRequestsForPrototypeAsync(id);

        SwitchablePrototypes = (await prototypeService.GetVisibleAsync(
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport()))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetStatusClass(PrototypeStatus status) => ListModel.GetStatusBadgeClass(status);

    public static string GetJobStatusClass(ProvisioningJobStatus status) => status switch
    {
        ProvisioningJobStatus.Succeeded => "dfe-c-fe-badge dfe-c-fe-badge--success",
        ProvisioningJobStatus.Failed => "dfe-c-fe-badge dfe-c-fe-badge--danger",
        ProvisioningJobStatus.Running => "dfe-c-fe-badge dfe-c-fe-badge--info",
        ProvisioningJobStatus.Queued => "dfe-c-fe-badge dfe-c-fe-badge--info",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--muted"
    };

    public bool CanTransferOwnership =>
        Contacts.Any() || !string.IsNullOrWhiteSpace(Prototype?.DeputyOwnerEmail);

    public IEnumerable<(string Email, string DisplayName)> OwnershipTransferCandidates
    {
        get
        {
            var candidates = new List<(string Email, string DisplayName)>();

            if (!string.IsNullOrWhiteSpace(Prototype?.DeputyOwnerEmail))
            {
                candidates.Add((
                    Prototype.DeputyOwnerEmail,
                    PrototypeContactPresentation.GetDisplayName(Prototype.DeputyOwnerEmail)));
            }

            foreach (var contact in Contacts)
            {
                if (candidates.Any(item =>
                        string.Equals(item.Email, contact.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                candidates.Add((
                    contact.Email,
                    PrototypeContactPresentation.GetDisplayName(contact.Email)));
            }

            return candidates;
        }
    }

    private RedirectToPageResult RedirectToContacts(Guid id) =>
        RedirectToPage(new { id, section = ContactsSection });

    private RedirectToPageResult RedirectToSettings(Guid id) =>
        RedirectToPage(new { id, section = SettingsSection });

    private static string ResolveConfigVarErrorField(string message)
    {
        if (message.Contains("value", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password must", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("characters or fewer", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("Values", StringComparison.Ordinal))
        {
            return nameof(ConfigVarValue);
        }

        return nameof(ConfigVarKey);
    }

    private static string ResolveHigherPlanErrorField(string message)
    {
        if (message.Contains("40 characters", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Explain why", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("legitimate research", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(ExceptionReason);
        }

        if (message.Contains("available higher plans", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("higher plan than", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Choose one", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(RequestedSku);
        }

        return string.Empty;
    }
}
