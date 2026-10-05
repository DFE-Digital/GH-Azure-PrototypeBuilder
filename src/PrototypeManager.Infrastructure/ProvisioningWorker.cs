using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Azure.Identity;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class ProvisioningWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GitHubOptions> githubOptions,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Provisioning worker encountered an unexpected error.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessNextJobAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrototypeManagerDbContext>();
        var provisioner = scope.ServiceProvider.GetRequiredService<IPrototypeProvisioner>();
        var githubDeploymentService = scope.ServiceProvider.GetRequiredService<IGitHubDeploymentService>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var secretStore = scope.ServiceProvider.GetRequiredService<IJobSecretStore>();
        var rollbackService = scope.ServiceProvider.GetRequiredService<ProvisioningRollbackService>();
        var appSettingsService = scope.ServiceProvider.GetRequiredService<IPrototypeAppSettingsService>();
        var githubRepositoryScaffoldService = scope.ServiceProvider.GetRequiredService<IGitHubRepositoryScaffoldService>();
        var githubRepositoryDeletionService = scope.ServiceProvider.GetRequiredService<IGitHubRepositoryDeletionService>();

        var queuedJobs = await dbContext.ProvisioningJobs
            .Include(x => x.Prototype)
            .Where(x => x.Status == ProvisioningJobStatus.Queued)
            .ToListAsync(cancellationToken);

        var job = SelectNextJob(queuedJobs);

        if (job?.Prototype is null)
        {
            return;
        }

        job.Status = ProvisioningJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.AttemptCount = Math.Max(job.AttemptCount, 0) + 1;

        if (job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry)
        {
            job.Prototype.Status = PrototypeStatus.Provisioning;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var prototype = job.Prototype;
        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            job.PrototypeId,
            prototype.Slug,
            $"Running {job.Type} job (attempt {job.AttemptCount}).");

        try
        {
            var successDetails = await ExecuteJobAsync(
                job,
                provisioner,
                githubDeploymentService,
                githubRepositoryScaffoldService,
                githubRepositoryDeletionService,
                appSettingsService,
                secretStore,
                dbContext,
                cancellationToken);

            job.Status = ProvisioningJobStatus.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = null;

            await dbContext.SaveChangesAsync(cancellationToken);

            if (job.Type is ProvisioningJobType.Create or ProvisioningJobType.Retry)
            {
                await ProvisioningTaskRecorder.RecordCompletedAsync(
                    auditService,
                    logger,
                    prototype.Id,
                    prototype.Slug,
                    ProvisioningTasks.AzureResources);

                await QueueGitHubDeploymentIfNeededAsync(dbContext, auditService, prototype, cancellationToken);
            }
            else if (job.Type == ProvisioningJobType.ScaffoldGitHubRepository)
            {
                await QueueHostingAfterScaffoldAsync(dbContext, secretStore, job, cancellationToken);

                if (prototype.Status == PrototypeStatus.Live)
                {
                    await QueueGitHubDeploymentIfNeededAsync(dbContext, auditService, prototype, cancellationToken);
                }
            }
            else if (job.Type is ProvisioningJobType.ConfigureGitHubDeployment)
            {
                await RemovePrototypeSecretsAsync(dbContext, secretStore, prototype.Id, cancellationToken);
            }
            else if (job.Type == ProvisioningJobType.UpdatePassword &&
                     !string.IsNullOrWhiteSpace(job.SecretReference))
            {
                await secretStore.RemoveAsync(job.SecretReference);
            }

            await auditService.RecordAsync(
                job.PrototypeId,
                "system",
                $"{job.Type} succeeded",
                successDetails ?? $"Provisioning job {job.Type} completed successfully.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Provisioning job {JobId} failed.", job.Id);

            job.Status = ProvisioningJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = GetUserFacingErrorMessage(exception);

            if (ShouldRollbackSetup(job.Type))
            {
                await ProvisioningRollbackService.CancelQueuedJobsAsync(dbContext, prototype.Id, cancellationToken);
                await rollbackService.RollbackSetupAsync(
                    prototype,
                    provisioner,
                    job,
                    job.ErrorMessage,
                    cancellationToken);
            }
            else if (job.Type is ProvisioningJobType.Delete)
            {
                var deletionPolicy = scope.ServiceProvider.GetRequiredService<IPrototypeAzureDeletionPolicy>();
                prototype.Status = await deletionPolicy.ShouldDeleteAzureResourcesAsync(prototype, cancellationToken)
                    ? PrototypeStatus.Live
                    : PrototypeStatus.Failed;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.RecordAsync(job.PrototypeId, "system", $"{job.Type} failed", job.ErrorMessage);
        }
    }

    private static string GetUserFacingErrorMessage(Exception exception) =>
        exception switch
        {
            CredentialUnavailableException =>
                "Azure sign-in has expired. Run az login, then delete the prototype again.",
            _ when exception.Message.Contains("DefaultAzureCredential failed", StringComparison.Ordinal) =>
                "Azure sign-in has expired. Run az login, then delete the prototype again.",
            _ => exception.Message
        };

    private static bool ShouldRollbackSetup(ProvisioningJobType jobType) =>
        jobType is ProvisioningJobType.Create
            or ProvisioningJobType.Retry
            or ProvisioningJobType.ConfigureGitHubDeployment
            or ProvisioningJobType.ScaffoldGitHubRepository;

    private static ProvisioningJob? SelectNextJob(IReadOnlyList<ProvisioningJob> queuedJobs)
    {
        var runnableJobs = queuedJobs
            .Where(job => !IsBlockedByPendingScaffold(job, queuedJobs))
            .OrderBy(job => job.CreatedAt)
            .ToList();

        return runnableJobs.FirstOrDefault();
    }

    private static bool IsBlockedByPendingScaffold(ProvisioningJob job, IReadOnlyList<ProvisioningJob> queuedJobs)
    {
        if (job.Type is not (ProvisioningJobType.Create or ProvisioningJobType.Retry))
        {
            return false;
        }

        if (job.Prototype?.RequiresRepositoryScaffold != true)
        {
            return false;
        }

        return queuedJobs.Any(otherJob =>
            otherJob.PrototypeId == job.PrototypeId &&
            otherJob.Type == ProvisioningJobType.ScaffoldGitHubRepository);
    }

    private static async Task QueueHostingAfterScaffoldAsync(
        PrototypeManagerDbContext dbContext,
        IJobSecretStore secretStore,
        ProvisioningJob scaffoldJob,
        CancellationToken cancellationToken)
    {
        var hostingAlreadyQueued = await dbContext.ProvisioningJobs.AnyAsync(
            job => job.PrototypeId == scaffoldJob.PrototypeId &&
                   (job.Type == ProvisioningJobType.Create || job.Type == ProvisioningJobType.Retry) &&
                   (job.Status == ProvisioningJobStatus.Queued ||
                    job.Status == ProvisioningJobStatus.Running ||
                    job.Status == ProvisioningJobStatus.Succeeded),
            cancellationToken);

        if (hostingAlreadyQueued)
        {
            return;
        }

        await RequireSecretAsync(secretStore, dbContext, scaffoldJob.SecretReference);

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = scaffoldJob.PrototypeId,
            Type = ProvisioningJobType.Create,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            SecretReference = scaffoldJob.SecretReference
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task QueueGitHubDeploymentIfNeededAsync(
        PrototypeManagerDbContext dbContext,
        IAuditService auditService,
        Prototype prototype,
        CancellationToken cancellationToken)
    {
        if (!_githubOptions.AutoConfigureDeployment || prototype.Status != PrototypeStatus.Live)
        {
            return;
        }

        var alreadyConfigured = await dbContext.ProvisioningJobs.AnyAsync(
            x => x.PrototypeId == prototype.Id &&
                 x.Type == ProvisioningJobType.ConfigureGitHubDeployment &&
                 (x.Status == ProvisioningJobStatus.Queued ||
                  x.Status == ProvisioningJobStatus.Running ||
                  x.Status == ProvisioningJobStatus.Succeeded),
            cancellationToken);

        if (alreadyConfigured)
        {
            return;
        }

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.ConfigureGitHubDeployment,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototype.Id,
            prototype.Slug,
            "Queued GitHub deployment configuration.");
    }

    private static async Task<string?> ExecuteJobAsync(
        ProvisioningJob job,
        IPrototypeProvisioner provisioner,
        IGitHubDeploymentService githubDeploymentService,
        IGitHubRepositoryScaffoldService githubRepositoryScaffoldService,
        IGitHubRepositoryDeletionService githubRepositoryDeletionService,
        IPrototypeAppSettingsService appSettingsService,
        IJobSecretStore secretStore,
        PrototypeManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var prototype = job.Prototype ?? throw new InvalidOperationException("Provisioning job prototype was not loaded.");

        switch (job.Type)
        {
            case ProvisioningJobType.ScaffoldGitHubRepository:
            {
                await githubRepositoryScaffoldService.ScaffoldAsync(prototype, cancellationToken);
                return "GitHub repository created and GOV.UK Prototype Kit code added.";
            }
            case ProvisioningJobType.Create:
            case ProvisioningJobType.Retry:
            {
                var secret = await RequireSecretAsync(secretStore, dbContext, job.SecretReference);
                var result = await provisioner.CreatePrototypeAsync(prototype, secret, cancellationToken);
                ApplyProvisioningResult(prototype, result);
                prototype.Status = PrototypeStatus.Live;
                return null;
            }
            case ProvisioningJobType.UpdatePassword:
            {
                var secret = await RequireSecretAsync(secretStore, dbContext, job.SecretReference);
                await provisioner.UpdatePasswordAsync(prototype, secret, cancellationToken);
                return null;
            }
            case ProvisioningJobType.ConfigureGitHubDeployment:
            {
                var result = await githubDeploymentService.ConfigureAsync(prototype, cancellationToken);
                return result.Message;
            }
            case ProvisioningJobType.Delete:
            {
                await provisioner.DeletePrototypeAsync(prototype, cancellationToken);
                var githubDeletionMessage = await githubRepositoryDeletionService.DeleteScaffoldedRepositoryAsync(
                    prototype,
                    cancellationToken);
                await RemovePrototypeSecretsAsync(dbContext, secretStore, prototype.Id, cancellationToken);
                prototype.Status = PrototypeStatus.Deleted;
                prototype.PrototypeUrl = string.Empty;
                prototype.GitHubRepositoryUrl = string.Empty;
                return githubDeletionMessage;
            }
            case ProvisioningJobType.UpdateAppSettings:
                await appSettingsService.SyncSettingsAsync(prototype, cancellationToken);
                return "Config vars synced to Azure.";
            default:
                throw new InvalidOperationException($"Unsupported provisioning job type '{job.Type}'.");
        }
    }

    private static void ApplyProvisioningResult(Prototype prototype, ProvisioningResult result)
    {
        prototype.AzureResourceGroupName = result.ResourceGroupName;
        prototype.AzureAppServicePlanName = result.AppServicePlanName;
        prototype.AzureWebAppName = result.WebAppName;
        prototype.PrototypeUrl = result.PrototypeUrl;
    }

    private static async Task RemovePrototypeSecretsAsync(
        PrototypeManagerDbContext dbContext,
        IJobSecretStore secretStore,
        Guid prototypeId,
        CancellationToken cancellationToken)
    {
        var references = await dbContext.ProvisioningJobs
            .Where(job => job.PrototypeId == prototypeId && job.SecretReference != string.Empty)
            .Select(job => job.SecretReference)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var reference in references)
        {
            await secretStore.RemoveAsync(reference);
        }
    }

    private static async Task<string> RequireSecretAsync(
        IJobSecretStore secretStore,
        PrototypeManagerDbContext dbContext,
        string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new InvalidOperationException(
                "The prototype password was not linked to this job. Delete the prototype and create it again.");
        }

        var secret = await secretStore.GetAsync(reference);

        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = await dbContext.ProvisioningJobSecrets
                .AsNoTracking()
                .Where(item => item.Reference == reference)
                .Select(item => item.Secret)
                .SingleOrDefaultAsync();
        }

        return !string.IsNullOrWhiteSpace(secret)
            ? secret
            : throw new InvalidOperationException(
                "The prototype password is no longer available. Delete the prototype and create it again with a new password.");
    }
}
