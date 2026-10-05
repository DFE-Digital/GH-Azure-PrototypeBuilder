using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class SetupModel(
    IPrototypeService prototypeService,
    IProvisioningProgressService provisioningProgressService,
    IOptions<GitHubOptions> githubOptions) : PageModel
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    [TempData]
    public string? StatusMessage { get; set; }

    public Prototype? Prototype { get; private set; }
    public ProvisioningProgressSnapshot Progress { get; private set; } = new();
    public IReadOnlyList<ProvisioningJob> Jobs { get; private set; } = [];
    public IReadOnlyList<PrototypeContact> Contacts { get; private set; } = [];
    public SetupRetryEvaluator.RetryOptions Retry { get; private set; } = new(false, false, false, null);

    public bool CanManage =>
        Prototype is not null &&
        PrototypeAccess.CanManagePrototype(
            Prototype,
            Contacts,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport());

    public bool CanDeletePrototype =>
        Prototype is not null &&
        PrototypeAccess.CanDeletePrototype(
            Prototype,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport());

    public bool ShowStatusMessage =>
        !string.IsNullOrWhiteSpace(StatusMessage) &&
        (Progress.IsComplete || Progress.IsFailed);

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        Prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (Prototype is null)
        {
            return NotFound();
        }

        Jobs = await prototypeService.GetJobsAsync(id);
        Contacts = await prototypeService.GetContactsAsync(id);
        var auditEvents = await prototypeService.GetAuditEventsAsync(id);

        if (!provisioningProgressService.ShouldShowProgress(Prototype, Jobs, auditEvents))
        {
            return RedirectToPage("/Prototypes/Details", new { id });
        }

        Progress = provisioningProgressService.Build(Prototype, Jobs, auditEvents);
        Retry = SetupRetryEvaluator.Evaluate(
            Prototype,
            Progress,
            Jobs,
            CanManage,
            !string.IsNullOrWhiteSpace(_githubOptions.PersonalAccessToken));

        return Page();
    }

    public async Task<IActionResult> OnGetProgressAsync(Guid id)
    {
        var prototype = await prototypeService.GetAccessibleAsync(id, User.GetEmail(), User.IsAdmin(), User.IsSupport());

        if (prototype is null)
        {
            return NotFound();
        }

        var jobs = await prototypeService.GetJobsAsync(id);
        var contacts = await prototypeService.GetContactsAsync(id);
        var auditEvents = await prototypeService.GetAuditEventsAsync(id);
        var progress = provisioningProgressService.Build(prototype, jobs, auditEvents);
        var canManage = PrototypeAccess.CanManagePrototype(
            prototype,
            contacts,
            User.GetEmail(),
            User.IsAdmin(),
            User.IsSupport());
        var retry = SetupRetryEvaluator.Evaluate(
            prototype,
            progress,
            jobs,
            canManage,
            !string.IsNullOrWhiteSpace(_githubOptions.PersonalAccessToken));

        return new JsonResult(new
        {
            showProgress = provisioningProgressService.ShouldShowProgress(prototype, jobs, auditEvents),
            prototypeName = prototype.Name,
            prototypeStatus = prototype.Status.ToString(),
            isActive = progress.IsActive,
            isComplete = progress.IsComplete,
            isFailed = progress.IsFailed,
            errorMessage = progress.ErrorMessage,
            prototypeUrl = progress.PrototypeUrl,
            adminUrl = Url.Page("/Prototypes/Details", new { id }),
            canRetryProvisioning = retry.CanRetryProvisioning,
            canRetryScaffold = retry.CanRetryScaffold,
            canRetryGitHubDeployment = retry.CanRetryGitHubDeployment,
            retryBlockedReason = retry.BlockedReason,
            tasks = progress.Tasks.Select(task => new
            {
                id = task.Id,
                label = task.Label,
                description = task.Description,
                status = task.Status
            })
        });
    }

    public static string GetTaskTagClass(string status) => status switch
    {
        "Completed" => "dfe-c-fe-badge dfe-c-fe-badge--success",
        "InProgress" => "dfe-c-fe-badge dfe-c-fe-badge--info",
        "Failed" => "dfe-c-fe-badge dfe-c-fe-badge--danger",
        "Skipped" => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--muted"
    };

    public static string GetSetupStatusBadgeClass(ProvisioningProgressSnapshot progress) =>
        progress.IsFailed
            ? "dfe-c-fe-badge dfe-c-fe-badge--danger"
            : progress.IsComplete
                ? "dfe-c-fe-badge dfe-c-fe-badge--success"
                : progress.IsActive
                    ? "dfe-c-fe-badge dfe-c-fe-badge--info"
                    : "dfe-c-fe-badge dfe-c-fe-badge--muted";

    public static string GetSetupStatusLabel(ProvisioningProgressSnapshot progress) =>
        progress.IsFailed
            ? "Setup failed"
            : progress.IsComplete
                ? "Setup complete"
                : progress.IsActive
                    ? "In progress"
                    : "Waiting to start";

    public static string GetProgressPanelClass(ProvisioningProgressSnapshot progress) =>
        progress.IsFailed
            ? "setup-page__progress-panel--failed"
            : progress.IsComplete
                ? "setup-page__progress-panel--complete"
                : progress.IsActive
                    ? "setup-page__progress-panel--active"
                    : string.Empty;

    public int CompletedTaskCount =>
        Progress.Tasks.Count(task => string.Equals(task.Status, "Completed", StringComparison.Ordinal));

    public int TotalTaskCount => Progress.Tasks.Count;

    public int ProgressPercentage =>
        TotalTaskCount == 0 ? 0 : (int)Math.Round(CompletedTaskCount * 100d / TotalTaskCount);

    public static string GetTaskStatusLabel(string status) => status switch
    {
        "Completed" => "Completed",
        "InProgress" => "In progress",
        "Failed" => "Failed",
        "Skipped" => "Skipped",
        _ => "Not started"
    };
}
