using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class ProvisioningProgressServiceTests
{
    [Fact]
    public void Build_MarksTasksCompleteFromAuditEvents()
    {
        var prototypeId = Guid.NewGuid();
        var prototype = new Prototype
        {
            Id = prototypeId,
            Slug = "example",
            Status = PrototypeStatus.Provisioning,
            PrototypeUrl = "https://example.azurewebsites.net"
        };

        var jobs =
            new List<ProvisioningJob>
            {
                new()
                {
                    PrototypeId = prototypeId,
                    Type = ProvisioningJobType.Create,
                    Status = ProvisioningJobStatus.Running
                }
            };

        var auditEvents = new List<AuditEvent>
        {
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.Queued },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.AzureResources }
        };

        var service = CreateService(autoConfigure: true);
        var progress = service.Build(prototype, jobs, auditEvents);

        Assert.Equal("Completed", progress.Tasks.Single(task => task.Id == ProvisioningTasks.Queued).Status);
        Assert.Equal("Completed", progress.Tasks.Single(task => task.Id == ProvisioningTasks.AzureResources).Status);
        Assert.Equal("Pending", progress.Tasks.Single(task => task.Id == ProvisioningTasks.GitHubConfigure).Status);
    }

    [Fact]
    public void ShouldShowProgress_ReturnsFalseForLegacyPrototypesWithoutTrackedTasks()
    {
        var prototype = new Prototype { Id = Guid.NewGuid(), Status = PrototypeStatus.Live };

        var service = CreateService(autoConfigure: true);

        Assert.False(service.ShouldShowProgress(prototype, [], []));
    }

    [Fact]
    public void Build_RemainsCompleteWhenAppSettingsSyncIsQueued()
    {
        var prototypeId = Guid.NewGuid();
        var prototype = new Prototype
        {
            Id = prototypeId,
            Slug = "example",
            Status = PrototypeStatus.Live,
            PrototypeUrl = "https://example.azurewebsites.net"
        };

        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.UpdateAppSettings,
                Status = ProvisioningJobStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var auditEvents = CompletedSetupAuditEvents(prototypeId);

        var service = CreateService(autoConfigure: true);
        var progress = service.Build(prototype, jobs, auditEvents);

        Assert.True(progress.IsComplete);
        Assert.False(progress.IsActive);
    }

    [Fact]
    public void Build_RemainsCompleteWhenAppSettingsSyncFails()
    {
        var prototypeId = Guid.NewGuid();
        var prototype = new Prototype
        {
            Id = prototypeId,
            Slug = "example",
            Status = PrototypeStatus.Live,
            PrototypeUrl = "https://example.azurewebsites.net",
            AzureWebAppName = "example"
        };

        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Succeeded,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-9)
            },
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.ConfigureGitHubDeployment,
                Status = ProvisioningJobStatus.Succeeded,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-7)
            },
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.UpdateAppSettings,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "Azure sign-in has expired. Run az login, then try again."
            }
        };

        var auditEvents = CompletedSetupAuditEvents(prototypeId);
        var service = CreateService(autoConfigure: true);
        var progress = service.Build(prototype, jobs, auditEvents);

        Assert.True(progress.IsComplete);
        Assert.False(progress.IsFailed);
        Assert.Null(progress.ErrorMessage);
        Assert.All(progress.Tasks, task => Assert.Equal("Completed", task.Status));
        Assert.False(service.ShouldShowProgress(prototype, jobs, auditEvents));
    }

    [Fact]
    public void Build_MarksGitHubFailureWithoutTreatingHostingAsFailed()
    {
        var prototypeId = Guid.NewGuid();
        var prototype = new Prototype
        {
            Id = prototypeId,
            Slug = "example",
            Status = PrototypeStatus.Live,
            PrototypeUrl = "https://example.azurewebsites.net",
            AzureWebAppName = "example"
        };

        var jobs = new List<ProvisioningJob>
        {
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Succeeded,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4)
            },
            new()
            {
                PrototypeId = prototypeId,
                Type = ProvisioningJobType.ConfigureGitHubDeployment,
                Status = ProvisioningJobStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = "GitHub token missing"
            }
        };

        var auditEvents = new List<AuditEvent>
        {
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.Queued },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.AzureResources }
        };

        var service = CreateService(autoConfigure: true);
        var progress = service.Build(prototype, jobs, auditEvents);

        Assert.True(progress.IsFailed);
        Assert.Equal("Failed", progress.Tasks.Single(task => task.Id == ProvisioningTasks.GitHubConfigure).Status);
        Assert.Equal("Completed", progress.Tasks.Single(task => task.Id == ProvisioningTasks.AzureResources).Status);
    }

    private static List<AuditEvent> CompletedSetupAuditEvents(Guid prototypeId) =>
    [
        new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.Queued },
        new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.AzureResources },
        new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.GitHubConfigure },
        new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.GitHubDeploy },
        new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.SiteVerify }
    ];

    private static ProvisioningProgressService CreateService(bool autoConfigure) =>
        new(Options.Create(new GitHubOptions { AutoConfigureDeployment = autoConfigure }));
}
