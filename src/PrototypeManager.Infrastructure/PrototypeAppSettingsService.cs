using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class PrototypeAppSettingsService(
    PrototypeManagerDbContext dbContext,
    IAuditService auditService,
    AzureWebAppSettingsSyncService azureWebAppSettingsSyncService,
    IOptions<ProvisioningOptions> provisioningOptions) : IPrototypeAppSettingsService
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    public async Task<IReadOnlyList<PrototypeAppSettingView>> GetSettingsAsync(
        Guid prototypeId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await GetAccessiblePrototypeAsync(prototypeId, actorEmail, isAdmin, isSupport);

        var userSettings = await dbContext.PrototypeAppSettings
            .AsNoTracking()
            .Where(setting => setting.PrototypeId == prototype.Id)
            .OrderBy(setting => setting.Key)
            .ToListAsync();

        var pendingSyncJob = HasPendingSyncJob(prototype);
        var lastSuccessfulSyncAt = await GetLastSuccessfulAppSettingsSyncAtAsync(prototype.Id);
        var views = userSettings
            .Where(stored => !PrototypeAppSettingCatalog.IsPasswordKey(stored.Key))
            .Select(stored => new PrototypeAppSettingView
        {
            Key = stored.Key,
            Value = stored.Value,
            IsEditable = true,
            IsSecret = LooksSecret(stored.Key),
            IsPendingSync = pendingSyncJob &&
                (lastSuccessfulSyncAt is null || stored.UpdatedAt > lastSuccessfulSyncAt)
        }).ToList();

        views.Add(new PrototypeAppSettingView
        {
            Key = PrototypeAppSettingCatalog.PasswordKey,
            Value = "••••••••",
            Description = "Password for accessing the hosted prototype.",
            IsEditable = true,
            IsSecret = true,
            IsPendingSync = HasPendingPasswordJob(prototype.Id)
        });

        foreach (var systemKey in PrototypeAppSettingCatalog.SystemManagedKeys.OrderBy(key => key))
        {
            views.Add(new PrototypeAppSettingView
            {
                Key = systemKey,
                Value = MaskSystemValue(systemKey),
                Description = "Managed automatically by Prototype Manager.",
                IsEditable = false,
                IsSecret = LooksSecret(systemKey),
                IsPendingSync = false
            });
        }

        return views.OrderBy(view => view.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> GetSettingValueAsync(
        Guid prototypeId,
        string key,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await GetAccessiblePrototypeAsync(prototypeId, actorEmail, isAdmin, isSupport);
        var normalisedKey = NormaliseReadableKey(key);

        if (PrototypeAppSettingCatalog.IsSystemManaged(normalisedKey))
        {
            throw new ValidationException("That app setting is managed by Prototype Manager.");
        }

        if (PrototypeAppSettingCatalog.IsPasswordKey(normalisedKey))
        {
            throw new ValidationException("Use Manage on the PASSWORD row to view or change the prototype password.");
        }

        var setting = await FindUserSettingAsync(prototype.Id, normalisedKey);

        return setting?.Value;
    }

    public async Task SetSettingAsync(
        Guid prototypeId,
        string key,
        string value,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await GetTrackedAccessiblePrototypeAsync(prototypeId, actorEmail, isAdmin, isSupport);
        var normalisedKey = ValidateUserKey(key);
        var trimmedValue = value?.Trim() ?? string.Empty;
        PrototypeAppSettingCatalog.ValidateUserValue(trimmedValue);

        var setting = await FindUserSettingAsync(prototype.Id, normalisedKey, tracked: true);

        if (setting is null)
        {
            setting = new PrototypeAppSetting
            {
                PrototypeId = prototype.Id,
                Key = normalisedKey
            };
            dbContext.PrototypeAppSettings.Add(setting);
        }

        setting.Value = trimmedValue;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        setting.UpdatedBy = actorEmail;

        QueueSyncJob(prototype.Id);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            prototype.Id,
            actorEmail,
            "Config var updated",
            $"Updated config var {normalisedKey}.");
    }

    public async Task RemoveSettingAsync(
        Guid prototypeId,
        string key,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await GetTrackedAccessiblePrototypeAsync(prototypeId, actorEmail, isAdmin, isSupport);
        var normalisedKey = ValidateUserKey(key);

        if (PrototypeAppSettingCatalog.IsPasswordKey(normalisedKey))
        {
            throw new ValidationException("The prototype password cannot be removed.");
        }

        var setting = await FindUserSettingAsync(prototype.Id, normalisedKey, tracked: true);

        if (setting is null)
        {
            return;
        }

        dbContext.PrototypeAppSettings.Remove(setting);
        QueueSyncJob(prototype.Id);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(
            prototype.Id,
            actorEmail,
            "Config var removed",
            $"Removed config var {normalisedKey}.");
    }

    public async Task SyncSettingsAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        var userSettings = await dbContext.PrototypeAppSettings
            .AsNoTracking()
            .Where(setting => setting.PrototypeId == prototype.Id)
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

        if (!_provisioningOptions.IsAzureMode ||
            string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) ||
            string.IsNullOrWhiteSpace(prototype.AzureWebAppName))
        {
            return;
        }

        await azureWebAppSettingsSyncService.ApplyUserSettingsAsync(prototype, userSettings, cancellationToken);
    }

    private void QueueSyncJob(Guid prototypeId)
    {
        var alreadyQueued = dbContext.ProvisioningJobs.Local.Any(job =>
            job.PrototypeId == prototypeId &&
            job.Type == ProvisioningJobType.UpdateAppSettings &&
            (job.Status == ProvisioningJobStatus.Queued || job.Status == ProvisioningJobStatus.Running));

        if (alreadyQueued)
        {
            return;
        }

        dbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototypeId,
            Type = ProvisioningJobType.UpdateAppSettings,
            Status = ProvisioningJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0
        });
    }

    private bool HasPendingPasswordJob(Guid prototypeId)
    {
        var latest = dbContext.ProvisioningJobs
            .AsNoTracking()
            .Where(job =>
                job.PrototypeId == prototypeId &&
                job.Type == ProvisioningJobType.UpdatePassword)
            .AsEnumerable()
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

        return latest?.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running;
    }

    private bool HasPendingSyncJob(Prototype prototype)
    {
        if (!IsSyncApplicable(prototype))
        {
            return false;
        }

        var latest = dbContext.ProvisioningJobs
            .AsNoTracking()
            .Where(job =>
                job.PrototypeId == prototype.Id &&
                job.Type == ProvisioningJobType.UpdateAppSettings)
            .AsEnumerable()
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefault();

        return latest?.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running;
    }

    private async Task<DateTimeOffset?> GetLastSuccessfulAppSettingsSyncAtAsync(Guid prototypeId)
    {
        var completedJobs = await dbContext.ProvisioningJobs
            .AsNoTracking()
            .Where(job =>
                job.PrototypeId == prototypeId &&
                job.Type == ProvisioningJobType.UpdateAppSettings &&
                job.Status == ProvisioningJobStatus.Succeeded)
            .ToListAsync();

        var completedAt = completedJobs
            .OrderByDescending(job => job.CompletedAt ?? job.CreatedAt)
            .Select(job => job.CompletedAt ?? job.CreatedAt)
            .FirstOrDefault();

        return completedAt == default ? null : completedAt;
    }

    private bool IsSyncApplicable(Prototype prototype) =>
        _provisioningOptions.IsAzureMode &&
        !string.Equals(prototype.AzureSubscriptionId, "local-development", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(prototype.AzureResourceGroupName) &&
        !string.IsNullOrWhiteSpace(prototype.AzureWebAppName);

    private async Task<Prototype> GetAccessiblePrototypeAsync(
        Guid prototypeId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await dbContext.Prototypes
            .AsNoTracking()
            .Include(item => item.Contacts)
            .SingleOrDefaultAsync(item => item.Id == prototypeId);

        if (prototype is null ||
            !PrototypeManagementAccess.CanManagePrototype(prototype, prototype.Contacts, actorEmail, isAdmin, isSupport))
        {
            throw new ValidationException("You do not have permission to manage config vars for this prototype.");
        }

        return prototype;
    }

    private async Task<Prototype> GetTrackedAccessiblePrototypeAsync(
        Guid prototypeId,
        string actorEmail,
        bool isAdmin,
        bool isSupport)
    {
        var prototype = await dbContext.Prototypes
            .Include(item => item.Contacts)
            .SingleOrDefaultAsync(item => item.Id == prototypeId);

        if (prototype is null ||
            !PrototypeManagementAccess.CanManagePrototype(prototype, prototype.Contacts, actorEmail, isAdmin, isSupport))
        {
            throw new ValidationException("You do not have permission to manage config vars for this prototype.");
        }

        return prototype;
    }

    private static string ValidateUserKey(string key)
    {
        PrototypeAppSettingCatalog.ValidateUserKey(key);
        return key.Trim();
    }

    private static string NormaliseReadableKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ValidationException("Enter a key for this app setting.");
        }

        return key.Trim();
    }

    private async Task<PrototypeAppSetting?> FindUserSettingAsync(
        Guid prototypeId,
        string key,
        bool tracked = false)
    {
        var query = tracked
            ? dbContext.PrototypeAppSettings.Where(item => item.PrototypeId == prototypeId)
            : dbContext.PrototypeAppSettings.AsNoTracking().Where(item => item.PrototypeId == prototypeId);

        var settings = await query.ToListAsync();
        return settings.SingleOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksSecret(string key) =>
        key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("PROFILE", StringComparison.OrdinalIgnoreCase);

    private static string MaskSystemValue(string key) =>
        LooksSecret(key) ? "••••••••" : "Set by Prototype Manager";
}
