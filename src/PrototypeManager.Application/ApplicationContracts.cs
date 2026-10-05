using PrototypeManager.Domain;

namespace PrototypeManager.Application;

public interface IPrototypeService
{
    Task<Prototype> CreateAsync(CreatePrototypeRequest request, string actorEmail);
    Task<Prototype> CreateScaffoldedAsync(CreateScaffoldedPrototypeRequest request, string actorEmail);
    Task<Prototype?> GetAsync(Guid id);
    Task<Prototype?> GetAccessibleAsync(Guid id, string email, bool isAdmin, bool isSupport);
    Task<IReadOnlyList<Prototype>> GetForUserAsync(string email);
    Task<IReadOnlyList<Prototype>> GetForTeamAsync(string team);
    Task<IReadOnlyList<Prototype>> GetAllAsync();
    Task<IReadOnlyList<Prototype>> GetVisibleAsync(string email, bool isAdmin, bool isSupport);
    Task<IReadOnlyList<Prototype>> GetExpiringSoonAsync(int withinDays = 14);
    Task<IReadOnlyList<Prototype>> GetExpiringSoonAsync(string email, bool isAdmin, bool isSupport, int withinDays = 14);
    Task<IReadOnlyList<Prototype>> GetFailedAsync();
    Task<IReadOnlyList<Prototype>> GetFailedAsync(string email, bool isAdmin, bool isSupport);
    Task<IReadOnlyList<ProvisioningJob>> GetJobsAsync(Guid prototypeId);
    Task<IReadOnlyList<AuditEvent>> GetAuditEventsAsync(Guid prototypeId);
    Task<IReadOnlyList<ExceptionRequest>> GetExceptionRequestsAsync();
    Task ExtendExpiryAsync(Guid id, DateTimeOffset newExpiryDate, string actorEmail);
    Task ChangePasswordAsync(Guid id, string newPassword, string actorEmail);
    Task ArchiveAsync(Guid id, string actorEmail);
    Task DeleteAsync(Guid id, string actorEmail);
    Task RetryProvisioningAsync(Guid id, string actorEmail);
    Task RetryScaffoldGitHubRepositoryAsync(Guid id, string actorEmail);
    Task ConfigureGitHubDeploymentAsync(Guid id, string actorEmail);
    Task<string?> GetPrototypePasswordAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport);
    Task<IReadOnlyList<PrototypeContact>> GetContactsAsync(Guid prototypeId);
    Task AddManagerAsync(Guid prototypeId, string email, string actorEmail, bool isAdmin, bool isSupport);
    Task RemoveManagerAsync(Guid prototypeId, Guid contactId, string actorEmail, bool isAdmin, bool isSupport);
    Task TransferOwnershipAsync(Guid prototypeId, string newOwnerEmail, string actorEmail, bool isAdmin, bool isSupport);
    Task UpdateDeputyOwnerAsync(Guid prototypeId, string deputyOwnerEmail, string actorEmail, bool isAdmin, bool isSupport);
    Task UpdateBusinessAreaAsync(Guid id, Guid businessAreaId, string actorEmail, bool isAdmin, bool isSupport);
    Task StartWebAppAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport);
    Task StopWebAppAsync(Guid id, string actorEmail, bool isAdmin, bool isSupport);
}

public enum WebAppPowerState
{
    Unknown,
    Running,
    Stopped,
    Starting
}

