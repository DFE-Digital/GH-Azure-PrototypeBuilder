using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class ProvisioningRollbackService(
    IAuditService auditService,
    ILogger<ProvisioningRollbackService> logger)
{
    public async Task RollbackSetupAsync(
        Prototype prototype,
        IPrototypeProvisioner provisioner,
        ProvisioningJob failedJob,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            try
            {
                await provisioner.DeletePrototypeAsync(prototype, cancellationToken);
                await ProvisioningActivityRecorder.RecordStepAsync(
                    auditService,
                    logger,
                    prototype.Id,
                    prototype.Slug,
                    $"Deleted Azure resource group {prototype.AzureResourceGroupName} during setup rollback.");
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete Azure resources while rolling back prototype {PrototypeId}.",
                    prototype.Id);
            }
        }

        prototype.AzureResourceGroupName = string.Empty;
        prototype.AzureAppServicePlanName = string.Empty;
        prototype.AzureWebAppName = string.Empty;
        prototype.PrototypeUrl = string.Empty;
        prototype.Status = PrototypeStatus.Failed;

        await auditService.RecordAsync(
            prototype.Id,
            "system",
            "Setup rolled back",
            $"{failedJob.Type} failed and Azure hosting was removed. {reason}");
    }

    public static async Task CancelQueuedJobsAsync(
        PrototypeManagerDbContext dbContext,
        Guid prototypeId,
        CancellationToken cancellationToken)
    {
        var queuedJobs = await dbContext.ProvisioningJobs
            .Where(job =>
                job.PrototypeId == prototypeId &&
                job.Status == ProvisioningJobStatus.Queued)
            .ToListAsync(cancellationToken);

        foreach (var queuedJob in queuedJobs)
        {
            queuedJob.Status = ProvisioningJobStatus.Failed;
            queuedJob.CompletedAt = DateTimeOffset.UtcNow;
            queuedJob.ErrorMessage = "Cancelled because prototype setup failed.";
        }
    }
}
