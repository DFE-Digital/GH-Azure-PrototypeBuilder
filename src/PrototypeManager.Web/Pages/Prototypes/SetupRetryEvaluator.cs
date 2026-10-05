using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public static class SetupRetryEvaluator
{
    public sealed record RetryOptions(
        bool CanRetryProvisioning,
        bool CanRetryScaffold,
        bool CanRetryGitHubDeployment,
        string? BlockedReason);

    public static RetryOptions Evaluate(
        Prototype prototype,
        ProvisioningProgressSnapshot progress,
        IReadOnlyList<ProvisioningJob> jobs,
        bool canManage,
        bool gitHubTokenConfigured)
    {
        if (!progress.IsFailed || !canManage)
        {
            return new RetryOptions(false, false, false, null);
        }

        var failedTaskId = progress.Tasks
            .FirstOrDefault(task => string.Equals(task.Status, "Failed", StringComparison.Ordinal))
            ?.Id;

        if (failedTaskId is null)
        {
            return new RetryOptions(false, false, false, null);
        }

        if (IsScaffoldFailure(failedTaskId))
        {
            return EvaluateScaffoldRetry(prototype, jobs);
        }

        if (IsHostingFailure(failedTaskId))
        {
            return EvaluateHostingRetry(prototype, jobs);
        }

        if (IsGitHubFailure(failedTaskId))
        {
            return EvaluateGitHubRetry(prototype, jobs, gitHubTokenConfigured, progress);
        }

        return new RetryOptions(false, false, false, null);
    }

    private static RetryOptions EvaluateScaffoldRetry(Prototype prototype, IReadOnlyList<ProvisioningJob> jobs)
    {
        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived or PrototypeStatus.Deleting)
        {
            return new RetryOptions(false, false, false, null);
        }

        if (jobs.Any(job =>
                job.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return new RetryOptions(false, false, false, "GitHub repository setup is already retrying.");
        }

        var scaffoldAttempts = jobs.Count(job => job.Type == ProvisioningJobType.ScaffoldGitHubRepository);

        if (scaffoldAttempts >= 3)
        {
            return new RetryOptions(
                false,
                false,
                false,
                "This prototype has reached the maximum number of GitHub setup attempts. Delete it and create a new one.");
        }

        return new RetryOptions(false, true, false, null);
    }

    private static RetryOptions EvaluateHostingRetry(Prototype prototype, IReadOnlyList<ProvisioningJob> jobs)
    {
        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived or PrototypeStatus.Deleting)
        {
            return new RetryOptions(false, false, false, null);
        }

        if (jobs.Any(job =>
                job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return new RetryOptions(false, false, false, "Setup is already retrying.");
        }

        var latestCreateOrRetry = jobs
            .Where(job => job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry)
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

        if (latestCreateOrRetry?.Status != ProvisioningJobStatus.Failed)
        {
            return new RetryOptions(false, false, false, null);
        }

        var provisioningAttempts = jobs.Count(job =>
            job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry);

        if (provisioningAttempts >= 3)
        {
            return new RetryOptions(
                false,
                false,
                false,
                "This prototype has reached the maximum number of setup attempts. Delete it and create a new one, or contact support.");
        }

        if (string.IsNullOrWhiteSpace(latestCreateOrRetry.SecretReference))
        {
            return new RetryOptions(
                false,
                false,
                false,
                "Setup cannot be retried because the original prototype password is no longer available.");
        }

        return new RetryOptions(true, false, false, null);
    }

    private static RetryOptions EvaluateGitHubRetry(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        bool gitHubTokenConfigured,
        ProvisioningProgressSnapshot progress)
    {
        if (prototype.RequiresRepositoryScaffold &&
            progress.Tasks.Any(task =>
                (string.Equals(task.Id, ProvisioningTasks.GitHubCreateRepository, StringComparison.Ordinal) ||
                 string.Equals(task.Id, ProvisioningTasks.GitHubScaffoldKit, StringComparison.Ordinal)) &&
                !string.Equals(task.Status, ProvisioningTaskState.Completed.ToString(), StringComparison.Ordinal)))
        {
            return EvaluateScaffoldRetry(prototype, jobs);
        }

        if (prototype.Status != PrototypeStatus.Live)
        {
            return new RetryOptions(false, false, false, null);
        }

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            return new RetryOptions(
                false,
                false,
                false,
                "Azure hosting must be ready before GitHub deployment can be retried.");
        }

        if (!gitHubTokenConfigured)
        {
            return new RetryOptions(
                false,
                false,
                false,
                "GitHub deployment cannot be retried until a GitHub access token is configured.");
        }

        if (jobs.Any(job =>
                job.Type == ProvisioningJobType.ConfigureGitHubDeployment &&
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return new RetryOptions(false, false, false, "GitHub deployment is already retrying.");
        }

        return new RetryOptions(false, false, true, null);
    }

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
