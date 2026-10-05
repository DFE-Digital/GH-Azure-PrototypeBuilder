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

        var auditEvents = new List<AuditEvent>
        {
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.Queued },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.AzureResources },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.GitHubConfigure },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.GitHubDeploy },
            new() { PrototypeId = prototypeId, Action = ProvisioningTasks.CompletedAction, Details = ProvisioningTasks.SiteVerify }
        };

        var service = CreateService(autoConfigure: true);
        var progress = service.Build(prototype, jobs, auditEvents);

        Assert.True(progress.IsComplete);
        Assert.False(progress.IsActive);
    }

    private static ProvisioningProgressService CreateService(bool autoConfigure) =>
        new(Options.Create(new GitHubOptions { AutoConfigureDeployment = autoConfigure }));
}
