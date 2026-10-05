using PrototypeManager.Domain;
using PrototypeManager.Web.Pages.Prototypes;

namespace PrototypeManager.Tests;

public sealed class SetupRetryEvaluatorTests
{
    [Fact]
    public void Evaluate_AllowsHostingRetryWhenFailedAndEligible()
    {
        var prototype = CreatePrototype(PrototypeStatus.Failed);
        var progress = FailedProgress(ProvisioningTasks.AzureResources);
        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Failed,
                SecretReference = "secret-ref",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.True(result.CanRetryProvisioning);
        Assert.False(result.CanRetryGitHubDeployment);
        Assert.Null(result.BlockedReason);
    }

    [Fact]
    public void Evaluate_BlocksHostingRetryAfterMaximumAttempts()
    {
        var prototype = CreatePrototype(PrototypeStatus.Failed);
        var progress = FailedProgress(ProvisioningTasks.AzureResources);
        var jobs = Enumerable.Range(0, 3)
            .Select(_ => new ProvisioningJob
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Failed,
                SecretReference = "secret-ref",
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.False(result.CanRetryProvisioning);
        Assert.Contains("maximum number of setup attempts", result.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_AllowsGitHubRetryWhenHostingSucceeded()
    {
        var prototype = CreatePrototype(PrototypeStatus.Live);
        prototype.AzureWebAppName = "app-name";
        prototype.AzureResourceGroupName = "rg-name";

        var progress = FailedProgress(ProvisioningTasks.GitHubConfigure);
        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.ConfigureGitHubDeployment,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.False(result.CanRetryProvisioning);
        Assert.True(result.CanRetryGitHubDeployment);
    }

    [Fact]
    public void Evaluate_AllowsScaffoldRetryWhenRepositorySetupFailed()
    {
        var prototype = CreatePrototype(PrototypeStatus.Live);
        prototype.RequiresRepositoryScaffold = true;

        var progress = FailedProgress(ProvisioningTasks.GitHubCreateRepository);
        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.ScaffoldGitHubRepository,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.False(result.CanRetryProvisioning);
        Assert.True(result.CanRetryScaffold);
        Assert.False(result.CanRetryGitHubDeployment);
    }

    [Fact]
    public void Evaluate_OffersScaffoldRetryWhenGitHubDeployFailsBeforeRepositoryExists()
    {
        var prototype = CreatePrototype(PrototypeStatus.Live);
        prototype.RequiresRepositoryScaffold = true;
        prototype.AzureWebAppName = "app-name";
        prototype.AzureResourceGroupName = "rg-name";

        var progress = new ProvisioningProgressSnapshot
        {
            IsFailed = true,
            Tasks =
            [
                new ProvisioningTaskProgressItem
                {
                    Id = ProvisioningTasks.GitHubCreateRepository,
                    Label = "Create GitHub repository",
                    Status = ProvisioningTaskState.Failed.ToString()
                },
                new ProvisioningTaskProgressItem
                {
                    Id = ProvisioningTasks.GitHubConfigure,
                    Label = "Configure GitHub deployment",
                    Status = ProvisioningTaskState.Failed.ToString()
                }
            ]
        };

        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.ScaffoldGitHubRepository,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.ConfigureGitHubDeployment,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.True(result.CanRetryScaffold);
        Assert.False(result.CanRetryGitHubDeployment);
    }

    [Fact]
    public void Evaluate_AllowsHostingRetryWhenPrototypeIsLiveButCreateJobFailed()
    {
        var prototype = CreatePrototype(PrototypeStatus.Live);
        prototype.PrototypeUrl = "https://example.azurewebsites.net";
        var progress = FailedProgress(ProvisioningTasks.AzureResources);
        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Failed,
                SecretReference = "secret-ref",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var result = SetupRetryEvaluator.Evaluate(prototype, progress, jobs, canManage: true, gitHubTokenConfigured: true);

        Assert.True(result.CanRetryProvisioning);
    }

    private static Prototype CreatePrototype(PrototypeStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "test-prototype",
        Slug = "test-prototype",
        OwnerEmail = "owner@education.gov.uk",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ProvisioningProgressSnapshot FailedProgress(string failedTaskId) => new()
    {
        IsFailed = true,
        Tasks =
        [
            new ProvisioningTaskProgressItem
            {
                Id = failedTaskId,
                Label = "Failed task",
                Status = ProvisioningTaskState.Failed.ToString()
            }
        ]
    };
}