public sealed class WebAppRuntimeStatus
{
    public WebAppPowerState PowerState { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool CanStart { get; init; }
    public bool CanStop { get; init; }
}

public interface IPrototypeWebAppRuntimeService
{
    Task<WebAppRuntimeStatus> GetStatusAsync(Prototype prototype, CancellationToken cancellationToken = default);
    Task StartAsync(Prototype prototype, CancellationToken cancellationToken = default);
    Task StopAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public enum PrototypeSiteAvailability
{
    Unavailable,
    Ready
}

public interface IPrototypeSiteAvailabilityChecker
{
    Task<PrototypeSiteAvailability> CheckAsync(string prototypeUrl, CancellationToken cancellationToken = default);
}

public interface IPrototypeAppSettingsService
{
    Task<IReadOnlyList<PrototypeAppSettingView>> GetSettingsAsync(Guid prototypeId, string actorEmail, bool isAdmin, bool isSupport);
    Task<string?> GetSettingValueAsync(Guid prototypeId, string key, string actorEmail, bool isAdmin, bool isSupport);
    Task SetSettingAsync(Guid prototypeId, string key, string value, string actorEmail, bool isAdmin, bool isSupport);
    Task RemoveSettingAsync(Guid prototypeId, string key, string actorEmail, bool isAdmin, bool isSupport);
    Task SyncSettingsAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public sealed class PrototypeAppSettingView
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsEditable { get; init; }
    public bool IsSecret { get; init; }
    public bool IsPendingSync { get; init; }
}

public interface IGitHubRepositoryValidator
{
    Task ValidateRepositoryAccessAsync(string repositoryUrl, CancellationToken cancellationToken = default);
}

public interface IGitHubRepositoryScaffoldService
{
    Task ScaffoldAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public interface IGitHubRepositoryDeletionService
{
    Task<string?> DeleteScaffoldedRepositoryAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public interface IGitHubTokenIdentityService
{
    Task<string?> TryGetAuthenticatedLoginAsync(CancellationToken cancellationToken = default);
    Task<string> ResolveRepositoryOwnerAsync(string selectedOwner, CancellationToken cancellationToken = default);
}

public interface IAccessManagementService
{
    Task<IReadOnlyList<BusinessArea>> GetVisibleBusinessAreasAsync(string email, bool isAdmin);
    Task<IReadOnlyList<BusinessAreaSummary>> GetVisibleBusinessAreaSummariesAsync(string email, bool isAdmin);
    Task<IReadOnlyList<AppUserAccessRecord>> GetUsersAsync();
    Task SaveUserAccessAsync(UpdateUserAccessRequest request, string actorEmail);
}

public interface IPrototypeProvisioner
{
    Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default);
    Task<ProvisioningResult> UpdatePasswordAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default);
    Task DeletePrototypeAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public interface ICostControlPolicy
{
    Task ValidateCreateRequestAsync(CreatePrototypeRequest request);
    Task ValidateScaffoldedCreateRequestAsync(CreateScaffoldedPrototypeRequest request);
    Task ValidatePrototypeAsync(Prototype prototype);
}

public interface IAuditService
{
    Task RecordAsync(Guid? prototypeId, string actorEmail, string action, string details);
}

public interface IJobSecretStore
{
    Task<string> SaveAsync(string secret);
    Task<string?> GetAsync(string reference);
    Task RemoveAsync(string reference);
}

public interface IDeploymentWorkflowGenerator
{
    string Generate(Prototype prototype, DeploymentWorkflowOptions? options = null);
}

public interface IGitHubDeploymentService
{
    Task<GitHubDeploymentResult> ConfigureAsync(Prototype prototype, CancellationToken cancellationToken = default);
}

public interface IPrototypeSiteVerifier
{
    Task WaitForDeployedContentAsync(
        Guid prototypeId,
        string appName,
        string prototypeUrl,
        IAuditService auditService,
        CancellationToken cancellationToken = default);
}

public interface IProvisioningProgressService
{
    ProvisioningProgressSnapshot Build(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        IReadOnlyList<AuditEvent> auditEvents);

