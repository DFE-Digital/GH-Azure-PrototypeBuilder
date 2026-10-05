using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace PrototypeManager.Domain;

public enum PrototypeStatus
{
    Requested,
    Queued,
    Provisioning,
    Live,
    Failed,
    Archived,
    Deleting,
    Deleted,
    Expired
}

public enum ProvisioningJobType
{
    Create,
    UpdatePassword,
    Delete,
    Retry,
    ConfigureGitHubDeployment,
    UpdateAppSettings,
    ScaffoldGitHubRepository
}

public enum ProvisioningJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public enum ExceptionRequestStatus
{
    Requested,
    Approved,
    Rejected,
    Cancelled
}

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string Support = "Support";
}

public static class BusinessAreaCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "Central Operations",
        "Children and Families",
        "Commercial",
        "Customer Experience and Design",
        "Customer Service Enterprise Operations",
        "Cyber and information security",
        "Digital Strategy",
        "Enterprise Data for Access and Sharing",
        "Enterprise Data for Funding and Oversight",
        "Enterprise Data for Insight",
        "Enterprise Delivery Service",
        "Estates Digital, Data and Technology",
        "Funding and Financial Oversight",
        "Operations and Infrastructure",
        "Regional Services",
        "Schools Digital",
        "Skills and Growth",
        "DDT Strategy and Transformation",
        "Technology Operations"
    ];
}

public static class GitHubOrganisationCatalog
{
    public static readonly IReadOnlyList<string> Defaults =
    [
        "DFE-Digital"
    ];
}

public static partial class SlugGenerator
{
    private static readonly Regex DuplicateHyphenRegex = DuplicateHyphenPattern();

    public static string GenerateSlug(string? value, int maxLength = 40)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "prototype";
        }

        var builder = new StringBuilder(value.Trim().ToLowerInvariant().Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var normalized = DuplicateHyphenRegex.Replace(builder.ToString(), "-").Trim('-');

        if (normalized.Length == 0)
        {
            normalized = "prototype";
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].Trim('-');
    }

    public static string CreateSuffix() => Guid.NewGuid().ToString("N")[..6];

    public static string CreateAzureSafeName(int maxLength, params string[] segments)
    {
        var slug = string.Join(
            '-',
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => GenerateSlug(segment)));

        if (slug.Length <= maxLength)
        {
            return slug;
        }

        return slug[..maxLength].Trim('-');
    }

    private static readonly Regex AppNameRegex = AppNamePattern();

    public static void ValidateAppName(string? value, int maxLength = 20)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Enter a prototype app name.");
        }

        var appName = value.Trim();

        if (appName.Length > maxLength)
        {
            throw new ArgumentException($"Prototype app name must be {maxLength} characters or fewer.");
        }

        if (!AppNameRegex.IsMatch(appName))
        {
            throw new ArgumentException("Prototype app name must be lowercase letters, numbers and hyphens only.");
        }
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex DuplicateHyphenPattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex AppNamePattern();
}

public static class ProvisioningActivity
{
    public const string StepAction = "Provisioning step";
}

public static class PrototypePasswordRules
{
    public const int MinimumLength = 3;

    public static void Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            throw new ValidationException($"Prototype password must be at least {MinimumLength} characters.");
        }
    }
}

public class ProvisioningJobSecret
{
    public string Reference { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public static class PrototypeResourceNaming
{
    public const string ResourceNamePrefix = "s186d01-pt-";

    public static string CreateResourceGroupName(string appName) =>
        CreateResourceName(appName, "rg", maxLength: 90);

    public static string CreateAppServicePlanName(string appName) =>
        CreateResourceName(appName, "asp", maxLength: 60);

    public static string CreateWebAppName(string appName) =>
        CreateResourceName(appName, "app", maxLength: 60);

    public static string CreateResourceName(string appName, string resourceType, int maxLength = 60)
    {
        var prefix = $"{ResourceNamePrefix}{resourceType}-";
        var slugMaxLength = Math.Max(1, maxLength - prefix.Length);
        var slug = SlugGenerator.GenerateSlug(appName.Trim().ToLowerInvariant(), slugMaxLength);

        return $"{prefix}{slug}";
    }
}

public static class PrototypeResourceTags
{
    public static IReadOnlyDictionary<string, string> Build() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Environment"] = "Dev",
            ["Product"] = "Design operations",
            ["Service Offering"] = "Design Operations"
        };
}

public class Prototype
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid? BusinessAreaId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Directorate { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string DeputyOwnerEmail { get; set; } = string.Empty;
    public string GitHubRepositoryUrl { get; set; } = string.Empty;
    public bool RequiresRepositoryScaffold { get; set; }
    public string AzureSubscriptionId { get; set; } = string.Empty;
    public string AzureResourceGroupName { get; set; } = string.Empty;
    public string AzureAppServicePlanName { get; set; } = string.Empty;
    public string AzureWebAppName { get; set; } = string.Empty;
    public string PrototypeUrl { get; set; } = string.Empty;
    public PrototypeStatus Status { get; set; } = PrototypeStatus.Requested;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public bool ContainsRealUserData { get; set; }
    public bool IsProductionService { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public bool ExceptionRequired { get; set; }

    public BusinessArea? BusinessArea { get; set; }
    public List<ProvisioningJob> ProvisioningJobs { get; set; } = [];
    public List<AuditEvent> AuditEvents { get; set; } = [];
    public List<ExceptionRequest> ExceptionRequests { get; set; } = [];
    public List<PrototypeContact> Contacts { get; set; } = [];
    public List<PrototypeAppSetting> AppSettings { get; set; } = [];
}

public class PrototypeAppSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrototypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;

    public Prototype? Prototype { get; set; }
}

public class PrototypeContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrototypeId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public Prototype? Prototype { get; set; }
}

public class BusinessArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    public List<Prototype> Prototypes { get; set; } = [];
    public List<AppUserBusinessAreaAssignment> UserAssignments { get; set; } = [];
}

public class GitHubOrganisation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<AppUserBusinessAreaAssignment> BusinessAreaAssignments { get; set; } = [];
}

public class AppUserBusinessAreaAssignment
{
    public Guid AppUserId { get; set; }
    public Guid BusinessAreaId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? AppUser { get; set; }
    public BusinessArea? BusinessArea { get; set; }
}

public class ProvisioningJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrototypeId { get; set; }
    public ProvisioningJobType Type { get; set; }
    public ProvisioningJobStatus Status { get; set; } = ProvisioningJobStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string SecretReference { get; set; } = string.Empty;

    public Prototype? Prototype { get; set; }
}

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PrototypeId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Prototype? Prototype { get; set; }
}

public class ExceptionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrototypeId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RequestedSku { get; set; } = string.Empty;
    public ExceptionRequestStatus Status { get; set; } = ExceptionRequestStatus.Requested;
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Prototype? Prototype { get; set; }
}
