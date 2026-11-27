using Microsoft.AspNetCore.Mvc.RazorPages;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Admin;

public sealed class IndexModel(
    IPrototypeService prototypeService,
    IAccessManagementService accessManagementService) : PageModel
{
    public IReadOnlyList<Prototype> AllPrototypes { get; private set; } = [];
    public IReadOnlyList<BusinessAreaSummary> BusinessAreas { get; private set; } = [];
    public IReadOnlyList<AppUserAccessRecord> Users { get; private set; } = [];

    public int BusinessAreaCount => BusinessAreas.Count;
    public int TotalPrototypeCount { get; private set; }
    public int TotalUserCount => Users.Count;
    public int ActiveDeploymentCount { get; private set; }
    public int StaleDeploymentCount { get; private set; }
    public int FailedDeploymentCount { get; private set; }
    public int OpenExceptionRequestCount { get; private set; }

    public async Task OnGetAsync()
    {
        AllPrototypes = await prototypeService.GetAllAsync();
        BusinessAreas = await accessManagementService.GetVisibleBusinessAreaSummariesAsync(User.GetEmail(), true);
        Users = await accessManagementService.GetUsersAsync();
        OpenExceptionRequestCount = (await prototypeService.GetExceptionRequestsAsync())
            .Count(request => request.Status == ExceptionRequestStatus.Requested);

        var prototypes = AllPrototypes
            .Where(prototype => prototype.Status != PrototypeStatus.Deleted)
            .ToList();
        TotalPrototypeCount = prototypes.Count;

        var jobsByPrototype = (await prototypeService.GetAllJobsAsync())
            .GroupBy(job => job.PrototypeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ProvisioningJob>)group.ToList());

        foreach (var prototype in prototypes)
        {
            jobsByPrototype.TryGetValue(prototype.Id, out var jobs);
            jobs ??= [];

            switch (ClassifyDeployment(prototype, jobs))
            {
                case DeploymentHealth.Failed:
                    FailedDeploymentCount++;
                    break;
                case DeploymentHealth.Stale:
                    StaleDeploymentCount++;
                    break;
                case DeploymentHealth.Active:
                    ActiveDeploymentCount++;
                    break;
            }
        }
    }

    private enum DeploymentHealth
    {
        Active,
        Stale,
        Failed,
        Other
    }

    private static DeploymentHealth ClassifyDeployment(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs)
    {
        if (prototype.Status == PrototypeStatus.Failed)
        {
            return DeploymentHealth.Failed;
        }

        var latestJob = jobs
            .OrderByDescending(job => job.CompletedAt ?? job.StartedAt ?? job.CreatedAt)
            .FirstOrDefault();

        if (latestJob?.Status == ProvisioningJobStatus.Failed)
        {
            return DeploymentHealth.Failed;
        }

        if (prototype.Status is PrototypeStatus.Requested
            or PrototypeStatus.Queued
            or PrototypeStatus.Provisioning
            or PrototypeStatus.Deleting)
        {
            return DeploymentHealth.Active;
        }

        if (prototype.Status != PrototypeStatus.Live)
        {
            return DeploymentHealth.Other;
        }

        if (jobs.Any(job => job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running))
        {
            return DeploymentHealth.Active;
        }

        var lastDeploymentAt = jobs
            .Where(job =>
                job.Status == ProvisioningJobStatus.Succeeded &&
                IsDeploymentRelatedJob(job.Type))
            .Select(job => job.CompletedAt ?? job.StartedAt ?? job.CreatedAt)
            .DefaultIfEmpty(prototype.CreatedAt)
            .Max();

        return lastDeploymentAt < DateTimeOffset.UtcNow.AddMonths(-2)
            ? DeploymentHealth.Stale
            : DeploymentHealth.Active;
    }

    private static bool IsDeploymentRelatedJob(ProvisioningJobType type) => type is
        ProvisioningJobType.Create
        or ProvisioningJobType.Retry
        or ProvisioningJobType.ConfigureGitHubDeployment
        or ProvisioningJobType.ScaffoldGitHubRepository;
}