    bool ShouldShowProgress(
        Prototype prototype,
        IReadOnlyList<ProvisioningJob> jobs,
        IReadOnlyList<AuditEvent> auditEvents);
}

public interface IGitHubAccessValidator
{
    Task ValidateForRepositoryAsync(string repositoryUrl, CancellationToken cancellationToken = default);
}

public sealed class DeploymentWorkflowOptions
{
    public bool UsePublishProfile { get; set; }
}

public sealed class GitHubDeploymentResult
{
    public bool Succeeded { get; init; }
    public string? WorkflowFilePath { get; init; }
    public string? CommitUrl { get; init; }
    public string? WorkflowRunUrl { get; init; }
    public string? Message { get; init; }
}

public sealed class CreatePrototypeRequest
{
    public string AppName { get; set; } = string.Empty;
    public Guid BusinessAreaId { get; set; }
    public string Project { get; set; } = string.Empty;
    public string GitHubRepositoryUrl { get; set; } = string.Empty;
    public string PrototypePassword { get; set; } = string.Empty;
    public bool DeclarationAgreed { get; set; }
}

public sealed class CreateScaffoldedPrototypeRequest
{
    public string AppName { get; set; } = string.Empty;
    public Guid BusinessAreaId { get; set; }
    public string GitHubOrganisation { get; set; } = string.Empty;
    public string GitHubRepositoryName { get; set; } = string.Empty;
    public string PrototypePassword { get; set; } = string.Empty;
    public bool DeclarationAgreed { get; set; }
}

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public bool ValidateWithApi { get; set; }
    public string PersonalAccessToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
    public bool AutoConfigureDeployment { get; set; } = true;
    public string WorkflowBranch { get; set; } = "main";
    public string WorkflowFileName { get; set; } = "deploy-prototype.yml";
    public bool TriggerInitialDeployment { get; set; } = true;
    public bool VerifyDeployedContent { get; set; } = true;
    public int DeploymentVerificationTimeoutMinutes { get; set; } = 5;
    public int DeploymentVerificationPollIntervalSeconds { get; set; } = 5;
    public string PublishProfileSecretName { get; set; } = "AZURE_WEBAPP_PUBLISH_PROFILE";
    public string PrototypeKitVersion { get; set; } = "13";
}

public sealed class ProvisioningResult
{
    public string ResourceGroupName { get; init; } = string.Empty;
    public string AppServicePlanName { get; init; } = string.Empty;
    public string WebAppName { get; init; } = string.Empty;
    public string PrototypeUrl { get; init; } = string.Empty;
}

public sealed class CostControlOptions
{
    public List<string> AllowedRegions { get; set; } = ["westeurope"];
    public List<string> AllowedSkus { get; set; } = ["F1"];
    public string AllowedRuntime { get; set; } = "NODE|24-lts";
    public int MaximumExpiryDays { get; set; } = 365;
    public bool RequireOwner { get; set; } = true;
    public bool RequireDeputyOwner { get; set; } = false;
    public bool RequireNoRealUserData { get; set; } = true;
    public bool RequireNotProduction { get; set; } = true;
}

public sealed class ProvisioningOptions
{
    public const string SectionName = "Provisioning";

    public string Mode { get; set; } = "Mock";
    public string AzureSubscriptionId { get; set; } = string.Empty;
    public string Location { get; set; } = "westeurope";
    public string CompiledTemplatePath { get; set; } = "infra/compiled/prototype-hosting.json";

    public bool IsAzureMode => string.Equals(Mode, "Azure", StringComparison.OrdinalIgnoreCase);
}

public sealed class DevelopmentUserOptions
{
    public const string SectionName = "DevelopmentUser";

    public bool Enabled { get; set; } = true;
    public string Email { get; set; } = "prototype.owner@education.gov.uk";
    public string DisplayName { get; set; } = "Prototype Owner";
    public string Team { get; set; } = "DesignOps";
    public string Directorate { get; set; } = "Digital";
    public List<string> Roles { get; set; } = [AppRoles.User, AppRoles.Admin];
    public List<string> AssignedBusinessAreas { get; set; } = [];
}

public sealed class BusinessAreaSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int PrototypeCount { get; init; }
}

public sealed class AppUserAccessRecord
{
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }
    public IReadOnlyList<Guid> BusinessAreaIds { get; init; } = [];
    public IReadOnlyList<string> BusinessAreaNames { get; init; } = [];
}

public sealed class UpdateUserAccessRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<Guid> BusinessAreaIds { get; set; } = [];
}

public interface IGitHubOrganisationService
{
    Task<IReadOnlyList<GitHubOrganisation>> GetAllAsync();
    Task AddAsync(string organisationInput, string actorEmail);
    Task RemoveAsync(Guid id, string actorEmail);
    Task ValidateRepositoryUrlAsync(string repositoryUrl, CancellationToken cancellationToken = default);
    Task ValidateOwnerAsync(string owner, CancellationToken cancellationToken = default);
}

public interface ILiveOperationsService
{
    Task<LiveOperationsData> GetDataAsync(string email, bool isAdmin, bool isSupport, CancellationToken cancellationToken = default);
}

public sealed class LiveOperationsData
{
    public required IReadOnlyList<Prototype> Prototypes { get; init; }
    public required IReadOnlyDictionary<Guid, IReadOnlyList<ProvisioningJob>> JobsByPrototypeId { get; init; }
    public required IReadOnlyDictionary<Guid, IReadOnlyList<AuditEvent>> AuditEventsByPrototypeId { get; init; }
}
