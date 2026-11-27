using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class ProvisioningProgressService(IOptions<GitHubOptions> githubOptions) : IProvisioningProgressService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    public ProvisioningProgressSnapshot Build(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        IReadOnlyList<AuditEvent> auditEvents)
    {
        var completedTasks = auditEvents
            .Where(item => string.Equals(item.Action, ProvisioningTasks.CompletedAction, StringComparison.Ordinal))
            .Select(item => item.Details.Trim())
            .Where(details => !string.IsNullOrWhiteSpace(details))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var hasActiveSetupJob = jobs.Any(job =>
            IsSetupJob(job.Type) &&
            job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

        var applicableTasks = ResolveApplicableTasks(prototype, jobs, completedTasks);
        var failedTaskId = ResolveFailedTaskId(jobs, completedTasks);
        var failedSetupJob = jobs
            .Where(job => IsSetupJob(job.Type) && job.Status == ProvisioningJobStatus.Failed)
            .OrderByDescending(job => job.CompletedAt ?? job.CreatedAt)
            .FirstOrDefault();

        var tasks = BuildTaskItems(prototype, applicableTasks, completedTasks, failedTaskId, jobs, hasActiveSetupJob);
        var allTasksCompleted = applicableTasks.All(task => completedTasks.Contains(task.Id));
        var isComplete = allTasksCompleted
            && tasks.All(task => !string.Equals(task.Status, ProvisioningTaskState.Failed.ToString(), StringComparison.Ordinal));
        // Non-setup jobs (app settings, password) must not reopen setup. Only setup job failures or Failed status during incomplete setup count.
        var isFailed = failedTaskId is not null
            || (prototype.Status == PrototypeStatus.Failed && !allTasksCompleted);

        return new ProvisioningProgressSnapshot
        {
            IsActive = hasActiveSetupJob,
            IsComplete = isComplete && !hasActiveSetupJob && !isFailed,
            IsFailed = isFailed,
            ErrorMessage = failedSetupJob?.ErrorMessage,
            PrototypeUrl = prototype.PrototypeUrl,
            Tasks = tasks
        };
    }

    public bool ShouldShowProgress(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        IReadOnlyList<AuditEvent> auditEvents)
    {
        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived)
        {
            return false;
        }

        var hasTrackedTasks = auditEvents.Any(item =>
            string.Equals(item.Action, ProvisioningTasks.CompletedAction, StringComparison.Ordinal));

        if (!hasTrackedTasks)
        {
            return false;
        }

        var progress = Build(prototype, jobs, auditEvents);
        if (progress.Tasks.Count == 0)
        {
            return false;
        }

        // Keep users on Details once setup finished; later operational jobs must not reopen Setup.
        return !progress.IsComplete;
    }

    private IReadOnlyList<ProvisioningTaskDefinition> ResolveApplicableTasks(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        HashSet<string> completedTasks)
    {
        if (_githubOptions.AutoConfigureDeployment)
        {
            return prototype.RequiresRepositoryScaffold
                ? ProvisioningTasks.ScaffoldSetupFlow
                : ProvisioningTasks.FullSetupFlow;
        }

        var hostingComplete = completedTasks.Contains(ProvisioningTasks.AzureResources);
        var hasGitHubJob = jobs.Any(job => job.Type == ProvisioningJobType.ConfigureGitHubDeployment);

        if (!hostingComplete || !hasGitHubJob)
        {
            return ProvisioningTasks.HostingOnlyFlow;
        }

        return
        [
            ..ProvisioningTasks.HostingOnlyFlow,
            ..ProvisioningTasks.GitHubSetupFlow
        ];
    }

    private static string? ResolveFailedTaskId(
        IReadOnlyList<ProvisioningJob> jobs,
        HashSet<string> completedTasks)
    {
        // Only the latest setup job matters. Operational jobs (app settings, password) are ignored.
        var latestSetupJob = jobs
            .Where(job => IsSetupJob(job.Type))
            .OrderByDescending(job => job.CompletedAt ?? job.CreatedAt)
            .FirstOrDefault();

        if (latestSetupJob?.Status != ProvisioningJobStatus.Failed)
        {
            return null;
        }

        return latestSetupJob.Type switch
        {
            ProvisioningJobType.ScaffoldGitHubRepository =>
                completedTasks.Contains(ProvisioningTasks.GitHubCreateRepository)
                    ? ProvisioningTasks.GitHubScaffoldKit
                    : ProvisioningTasks.GitHubCreateRepository,
            ProvisioningJobType.Create or ProvisioningJobType.Retry =>
                completedTasks.Contains(ProvisioningTasks.Queued)
                    ? ProvisioningTasks.AzureResources
                    : ProvisioningTasks.Queued,
            ProvisioningJobType.ConfigureGitHubDeployment => ResolveGitHubFailedTask(completedTasks),
            _ => null
        };
    }

    private static string ResolveGitHubFailedTask(HashSet<string> completedTasks)
    {
        if (!completedTasks.Contains(ProvisioningTasks.GitHubConfigure))
        {
            return ProvisioningTasks.GitHubConfigure;
        }

        if (!completedTasks.Contains(ProvisioningTasks.GitHubDeploy))
        {
            return ProvisioningTasks.GitHubDeploy;
        }

        return ProvisioningTasks.SiteVerify;
    }

    private static IReadOnlyList<ProvisioningTaskProgressItem> BuildTaskItems(
        Prototype prototype,
        IReadOnlyList<ProvisioningTaskDefinition> applicableTasks,
        HashSet<string> completedTasks,
        string? failedTaskId,
        IReadOnlyList<ProvisioningJob> jobs,
        bool hasActiveJob)
    {
        var items = new List<ProvisioningTaskProgressItem>();
        var inProgressAssigned = false;

        foreach (var task in applicableTasks)
        {
            string status;

            if (completedTasks.Contains(task.Id))
            {
                status = ProvisioningTaskState.Completed.ToString();
            }
            else if (string.Equals(task.Id, failedTaskId, StringComparison.OrdinalIgnoreCase))
            {
                status = ProvisioningTaskState.Failed.ToString();
            }
            else if (!inProgressAssigned && hasActiveJob && ShouldMarkInProgress(prototype, task.Id, completedTasks, jobs))
            {
                status = ProvisioningTaskState.InProgress.ToString();
                inProgressAssigned = true;
            }
            else
            {
                status = ProvisioningTaskState.Pending.ToString();
            }

            items.Add(new ProvisioningTaskProgressItem
            {
                Id = task.Id,
                Label = task.Label,
                Description = task.Description,
                Status = status
            });
        }

        return items;
    }

    private static bool ShouldMarkInProgress(
        Prototype prototype,
        string taskId,
        HashSet<string> completedTasks,
        IReadOnlyList<ProvisioningJob> jobs)
    {
        var createRunning = jobs.Any(job =>
            job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry &&
            job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

        var scaffoldRunning = jobs.Any(job =>
            job.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
            job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

        var scaffoldSucceeded = jobs.Any(job =>
            job.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
            job.Status == ProvisioningJobStatus.Succeeded);

        var githubRunning = jobs.Any(job =>
            job.Type == ProvisioningJobType.ConfigureGitHubDeployment &&
            job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

        return taskId switch
        {
            ProvisioningTasks.Queued => (createRunning || scaffoldRunning) && !completedTasks.Contains(ProvisioningTasks.Queued),
            ProvisioningTasks.GitHubCreateRepository => scaffoldRunning && !completedTasks.Contains(ProvisioningTasks.GitHubCreateRepository),
            ProvisioningTasks.GitHubScaffoldKit => scaffoldRunning &&
                completedTasks.Contains(ProvisioningTasks.GitHubCreateRepository) &&
                !completedTasks.Contains(ProvisioningTasks.GitHubScaffoldKit),
            ProvisioningTasks.AzureResources => jobs.Any(job =>
                job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry &&
                job.Status == ProvisioningJobStatus.Running) &&
                (!prototype.RequiresRepositoryScaffold || scaffoldSucceeded),
            ProvisioningTasks.GitHubConfigure => githubRunning && !completedTasks.Contains(ProvisioningTasks.GitHubConfigure),
            ProvisioningTasks.GitHubDeploy => githubRunning &&
                completedTasks.Contains(ProvisioningTasks.GitHubConfigure) &&
                !completedTasks.Contains(ProvisioningTasks.GitHubDeploy),
            ProvisioningTasks.SiteVerify => githubRunning &&
                completedTasks.Contains(ProvisioningTasks.GitHubDeploy) &&
                !completedTasks.Contains(ProvisioningTasks.SiteVerify),
            _ => false
        };
    }

    private static bool IsSetupJob(ProvisioningJobType type) =>
        type is ProvisioningJobType.Create
            or ProvisioningJobType.Retry
            or ProvisioningJobType.ScaffoldGitHubRepository
            or ProvisioningJobType.ConfigureGitHubDeployment;
}
