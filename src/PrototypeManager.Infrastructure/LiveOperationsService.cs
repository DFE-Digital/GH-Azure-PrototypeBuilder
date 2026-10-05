using Microsoft.EntityFrameworkCore;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class LiveOperationsService(
    IPrototypeService prototypeService,
    PrototypeManagerDbContext dbContext) : ILiveOperationsService
{
    private static readonly TimeSpan RecentAuditWindow = TimeSpan.FromHours(24);

    public async Task<LiveOperationsData> GetDataAsync(
        string email,
        bool isAdmin,
        bool isSupport,
        CancellationToken cancellationToken = default)
    {
        var prototypes = await prototypeService.GetVisibleAsync(email, isAdmin, isSupport);

        if (prototypes.Count == 0)
        {
            return new LiveOperationsData
            {
                Prototypes = prototypes,
                JobsByPrototypeId = new Dictionary<Guid, IReadOnlyList<ProvisioningJob>>(),
                AuditEventsByPrototypeId = new Dictionary<Guid, IReadOnlyList<AuditEvent>>()
            };
        }

        var prototypeIds = prototypes.Select(prototype => prototype.Id).ToList();
        var recentCutoff = DateTimeOffset.UtcNow.Subtract(RecentAuditWindow);

        var jobs = await dbContext.ProvisioningJobs.AsNoTracking()
            .Where(job => prototypeIds.Contains(job.PrototypeId))
            .ToListAsync(cancellationToken);

        var auditEvents = await dbContext.AuditEvents.AsNoTracking()
            .Where(item =>
                item.PrototypeId.HasValue &&
                prototypeIds.Contains(item.PrototypeId.Value) &&
                item.CreatedAt >= recentCutoff)
            .ToListAsync(cancellationToken);

        var progressPrototypeIds = jobs
            .Where(job =>
                job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running &&
                job.Type is ProvisioningJobType.Create
                    or ProvisioningJobType.Retry
                    or ProvisioningJobType.ScaffoldGitHubRepository
                    or ProvisioningJobType.ConfigureGitHubDeployment)
            .Select(job => job.PrototypeId)
            .Distinct()
            .ToHashSet();

        var setupAuditEvents = progressPrototypeIds.Count == 0
            ? []
            : await dbContext.AuditEvents.AsNoTracking()
                .Where(item =>
                    item.PrototypeId.HasValue &&
                    progressPrototypeIds.Contains(item.PrototypeId.Value))
                .ToListAsync(cancellationToken);

        var auditByPrototypeId = auditEvents
            .Concat(setupAuditEvents)
            .GroupBy(item => item.PrototypeId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AuditEvent>)group
                    .DistinctBy(item => item.Id)
                    .OrderByDescending(item => item.CreatedAt)
                    .ToList());

        return new LiveOperationsData
        {
            Prototypes = prototypes,
            JobsByPrototypeId = jobs
                .GroupBy(job => job.PrototypeId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<ProvisioningJob>)group
                        .OrderByDescending(job => job.CreatedAt)
                        .ToList()),
            AuditEventsByPrototypeId = auditByPrototypeId
        };
    }
}
