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
    public IReadOnlyList<PrototypeActivityItem> ActivityItems { get; private set; } = [];
    public IReadOnlyList<Prototype> SwitchablePrototypes { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

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
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfigVarKey { get; set; }

    [BindProperty]
    public string? ConfigVarValue { get; set; }

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
        PrototypeSection => "Hosting status, access details, and quick actions for this prototype.",
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

    public bool GitHubTokenConfigured => !string.IsNullOrWhiteSpace(_githubOptions.PersonalAccessToken);

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

    public bool CanManageAppRuntime =>
        CanManage &&
        Prototype is not null &&
        Prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(Prototype.AzureWebAppName);

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
        if (provisioningProgressService.ShouldShowProgress(Prototype, Jobs, AuditEvents) && !progress.IsComplete)
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

        return new JsonResult(new
        {
            settings = settings.Select(setting => new
            {
                key = setting.Key,
                displayValue = setting.IsSecret ? null : setting.Value,
                setting.IsSecret,
                setting.IsPendingSync,
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

            await appSettingsService.SetSettingAsync(
                id,
                ConfigVarKey ?? string.Empty,
                ConfigVarValue ?? string.Empty,
                User.GetEmail(),
                User.IsAdmin(),
                User.IsSupport());

            StatusMessage = "Config var saved and queued for sync.";
            return RedirectToSettings(id);
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(string.IsNullOrWhiteSpace(ConfigVarKey) ? nameof(ConfigVarKey) : nameof(ConfigVarValue), exception.Message);
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
}
