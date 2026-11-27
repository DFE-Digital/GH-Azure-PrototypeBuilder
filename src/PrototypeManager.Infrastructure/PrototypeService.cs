using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class PrototypeService(
    PrototypeManagerDbContext dbContext,
    ICostControlPolicy costControlPolicy,
    IGitHubRepositoryValidator githubRepositoryValidator,
    IGitHubOrganisationService githubOrganisationService,
    IGitHubTokenIdentityService gitHubTokenIdentityService,
    IGitHubAccessValidator gitHubAccessValidator,
    ICompassCatalogService compassCatalogService,
    IGitHubRepositoryAvailability gitHubRepositoryAvailability,
    IAuditService auditService,
    IJobSecretStore jobSecretStore,
    AzureWebAppSettingsSyncService azureWebAppSettingsSyncService,
    IAccessManagementService accessManagementService,
    IPrototypeWebAppRuntimeService webAppRuntimeService,
    IAppServicePlanSkuService appServicePlanSkuService,
    IExceptionRequestNotifier exceptionRequestNotifier,
    IOptions<ProvisioningOptions> provisioningOptions,
    IOptions<CostControlOptions> costControlOptions) : IPrototypeService
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;
    private readonly CostControlOptions _costControlOptions = costControlOptions.Value;

    public async Task<Prototype> CreateAsync(CreatePrototypeRequest request, string actorEmail)
    {
        await costControlPolicy.ValidateCreateRequestAsync(request);
        await githubRepositoryValidator.ValidateRepositoryAccessAsync(request.GitHubRepositoryUrl);

        var businessArea = await dbContext.BusinessAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.BusinessAreaId);

        if (businessArea is null)
        {
            throw new ValidationException("Select a valid business area.");
        }

        var appName = request.AppName.Trim();
        var prototype = new Prototype
        {
            Name = appName,
            Slug = appName,
            BusinessAreaId = businessArea.Id,
            ServiceName = request.Project.Trim(),
            Team = businessArea.Name,
            Directorate = string.Empty,
            OwnerEmail = actorEmail.Trim(),
            DeputyOwnerEmail = string.Empty,
            GitHubRepositoryUrl = request.GitHubRepositoryUrl.Trim(),
            AzureSubscriptionId = _provisioningOptions.AzureSubscriptionId,
            Status = PrototypeStatus.Requested,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actorEmail,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(90),
            ContainsRealUserData = false,
            IsProductionService = false,
            Reason = "Created via Prototype Manager",
            Region = _costControlOptions.AllowedRegions.First(),
            Sku = _costControlOptions.AllowedSkus.First(),
            ExceptionRequired = false
        };

        dbContext.Prototypes.Add(prototype);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var secretReference = await jobSecretStore.SaveAsync(request.PrototypePassword);
        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.Create,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            SecretReference = secretReference
        });

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await auditService.RecordAsync(prototype.Id, actorEmail, "Prototype requested", "Prototype created and queued for provisioning.");
        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrototypeService>.Instance,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.Queued);

        return prototype;
    }

    public async Task<Prototype> CreateScaffoldedAsync(CreateScaffoldedPrototypeRequest request, string actorEmail)
    {
        await costControlPolicy.ValidateScaffoldedCreateRequestAsync(request);

        var businessArea = await dbContext.BusinessAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.BusinessAreaId);

        if (businessArea is null)
        {
            throw new ValidationException("Select a valid business area.");
        }

        var appName = SlugGenerator.NormaliseAppName(request.AppName);
        SlugGenerator.ValidateAppName(appName);
        var organisation = request.GitHubOrganisation.Trim();
        await githubOrganisationService.ValidateOwnerAsync(organisation);
        var repositoryOwner = await gitHubTokenIdentityService.ResolveRepositoryOwnerAsync(organisation);
        var repositoryName = await AllocateUniqueRepositoryNameAsync(repositoryOwner, appName);
        var repositoryUrl = PrototypeGitHubRepositoryUrls.BuildRepositoryUrl(repositoryOwner, repositoryName);

        var workItem = request.CompassWorkItemId is int workItemId
            ? await compassCatalogService.FindWorkItemAsync(workItemId)
            : null;
        if (request.CompassWorkItemId is not null && workItem is null)
        {
            throw new ValidationException("Select a valid COMPASS work item.");
        }

        var product = !string.IsNullOrWhiteSpace(request.CompassProductId)
            ? await compassCatalogService.FindProductAsync(request.CompassProductId)
            : null;
        if (!string.IsNullOrWhiteSpace(request.CompassProductId) && product is null)
        {
            throw new ValidationException("Select a valid COMPASS product.");
        }

        var prototype = new Prototype
        {
            Name = appName,
            Slug = appName,
            BusinessAreaId = businessArea.Id,
            ServiceName = repositoryName,
            Team = businessArea.Name,
            Directorate = string.Empty,
            OwnerEmail = actorEmail.Trim(),
            DeputyOwnerEmail = string.Empty,
            GitHubRepositoryUrl = repositoryUrl,
            RequiresRepositoryScaffold = true,
            CompassWorkItemId = workItem?.Id,
            CompassWorkItemTitle = workItem?.DisplayName ?? string.Empty,
            CompassProductId = product?.Id,
            CompassProductName = product?.Name ?? string.Empty,
            AzureSubscriptionId = _provisioningOptions.AzureSubscriptionId,
            Status = PrototypeStatus.Requested,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actorEmail,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(90),
            ContainsRealUserData = false,
            IsProductionService = false,
            Reason = "Created via Prototype Manager with GOV.UK Prototype Kit scaffold",
            Region = _costControlOptions.AllowedRegions.First(),
            Sku = _costControlOptions.AllowedSkus.First(),
            ExceptionRequired = false
        };

        dbContext.Prototypes.Add(prototype);
        dbContext.TrackedGitHubRepositories.Add(new TrackedGitHubRepository
        {
            PrototypeId = prototype.Id,
            RepositoryUrl = PrototypeGitHubRepositoryUrls.Normalize(repositoryUrl),
            Owner = repositoryOwner,
            RepositoryName = repositoryName,
            PrototypeName = appName,
            CreatedBy = NormalizeEmail(actorEmail),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var secretReference = await jobSecretStore.SaveAsync(request.PrototypePassword);

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.ScaffoldGitHubRepository,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            SecretReference = secretReference
        });

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await auditService.RecordAsync(
            prototype.Id,
            actorEmail,
            "Prototype requested",
            "Prototype created with GOV.UK Prototype Kit scaffold and queued for provisioning.");
        await ProvisioningTaskRecorder.RecordCompletedAsync(
            auditService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrototypeService>.Instance,
            prototype.Id,
            prototype.Slug,
            ProvisioningTasks.Queued);

        return prototype;
    }

    public Task<Prototype?> GetAsync(Guid id) =>
        dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .SingleOrDefaultAsync(x => x.Id == id);

    public async Task<Prototype?> GetAccessibleAsync(Guid id, string email, bool isAdmin, bool isSupport)
    {
        var prototypes = await GetVisibleAsync(email, isAdmin, isSupport);
        return prototypes.SingleOrDefault(x => x.Id == id);
    }

    public async Task<IReadOnlyList<Prototype>> GetForUserAsync(string email) =>
        (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .Where(x => x.OwnerEmail == email || x.DeputyOwnerEmail == email || x.CreatedBy == email)
            .ToListAsync())
        .Where(prototype => prototype.Status != PrototypeStatus.Deleted)
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<Prototype>> GetForTeamAsync(string team) =>
        (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .Where(x => x.Team == team)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<Prototype>> GetAllAsync() =>
        (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<Prototype>> GetVisibleAsync(string email, bool isAdmin, bool isSupport)
    {
        var prototypes = (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .Include(x => x.Contacts)
            .ToListAsync())
            .Where(prototype => prototype.Status != PrototypeStatus.Deleted);

        if (isAdmin || isSupport)
        {
            return prototypes.OrderByDescending(x => x.CreatedAt).ToList();
        }

        var normalizedEmail = NormalizeEmail(email);
        var assignedBusinessAreaIds = (await AccessQueryHelpers.GetAssignedBusinessAreaIdsAsync(dbContext, normalizedEmail)).ToHashSet();
        var managerPrototypeIds = (await dbContext.PrototypeContacts.AsNoTracking()
            .Where(contact => contact.Email == normalizedEmail)
            .Select(contact => contact.PrototypeId)
            .ToListAsync()).ToHashSet();

        return prototypes
            .Where(prototype =>
                string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prototype.CreatedBy, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
                managerPrototypeIds.Contains(prototype.Id) ||
                (prototype.BusinessAreaId.HasValue && assignedBusinessAreaIds.Contains(prototype.BusinessAreaId.Value)))
            .OrderByDescending(prototype => prototype.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<Prototype>> GetExpiringSoonAsync(int withinDays = 14)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(withinDays);

        return (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .ToListAsync())
            .Where(x => x.Status != PrototypeStatus.Deleted && x.ExpiresAt <= cutoff)
            .OrderBy(x => x.ExpiresAt)
            .ToList();
    }

    public async Task<IReadOnlyList<Prototype>> GetExpiringSoonAsync(string email, bool isAdmin, bool isSupport, int withinDays = 14)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(withinDays);
        var visible = await GetVisibleAsync(email, isAdmin, isSupport);

        return visible
            .Where(x => x.Status != PrototypeStatus.Deleted && x.ExpiresAt <= cutoff)
            .OrderBy(x => x.ExpiresAt)
            .ToList();
    }

    public async Task<IReadOnlyList<Prototype>> GetFailedAsync() =>
        (await dbContext.Prototypes.AsNoTracking()
            .Include(x => x.BusinessArea)
            .Where(x => x.Status == PrototypeStatus.Failed)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<Prototype>> GetFailedAsync(string email, bool isAdmin, bool isSupport)
    {
        var visible = await GetVisibleAsync(email, isAdmin, isSupport);
        return visible.Where(x => x.Status == PrototypeStatus.Failed).ToList();
    }

    public async Task<IReadOnlyList<ProvisioningJob>> GetJobsAsync(Guid prototypeId) =>
        (await dbContext.ProvisioningJobs.AsNoTracking()
            .Where(x => x.PrototypeId == prototypeId)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<ProvisioningJob>> GetAllJobsAsync() =>
        (await dbContext.ProvisioningJobs.AsNoTracking().ToListAsync())
        .OrderByDescending(job => job.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(Guid prototypeId) =>
        (await dbContext.AuditEvents.AsNoTracking()
            .Where(x => x.PrototypeId == prototypeId)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<ExceptionRequest>> GetExceptionRequestsAsync() =>
        (await dbContext.ExceptionRequests.AsNoTracking()
            .Include(request => request.Prototype)
            .ToListAsync())
        .OrderByDescending(x => x.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<ExceptionRequest>> GetExceptionRequestsForPrototypeAsync(Guid prototypeId) =>
        (await dbContext.ExceptionRequests.AsNoTracking()
            .Where(request => request.PrototypeId == prototypeId)
            .ToListAsync())
        .OrderByDescending(request => request.CreatedAt)
        .ToList();

    public async Task<IReadOnlyList<OrphanedGitHubRepository>> GetOrphanedGitHubRepositoriesAsync()
    {
        var tracked = await dbContext.TrackedGitHubRepositories.AsNoTracking()
            .Where(item => item.ClearedAt == null)
            .ToListAsync();

        if (tracked.Count == 0)
        {
            return [];
        }

        var activeUrls = (await dbContext.Prototypes.AsNoTracking()
                .Where(prototype =>
                    prototype.Status != PrototypeStatus.Deleted &&
                    prototype.GitHubRepositoryUrl != string.Empty)
                .Select(prototype => prototype.GitHubRepositoryUrl)
                .ToListAsync())
            .Select(PrototypeGitHubRepositoryUrls.Normalize)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activePrototypeIds = (await dbContext.Prototypes.AsNoTracking()
                .Where(prototype => prototype.Status != PrototypeStatus.Deleted)
                .Select(prototype => prototype.Id)
                .ToListAsync())
            .ToHashSet();

        return tracked
            .Where(item =>
            {
                var normalizedUrl = PrototypeGitHubRepositoryUrls.Normalize(item.RepositoryUrl);
                var hasActiveUrl = activeUrls.Contains(normalizedUrl);
                var hasActivePrototype = item.PrototypeId is Guid prototypeId &&
                                         activePrototypeIds.Contains(prototypeId);
                return !hasActiveUrl && !hasActivePrototype;
            })
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new OrphanedGitHubRepository
            {
                Id = item.Id,
                PrototypeId = item.PrototypeId,
                RepositoryUrl = item.RepositoryUrl,
                Owner = item.Owner,
                RepositoryName = item.RepositoryName,
                PrototypeName = item.PrototypeName,
                CreatedBy = item.CreatedBy,
                CreatedAt = item.CreatedAt,
                Reason = item.PrototypeId is null
                    ? "No linked prototype entry."
                    : "Prototype entry was deleted from Prototype Manager."
            })
            .ToList();
    }

    public async Task MarkTrackedGitHubRepositoryClearedAsync(Guid trackedRepositoryId, string actorEmail)
    {
        var tracked = await dbContext.TrackedGitHubRepositories
            .SingleOrDefaultAsync(item => item.Id == trackedRepositoryId)
            ?? throw new ValidationException("Tracked GitHub repository not found.");

        if (tracked.ClearedAt is not null)
        {
            return;
        }

        tracked.ClearedAt = DateTimeOffset.UtcNow;
        tracked.ClearedBy = NormalizeEmail(actorEmail);
        await dbContext.SaveChangesAsync();

        await auditService.RecordAsync(
            tracked.PrototypeId,
            actorEmail,
            "GitHub repository marked cleaned up",
            $"Marked {tracked.RepositoryUrl} as cleaned up in GitHub.");
    }

    public async Task<ExceptionRequest> RequestHigherPlanAsync(
        Guid prototypeId,
        string requestedSku,
        string reason,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        _ = await GetAccessibleAsync(prototypeId, actorEmail, isAdmin, isSupport)
            ?? throw new ValidationException("Prototype not found.");

        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        var contacts = await GetContactsAsync(prototypeId);
        var normalizedActor = NormalizeEmail(actorEmail);

        var canManage = isAdmin ||
                        isSupport ||
                        string.Equals(prototype.OwnerEmail, normalizedActor, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prototype.DeputyOwnerEmail, normalizedActor, StringComparison.OrdinalIgnoreCase) ||
                        contacts.Any(contact =>
                            string.Equals(contact.Email, normalizedActor, StringComparison.OrdinalIgnoreCase));

        if (!canManage)
        {
            throw new ValidationException("You do not have permission to request a higher plan for this prototype.");
        }

        var normalizedSku = (requestedSku ?? string.Empty).Trim().ToUpperInvariant();
        var allowedHigherPlans = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B1", "B2" };
        if (!allowedHigherPlans.Contains(normalizedSku))
        {
            throw new ValidationException("Choose one of the available higher plans.");
        }

        if (_costControlOptions.AllowedSkus.Contains(normalizedSku, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(normalizedSku, prototype.Sku, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Choose a higher plan than the plan this prototype already uses.");
        }

        var normalizedReason = (reason ?? string.Empty).Trim();
        if (normalizedReason.Length < 40)
        {
            throw new ValidationException(
                "Explain why you need a higher plan in at least 40 characters. Include the legitimate research or stakeholder activity that may exceed free-plan limits.");
        }

        var hasOpenRequest = await dbContext.ExceptionRequests.AsNoTracking().AnyAsync(request =>
            request.PrototypeId == prototypeId &&
            request.Status == ExceptionRequestStatus.Requested);

        if (hasOpenRequest)
        {
            throw new ValidationException("There is already an open higher-plan request for this prototype.");
        }

        var exceptionRequest = new ExceptionRequest
        {
            PrototypeId = prototypeId,
            RequestedBy = normalizedActor,
            RequestedSku = normalizedSku,
            Reason = normalizedReason,
            Status = ExceptionRequestStatus.Requested,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ExceptionRequests.Add(exceptionRequest);
        prototype.ExceptionRequired = true;
        await dbContext.SaveChangesAsync();

        await auditService.RecordAsync(
            prototypeId,
            actorEmail,
            "Higher plan requested",
            $"Requested SKU {normalizedSku}. Reason: {normalizedReason}");

        await exceptionRequestNotifier.NotifyExceptionRequestedAsync(prototype, exceptionRequest);

        return exceptionRequest;
    }

    public async Task SetTemporarySkuAsync(Guid prototypeId, string sku, DateTimeOffset until, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        var normalizedSku = (sku ?? string.Empty).Trim().ToUpperInvariant();
        var allowedPlans = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B1", "B2" };

        if (!allowedPlans.Contains(normalizedSku))
        {
            throw new ValidationException("Choose B1 or B2 for a temporary escalation.");
        }

        if (until.UtcDateTime <= DateTimeOffset.UtcNow.UtcDateTime)
        {
            throw new ValidationException("Choose a future date for when the plan should revert to Free.");
        }

        if (until.UtcDateTime > DateTimeOffset.UtcNow.AddDays(90).UtcDateTime)
        {
            throw new ValidationException("Temporary escalations can last at most 90 days.");
        }

        if (prototype.Status is not (PrototypeStatus.Live or PrototypeStatus.Failed))
        {
            throw new ValidationException("Only provisioned prototypes can have their plan changed.");
        }

        var baseline = string.IsNullOrWhiteSpace(prototype.BaselineSku)
            ? (_costControlOptions.AllowedSkus.FirstOrDefault() ?? "F1")
            : prototype.BaselineSku;

        if (string.IsNullOrWhiteSpace(prototype.BaselineSku) ||
            string.Equals(prototype.Sku, baseline, StringComparison.OrdinalIgnoreCase) ||
            prototype.TemporarySkuUntil is null)
        {
            prototype.BaselineSku = string.IsNullOrWhiteSpace(prototype.Sku) ? baseline : prototype.Sku;
            if (allowedPlans.Contains(prototype.BaselineSku))
            {
                prototype.BaselineSku = _costControlOptions.AllowedSkus.FirstOrDefault() ?? "F1";
            }
        }

        await appServicePlanSkuService.EscalateToDedicatedPlanAsync(prototype, normalizedSku);
        prototype.Sku = normalizedSku;
        prototype.TemporarySkuUntil = until.ToUniversalTime();
        prototype.ExceptionRequired = true;

        var openRequest = await dbContext.ExceptionRequests
            .Where(request =>
                request.PrototypeId == prototypeId &&
                request.Status == ExceptionRequestStatus.Requested)
            .OrderByDescending(request => request.CreatedAt)
            .FirstOrDefaultAsync();

        if (openRequest is not null)
        {
            openRequest.Status = ExceptionRequestStatus.Approved;
            openRequest.ApprovedBy = NormalizeEmail(actorEmail);
            openRequest.ApprovedAt = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(openRequest.RequestedSku))
            {
                openRequest.RequestedSku = normalizedSku;
            }
        }

        await dbContext.SaveChangesAsync();

        await auditService.RecordAsync(
            prototypeId,
            actorEmail,
            "Temporary plan set",
            $"SKU set to {normalizedSku} until {until:yyyy-MM-dd}. Reverts to {prototype.BaselineSku}.");
    }

    public async Task RevertTemporarySkuAsync(Guid prototypeId, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        await RevertSkuInternalAsync(prototype, actorEmail, "Temporary plan reverted");
    }

    public async Task ProcessExpiredTemporarySkusAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        // SQLite cannot translate TemporarySkuUntil/Status enum filters together — load then filter in memory.
        var candidates = await dbContext.Prototypes
            .Where(prototype => prototype.TemporarySkuUntil != null)
            .ToListAsync(cancellationToken);

        var expired = candidates
            .Where(prototype =>
                prototype.TemporarySkuUntil <= now &&
                prototype.Status != PrototypeStatus.Deleted)
            .ToList();

        foreach (var prototype in expired)
        {
            try
            {
                await RevertSkuInternalAsync(prototype, "system@prototype-manager", "Temporary plan expired");
            }
            catch
            {
                // Retry on the next worker cycle.
            }
        }
    }

    private async Task RevertSkuInternalAsync(Prototype prototype, string actorEmail, string auditAction)
    {
        var baseline = string.IsNullOrWhiteSpace(prototype.BaselineSku)
            ? (_costControlOptions.AllowedSkus.FirstOrDefault() ?? "F1")
            : prototype.BaselineSku.Trim().ToUpperInvariant();

        await appServicePlanSkuService.RevertToFreePoolAsync(prototype);
        prototype.Sku = baseline;
        prototype.TemporarySkuUntil = null;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            prototype.Id,
            actorEmail,
            auditAction,
            $"SKU restored to {baseline}.");
    }

    public async Task ExtendExpiryAsync(Guid id, DateTimeOffset newExpiryDate, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        if (newExpiryDate.Date <= DateTimeOffset.UtcNow.Date)
        {
            throw new ValidationException("Expiry date must be in the future.");
        }

        if (newExpiryDate.Date > DateTimeOffset.UtcNow.AddDays(_costControlOptions.MaximumExpiryDays).Date)
        {
            throw new ValidationException("Expiry date must be no more than 12 months from today.");
        }

        prototype.ExpiresAt = newExpiryDate;
        prototype.LastReviewedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(id, actorEmail, "Expiry extended", $"Expiry date changed to {newExpiryDate:yyyy-MM-dd}.");
    }

    public async Task ChangePasswordAsync(Guid id, string newPassword, string actorEmail)
    {
        PrototypePasswordRules.Validate(newPassword);

        var prototype = await GetTrackedPrototypeAsync(id);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var secretReference = await jobSecretStore.SaveAsync(newPassword);
        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.UpdatePassword,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            SecretReference = secretReference
        });

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await auditService.RecordAsync(id, actorEmail, "Password change queued", "Prototype password update queued.");
    }

    public async Task ArchiveAsync(Guid id, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);
        prototype.Status = PrototypeStatus.Archived;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(id, actorEmail, "Prototype archived", "Prototype archived by user action.");
    }

    public async Task DeleteAsync(Guid id, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);
        prototype.Status = PrototypeStatus.Deleting;

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = id,
            Type = ProvisioningJobType.Delete,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0
        });

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(id, actorEmail, "Delete queued", "Prototype marked for deletion and queued.");
    }

    public async Task RetryProvisioningAsync(Guid id, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);
        var previousAttempts = await dbContext.ProvisioningJobs
            .CountAsync(x => x.PrototypeId == id && (x.Type == ProvisioningJobType.Create || x.Type == ProvisioningJobType.Retry));

        if (previousAttempts >= 3)
        {
            throw new ValidationException("This prototype has already reached the maximum number of provisioning attempts.");
        }

        var latestCreateOrRetry = await dbContext.ProvisioningJobs
            .Where(x => x.PrototypeId == id && (x.Type == ProvisioningJobType.Create || x.Type == ProvisioningJobType.Retry))
            .ToListAsync();

        var mostRecentCreateOrRetry = latestCreateOrRetry
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (mostRecentCreateOrRetry is null || string.IsNullOrWhiteSpace(mostRecentCreateOrRetry.SecretReference))
        {
            throw new ValidationException("A retry requires the original prototype password to still be available.");
        }

        prototype.Status = PrototypeStatus.Queued;
        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = id,
            Type = ProvisioningJobType.Retry,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = previousAttempts,
            SecretReference = mostRecentCreateOrRetry.SecretReference
        });

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(id, actorEmail, "Retry queued", "Provisioning retry queued.");
    }

    public async Task RetryScaffoldGitHubRepositoryAsync(Guid id, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        if (!prototype.RequiresRepositoryScaffold)
        {
            throw new ValidationException("This prototype does not use GitHub repository scaffolding.");
        }

        if (prototype.Status is PrototypeStatus.Deleted or PrototypeStatus.Archived or PrototypeStatus.Deleting)
        {
            throw new ValidationException("This prototype cannot be retried.");
        }

        var activeScaffoldJob = await dbContext.ProvisioningJobs.AnyAsync(x =>
            x.PrototypeId == id &&
            x.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
            (x.Status == ProvisioningJobStatus.Queued || x.Status == ProvisioningJobStatus.Running));

        if (activeScaffoldJob)
        {
            throw new ValidationException("GitHub repository setup is already in progress.");
        }

        var previousAttempts = await dbContext.ProvisioningJobs
            .CountAsync(x => x.PrototypeId == id && x.Type == ProvisioningJobType.ScaffoldGitHubRepository);

        if (previousAttempts >= 3)
        {
            throw new ValidationException("This prototype has reached the maximum number of GitHub setup attempts.");
        }

        if (prototype.Status == PrototypeStatus.Failed)
        {
            prototype.Status = PrototypeStatus.Requested;
        }

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = id,
            Type = ProvisioningJobType.ScaffoldGitHubRepository,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = previousAttempts,
            SecretReference = await ResolveScaffoldSecretReferenceAsync(id)
        });

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(id, actorEmail, "GitHub scaffold retry queued", "GitHub repository setup retry queued.");
    }

    public async Task ConfigureGitHubDeploymentAsync(Guid id, string actorEmail)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        if (prototype.Status != PrototypeStatus.Live)
        {
            throw new ValidationException("GitHub deployment can only be configured after the prototype is live.");
        }

        if (string.IsNullOrWhiteSpace(prototype.AzureWebAppName) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName))
        {
            throw new ValidationException("Azure hosting must be provisioned before GitHub deployment can be configured.");
        }

        var activeJob = await dbContext.ProvisioningJobs.AnyAsync(x =>
            x.PrototypeId == id &&
            x.Type == ProvisioningJobType.ConfigureGitHubDeployment &&
            (x.Status == ProvisioningJobStatus.Queued || x.Status == ProvisioningJobStatus.Running));

        if (activeJob)
        {
            throw new ValidationException("GitHub deployment configuration is already in progress.");
        }

        await gitHubAccessValidator.ValidateForRepositoryAsync(prototype.GitHubRepositoryUrl);

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = id,
            Type = ProvisioningJobType.ConfigureGitHubDeployment,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0
        });

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            id,
            actorEmail,
            "GitHub deployment queued",
            "Automated GitHub workflow and publish profile configuration queued.");
    }

    public async Task<IReadOnlyList<PrototypeContact>> GetContactsAsync(Guid prototypeId) =>
        (await dbContext.PrototypeContacts.AsNoTracking()
            .Where(contact => contact.PrototypeId == prototypeId)
            .ToListAsync())
        .OrderBy(contact => contact.Email)
        .ToList();

    public async Task AddManagerAsync(Guid prototypeId, string email, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        RequireContactManagementAccess(prototype, actorEmail, isAdmin, isSupport);

        var normalizedEmail = NormalizeAndValidateEmail(email);

        if (string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("This person is already listed as owner or deputy owner.");
        }

        var alreadyAdded = await dbContext.PrototypeContacts.AnyAsync(contact =>
            contact.PrototypeId == prototypeId && contact.Email == normalizedEmail);

        if (alreadyAdded)
        {
            throw new ValidationException("This person is already a prototype manager.");
        }

        dbContext.PrototypeContacts.Add(new PrototypeContact
        {
            PrototypeId = prototypeId,
            Email = normalizedEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = NormalizeEmail(actorEmail)
        });

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(prototypeId, actorEmail, "Manager added", $"Added {normalizedEmail} as a prototype manager.");
    }

    public async Task RemoveManagerAsync(Guid prototypeId, Guid contactId, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        RequireContactManagementAccess(prototype, actorEmail, isAdmin, isSupport);

        var contact = await dbContext.PrototypeContacts
            .SingleOrDefaultAsync(item => item.Id == contactId && item.PrototypeId == prototypeId);

        if (contact is null)
        {
            throw new KeyNotFoundException("Prototype manager not found.");
        }

        dbContext.PrototypeContacts.Remove(contact);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(prototypeId, actorEmail, "Manager removed", $"Removed {contact.Email} as a prototype manager.");
    }

    public async Task TransferOwnershipAsync(
        Guid prototypeId,
        string newOwnerEmail,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        RequireContactManagementAccess(prototype, actorEmail, isAdmin, isSupport);

        var normalizedNewOwner = NormalizeAndValidateEmail(newOwnerEmail);
        var oldOwner = prototype.OwnerEmail;

        if (string.Equals(oldOwner, normalizedNewOwner, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Choose a different person to transfer ownership to.");
        }

        var isDeputy = !string.IsNullOrWhiteSpace(prototype.DeputyOwnerEmail) &&
                       string.Equals(prototype.DeputyOwnerEmail, normalizedNewOwner, StringComparison.OrdinalIgnoreCase);

        var contact = await dbContext.PrototypeContacts
            .SingleOrDefaultAsync(item =>
                item.PrototypeId == prototypeId &&
                item.Email == normalizedNewOwner);

        if (!isDeputy && contact is null)
        {
            throw new ValidationException("The new owner must be an existing member or deputy.");
        }

        var oldOwnerAlreadyMember = await dbContext.PrototypeContacts.AnyAsync(item =>
            item.PrototypeId == prototypeId &&
            item.Email == oldOwner);

        if (!oldOwnerAlreadyMember)
        {
            dbContext.PrototypeContacts.Add(new PrototypeContact
            {
                PrototypeId = prototypeId,
                Email = oldOwner,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = NormalizeEmail(actorEmail)
            });
        }

        prototype.OwnerEmail = normalizedNewOwner;

        if (isDeputy)
        {
            prototype.DeputyOwnerEmail = string.Empty;
        }

        if (contact is not null)
        {
            dbContext.PrototypeContacts.Remove(contact);
        }

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            prototypeId,
            actorEmail,
            "Ownership transferred",
            $"Transferred ownership from {oldOwner} to {normalizedNewOwner}.");
    }

    public async Task UpdateDeputyOwnerAsync(Guid prototypeId, string deputyOwnerEmail, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        RequireContactManagementAccess(prototype, actorEmail, isAdmin, isSupport);

        var normalizedDeputy = string.IsNullOrWhiteSpace(deputyOwnerEmail)
            ? string.Empty
            : NormalizeAndValidateEmail(deputyOwnerEmail);

        if (!string.IsNullOrWhiteSpace(normalizedDeputy) &&
            string.Equals(prototype.OwnerEmail, normalizedDeputy, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("The deputy owner must be a different person from the owner.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedDeputy))
        {
            var alreadyManager = await dbContext.PrototypeContacts.AnyAsync(contact =>
                contact.PrototypeId == prototypeId && contact.Email == normalizedDeputy);

            if (alreadyManager)
            {
                throw new ValidationException("Remove this person from prototype managers before making them deputy owner.");
            }
        }

        prototype.DeputyOwnerEmail = normalizedDeputy;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            prototypeId,
            actorEmail,
            "Deputy owner updated",
            string.IsNullOrWhiteSpace(normalizedDeputy)
                ? "Deputy owner removed."
                : $"Deputy owner set to {normalizedDeputy}.");
    }

    public async Task<string?> GetPrototypePasswordAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetAccessibleAsync(id, actorEmail, isAdmin, isSupport);

        if (prototype is null)
        {
            throw new KeyNotFoundException("Prototype not found.");
        }

        if (!await CanViewPasswordAsync(prototype, actorEmail, isAdmin, isSupport))
        {
            throw new ValidationException("You do not have permission to view this prototype password.");
        }

        // Azure is the source of truth once hosting exists, so portal changes are reflected in the app.
        var azurePassword = await TryGetPasswordFromAzureAsync(prototype, id, actorEmail, recordAudit: false);
        if (!string.IsNullOrWhiteSpace(azurePassword))
        {
            await auditService.RecordAsync(id, actorEmail, "Password viewed", "Prototype password was displayed to the user.");
            return azurePassword;
        }

        var jobs = await GetJobsAsync(id);
        var secretJob = jobs
            .Where(job => job.Type is ProvisioningJobType.Create
                or ProvisioningJobType.Retry
                or ProvisioningJobType.UpdatePassword
                or ProvisioningJobType.ScaffoldGitHubRepository)
            .Where(job => !string.IsNullOrWhiteSpace(job.SecretReference))
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

        if (secretJob is null)
        {
            return null;
        }

        var password = await jobSecretStore.GetAsync(secretJob.SecretReference);

        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        await auditService.RecordAsync(id, actorEmail, "Password viewed", "Prototype password was displayed to the user.");
        return password;
    }

    private async Task<string?> TryGetPasswordFromAzureAsync(
        Prototype prototype,
        Guid id,
        string actorEmail,
        bool recordAudit = true)
    {
        if (!azureWebAppSettingsSyncService.IsAzureConfigured(prototype))
        {
            return null;
        }

        try
        {
            var password = await azureWebAppSettingsSyncService.GetApplicationSettingAsync(
                prototype,
                PrototypeAppSettingCatalog.PasswordKey);

            if (string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            if (recordAudit)
            {
                await auditService.RecordAsync(id, actorEmail, "Password viewed", "Prototype password was displayed to the user.");
            }

            return password;
        }
        catch
        {
            return null;
        }
    }

    public async Task UpdateBusinessAreaAsync(Guid id, Guid businessAreaId, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        BusinessArea? businessArea;
        if (isAdmin || isSupport)
        {
            businessArea = await dbContext.BusinessAreas.AsNoTracking()
                .SingleOrDefaultAsync(area => area.Id == businessAreaId);
        }
        else
        {
            var visibleAreas = await accessManagementService.GetVisibleBusinessAreasAsync(actorEmail, isAdmin: false);
            businessArea = visibleAreas.SingleOrDefault(area => area.Id == businessAreaId);
        }

        if (businessArea is null)
        {
            throw new ValidationException("Select a valid business area.");
        }

        var previousName = prototype.BusinessArea?.Name ?? "None";
        prototype.BusinessAreaId = businessArea.Id;
        prototype.Team = businessArea.Name;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            id,
            actorEmail,
            "Business area changed",
            $"Changed from {previousName} to {businessArea.Name}.");
    }

    public async Task UpdateCompassWorkItemAsync(
        Guid id,
        int? workItemId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        await RequirePrototypeManagementAccessAsync(id, actorEmail, isAdmin, isSupport);
        var prototype = await GetTrackedPrototypeAsync(id);

        CompassWorkItemOption? workItem = null;
        if (workItemId is int selectedId)
        {
            workItem = await compassCatalogService.FindWorkItemAsync(selectedId)
                ?? throw new ValidationException("Select a valid COMPASS work item.");
        }

        var previous = string.IsNullOrWhiteSpace(prototype.CompassWorkItemTitle)
            ? prototype.CompassWorkItemId?.ToString() ?? "None"
            : prototype.CompassWorkItemTitle;

        prototype.CompassWorkItemId = workItem?.Id;
        prototype.CompassWorkItemTitle = workItem?.DisplayName ?? string.Empty;
        await dbContext.SaveChangesAsync();

        await auditService.RecordAsync(
            id,
            actorEmail,
            "COMPASS work item updated",
            workItem is null
                ? $"Cleared COMPASS work item (was {previous})."
                : $"Changed COMPASS work item from {previous} to {workItem.DisplayName}.");
    }

    public async Task UpdateCompassProductAsync(
        Guid id,
        string? productId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        await RequirePrototypeManagementAccessAsync(id, actorEmail, isAdmin, isSupport);
        var prototype = await GetTrackedPrototypeAsync(id);

        CompassProductOption? product = null;
        if (!string.IsNullOrWhiteSpace(productId))
        {
            product = await compassCatalogService.FindProductAsync(productId)
                ?? throw new ValidationException("Select a valid COMPASS product.");
        }

        var previous = string.IsNullOrWhiteSpace(prototype.CompassProductName)
            ? prototype.CompassProductId ?? "None"
            : prototype.CompassProductName;

        prototype.CompassProductId = product?.Id;
        prototype.CompassProductName = product?.Name ?? string.Empty;
        await dbContext.SaveChangesAsync();

        await auditService.RecordAsync(
            id,
            actorEmail,
            "COMPASS product updated",
            product is null
                ? $"Cleared COMPASS product (was {previous})."
                : $"Changed COMPASS product from {previous} to {product.Name}.");
    }

    public async Task StartWebAppAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        if (prototype.Status != PrototypeStatus.Live)
        {
            throw new ValidationException("Only live prototypes can be started.");
        }

        await webAppRuntimeService.StartAsync(prototype);
        await auditService.RecordAsync(id, actorEmail, "Web app started", "Azure web app start requested.");
    }

    public async Task StopWebAppAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport)
    {
        var prototype = await GetTrackedPrototypeAsync(id);

        if (prototype.Status != PrototypeStatus.Live)
        {
            throw new ValidationException("Only live prototypes can be stopped.");
        }

        await webAppRuntimeService.StopAsync(prototype);
        await auditService.RecordAsync(id, actorEmail, "Web app stopped", "Azure web app stop requested.");
    }

    private async Task<Prototype> GetTrackedPrototypeAsync(Guid id)
    {
        var prototype = await dbContext.Prototypes
            .Include(x => x.BusinessArea)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (prototype is null)
        {
            throw new KeyNotFoundException("Prototype not found.");
        }

        return prototype;
    }

    private async Task<string> ResolveScaffoldSecretReferenceAsync(Guid prototypeId)
    {
        // Order in memory — SQLite cannot ORDER BY DateTimeOffset in SQL.
        var jobs = (await dbContext.ProvisioningJobs.AsNoTracking()
                .Where(job => job.PrototypeId == prototypeId)
                .ToListAsync())
            .OrderByDescending(job => job.CreatedAt)
            .ToList();

        var secretReference = jobs
            .Where(job => job.Type is ProvisioningJobType.ScaffoldGitHubRepository
                or ProvisioningJobType.Create
                or ProvisioningJobType.Retry)
            .Select(job => job.SecretReference)
            .FirstOrDefault(reference => !string.IsNullOrWhiteSpace(reference));

        if (string.IsNullOrWhiteSpace(secretReference))
        {
            throw new ValidationException(
                "Setup cannot be retried because the original prototype password is no longer available.");
        }

        return secretReference;
    }

    private async Task RequirePrototypeManagementAccessAsync(
        Guid prototypeId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        _ = await GetAccessibleAsync(prototypeId, actorEmail, isAdmin, isSupport)
            ?? throw new ValidationException("Prototype not found.");

        var prototype = await GetTrackedPrototypeAsync(prototypeId);
        var contacts = await GetContactsAsync(prototypeId);
        var normalizedActor = NormalizeEmail(actorEmail);

        var canManage = isAdmin ||
                        isSupport ||
                        string.Equals(prototype.OwnerEmail, normalizedActor, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prototype.DeputyOwnerEmail, normalizedActor, StringComparison.OrdinalIgnoreCase) ||
                        contacts.Any(contact =>
                            string.Equals(contact.Email, normalizedActor, StringComparison.OrdinalIgnoreCase));

        if (!canManage)
        {
            throw new ValidationException("You do not have permission to manage this prototype.");
        }
    }

    private static void RequireContactManagementAccess(Prototype prototype, string actorEmail, bool isAdmin, bool isSupport)
    {
        if (isAdmin || isSupport)
        {
            return;
        }

        if (!string.Equals(prototype.OwnerEmail, NormalizeEmail(actorEmail), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Only the prototype owner can manage contacts.");
        }
    }

    private async Task<bool> CanViewPasswordAsync(Prototype prototype, string actorEmail, bool isAdmin, bool isSupport)
    {
        if (isAdmin || isSupport)
        {
            return true;
        }

        var normalizedEmail = NormalizeEmail(actorEmail);

        if (string.Equals(prototype.OwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(prototype.CreatedBy, normalizedEmail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(prototype.DeputyOwnerEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return await dbContext.PrototypeContacts.AsNoTracking()
            .AnyAsync(contact => contact.PrototypeId == prototype.Id && contact.Email == normalizedEmail);
    }

    private static string NormalizeAndValidateEmail(string email)
    {
        var normalizedEmail = NormalizeEmail(email);

        if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ValidationException("Enter a valid email address.");
        }

        return normalizedEmail;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task<string> AllocateUniqueRepositoryNameAsync(string repositoryOwner, string appName)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var repositoryName = PrototypeRepositoryNaming.CreateRepositoryName(appName);
            var repositoryUrl = PrototypeGitHubRepositoryUrls.BuildRepositoryUrl(repositoryOwner, repositoryName);

            var existsLocally = await dbContext.Prototypes.AsNoTracking()
                .AnyAsync(prototype =>
                    prototype.Status != PrototypeStatus.Deleted &&
                    (prototype.GitHubRepositoryUrl == repositoryUrl ||
                     prototype.ServiceName == repositoryName));

            if (existsLocally)
            {
                continue;
            }

            if (await gitHubRepositoryAvailability.ExistsAsync(repositoryOwner, repositoryName))
            {
                continue;
            }

            return repositoryName;
        }

        throw new ValidationException(
            "Could not allocate a unique GitHub repository name. Try again in a moment.");
    }
}
