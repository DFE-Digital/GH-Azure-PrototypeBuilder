using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public static class SetupRetryEvaluator
{
    public sealed record RetryOptions(
        bool CanRetryProvisioning,
        bool CanRetryScaffold,
        bool CanRetryGitHubDeployment,
        string? BlockedReason,
        string? FailedTaskId);

    public static RetryOptions Evaluate(
        Prototype prototype,
        ProvisioningProgressSnapshot progress,
        IReadOnlyList<ProvisioningJob> jobs,
        bool canManage,
        bool gitHubTokenConfigured)
    {
        if (!progress.IsFailed || !canManage)
        {
            return None();
        }

        var failedTaskId = progress.Tasks
            .FirstOrDefault(task => string.Equals(task.Status, "Failed", StringComparison.Ordinal))
            ?.Id;

        if (failedTaskId is null)
        {
            return None();
        }

        if (IsScaffoldFailure(failedTaskId))
        {
            return EvaluateScaffoldRetry(prototype, jobs, failedTaskId);
        }

        if (IsHostingFailure(failedTaskId))
        {
            return EvaluateHostingRetry(prototype, jobs, failedTaskId);
        }

        if (IsGitHubFailure(failedTaskId))
        {
            return EvaluateGitHubRetry(prototype, jobs, gitHubTokenConfigured, progress, failedTaskId);
        }

        return None();
    }

    public static string GetRetryButtonLabel(string? failedTaskId) => failedTaskId switch
    {
        ProvisioningTasks.GitHubCreateRepository => "Retry creating GitHub repository",
        ProvisioningTasks.GitHubScaffoldKit => "Retry adding Prototype Kit code",
        ProvisioningTasks.Queued => "Retry queuing prototype",
        ProvisioningTasks.AzureResources => "Retry creating Azure hosting",
        ProvisioningTasks.GitHubConfigure => "Retry configuring GitHub deployment",
        ProvisioningTasks.GitHubDeploy => "Retry deploying from GitHub",
        ProvisioningTasks.SiteVerify => "Retry testing your app",
        _ => "Retry this step"
    };

    private static RetryOptions None() => new(false, false, false, null, null);

    private static RetryOptions EvaluateScaffoldRetry(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        string failedTaskId)
    {
        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived or PrototypeStatus.Deleting)
        {
            return None();
        }

        if (jobs.Any(job =>
                job.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return Blocked("GitHub repository setup is already retrying.", failedTaskId);
        }

        var scaffoldAttempts = jobs.Count(job => job.Type == ProvisioningJobType.ScaffoldGitHubRepository);

        if (scaffoldAttempts >= 3)
        {
            return Blocked(
                HasLiveHosting(prototype)
                    ? "This prototype has reached the maximum number of GitHub setup attempts. Manage the existing hosting, or contact support."
                    : "This prototype has reached the maximum number of GitHub setup attempts. Delete it and create a new one.",
                failedTaskId);
        }

        return new RetryOptions(false, true, false, null, failedTaskId);
    }

    private static RetryOptions EvaluateHostingRetry(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        string failedTaskId)
    {
        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived or PrototypeStatus.Deleting)
        {
            return None();
        }

        if (HasLiveHosting(prototype))
        {
            return Blocked(
                "Azure hosting is already available. Open Manage prototype - you do not need to delete and recreate it.",
                failedTaskId);
        }

        if (jobs.Any(job =>
                job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return Blocked("Setup is already retrying.", failedTaskId);
        }

        // Order in memory - SQLite cannot ORDER BY DateTimeOffset.
        var latestCreateOrRetry = jobs
            .Where(job => job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry)
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

        if (latestCreateOrRetry?.Status != ProvisioningJobStatus.Failed)
        {
            return None();
        }

        var provisioningAttempts = jobs.Count(job =>
            job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry);

        if (provisioningAttempts >= 3)
        {
            return Blocked(
                "This prototype has reached the maximum number of setup attempts. Delete it and create a new one, or contact support.",
                failedTaskId);
        }

        if (string.IsNullOrWhiteSpace(latestCreateOrRetry.SecretReference))
        {
            return Blocked(
                "Setup cannot be retried because the original prototype password is no longer available.",
                failedTaskId);
        }

        return new RetryOptions(true, false, false, null, failedTaskId);
    }

    private static bool HasLiveHosting(Prototype prototype) =>
        prototype.Status == PrototypeStatus.Live
        || (!string.IsNullOrWhiteSpace(prototype.PrototypeUrl)
            && prototype.PrototypeUrl.Contains("azurewebsites.net", StringComparison.OrdinalIgnoreCase))
        || !string.IsNullOrWhiteSpace(prototype.AzureWebAppName);

    private static RetryOptions EvaluateGitHubRetry(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        bool gitHubTokenConfigured,
        ProvisioningProgressSnapshot progress,
        string failedTaskId)
    {
        if (prototype.RequiresRepositoryScaffold &&
            progress.Tasks.Any(task =>
                (string.Equals(task.Id, ProvisioningTasks.GitHubCreateRepository, StringComparison.Ordinal) ||
                 string.Equals(task.Id, ProvisioningTasks.GitHubScaffoldKit, StringComparison.Ordinal)) &&
                !string.Equals(task.Status, ProvisioningTaskState.Completed.ToString(), StringComparison.Ordinal)))
        {
            return EvaluateScaffoldRetry(prototype, jobs, failedTaskId);
        }

        if (prototype.Status != PrototypeStatus.Live)
        {
            return None();
        }

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            return Blocked(
                "Azure hosting must be ready before GitHub deployment can be retried.",
                failedTaskId);
        }

        if (!gitHubTokenConfigured)
        {
            return Blocked(
                "GitHub deployment cannot be retried until the GitHub App credentials are configured.",
                failedTaskId);
        }

        if (jobs.Any(job =>
                job.Type == ProvisioningJobType.ConfigureGitHubDeployment &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return Blocked("GitHub deployment is already retrying.", failedTaskId);
        }

        return new RetryOptions(false, false, true, null, failedTaskId);
    }

    private static RetryOptions Blocked(string reason, string failedTaskId) =>
        new(false, false, false, reason, failedTaskId);

    private static bool IsScaffoldFailure(string taskId) =>
        string.Equals(taskId, ProvisioningTasks.GitHubCreateRepository, StringComparison.Ordinal) ||
        string.Equals(taskId, ProvisioningTasks.GitHubScaffoldKit, StringComparison.Ordinal);

    private static bool IsHostingFailure(string taskId) =>
        string.Equals(taskId, ProvisioningTasks.Queued, StringComparison.Ordinal) ||
        string.Equals(taskId, ProvisioningTasks.AzureResources, StringComparison.Ordinal);

    private static bool IsGitHubFailure(string taskId) =>
        string.Equals(taskId, ProvisioningTasks.GitHubConfigure, StringComparison.Ordinal) ||
        string.Equals(taskId, ProvisioningTasks.GitHubDeploy, StringComparison.Ordinal) ||
        string.Equals(taskId, ProvisioningTasks.SiteVerify, StringComparison.Ordinal);
}
