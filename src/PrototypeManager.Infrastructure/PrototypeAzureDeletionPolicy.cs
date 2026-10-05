using Microsoft.EntityFrameworkCore;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal interface IPrototypeAzureDeletionPolicy
{
    Task<bool> ShouldDeleteAzureResourcesAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

internal sealed class PrototypeAzureDeletionPolicy(PrototypeManagerDbContext dbContext) : IPrototypeAzureDeletionPolicy
{
    public async Task<bool> ShouldDeleteAzureResourcesAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) ||
            string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return await dbContext.ProvisioningJobs.AnyAsync(
            job => job.PrototypeId == prototype.Id &&
                   (job.Type == ProvisioningJobType.Create || job.Type == ProvisioningJobType.Retry) &&
                   job.Status == ProvisioningJobStatus.Succeeded,
            cancellationToken);
    }
}
