using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed partial class CostControlPolicy(IOptions<CostControlOptions> options) : ICostControlPolicy
{
    private readonly CostControlOptions _options = options.Value;

    public Task ValidateCreateRequestAsync(CreatePrototypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Project) ||
            string.IsNullOrWhiteSpace(request.GitHubRepositoryUrl) ||
            string.IsNullOrWhiteSpace(request.PrototypePassword) ||
            request.BusinessAreaId == Guid.Empty)
        {
            throw new ValidationException("Complete all required fields before creating a prototype.");
        }

        try
        {
            SlugGenerator.ValidateAppName(request.AppName);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException(exception.Message);
        }

        if (!request.DeclarationAgreed)
        {
            throw new ValidationException("You must confirm the prototype declaration before continuing.");
        }

        if (request.PrototypePassword.Length < PrototypePasswordRules.MinimumLength)
        {
            throw new ValidationException($"Prototype password must be at least {PrototypePasswordRules.MinimumLength} characters.");
        }

        GitHubRepositoryValidator.ParseRepositoryUrl(request.GitHubRepositoryUrl);

        return Task.CompletedTask;
    }

    public Task ValidateScaffoldedCreateRequestAsync(CreateScaffoldedPrototypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GitHubOrganisation) ||
            string.IsNullOrWhiteSpace(request.GitHubRepositoryName) ||
            string.IsNullOrWhiteSpace(request.PrototypePassword) ||
            request.BusinessAreaId == Guid.Empty)
        {
            throw new ValidationException("Complete all required fields before creating a prototype.");
        }

        try
        {
            SlugGenerator.ValidateAppName(request.AppName);
        }
        catch (ArgumentException exception)
        {
            throw new ValidationException(exception.Message);
        }

        if (!GitHubRepositoryNameRegex().IsMatch(request.GitHubRepositoryName))
        {
            throw new ValidationException("Repository name must use lowercase letters, numbers and hyphens only.");
        }

        if (!request.DeclarationAgreed)
        {
            throw new ValidationException("You must confirm the prototype declaration before continuing.");
        }

        if (request.PrototypePassword.Length < PrototypePasswordRules.MinimumLength)
        {
            throw new ValidationException($"Prototype password must be at least {PrototypePasswordRules.MinimumLength} characters.");
        }

        return Task.CompletedTask;
    }

    public Task ValidatePrototypeAsync(Prototype prototype)
    {
        if (!_options.AllowedRegions.Contains(prototype.Region, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Region '{prototype.Region}' is not allowed.");
        }

        if (!_options.AllowedSkus.Contains(prototype.Sku, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"SKU '{prototype.Sku}' is not allowed.");
        }

        if (!PrototypeResourceNameRegex().IsMatch(prototype.AzureResourceGroupName) ||
            !prototype.AzureResourceGroupName.StartsWith($"{PrototypeResourceNaming.ResourceNamePrefix}rg-", StringComparison.Ordinal) ||
            !PrototypeResourceNameRegex().IsMatch(prototype.AzureAppServicePlanName) ||
            !prototype.AzureAppServicePlanName.StartsWith($"{PrototypeResourceNaming.ResourceNamePrefix}asp-", StringComparison.Ordinal) ||
            !PrototypeResourceNameRegex().IsMatch(prototype.AzureWebAppName) ||
            !prototype.AzureWebAppName.StartsWith($"{PrototypeResourceNaming.ResourceNamePrefix}app-", StringComparison.Ordinal))
        {
            throw new ValidationException("Generated Azure resource names must use the approved naming convention.");
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex GitHubRepositoryNameRegex();

    [GeneratedRegex("^s186d01-pt-[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex PrototypeResourceNameRegex();
}

internal sealed class AuditService(PrototypeManagerDbContext dbContext) : IAuditService
{
    public async Task RecordAsync(Guid? prototypeId, string actorEmail, string action, string details)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            PrototypeId = prototypeId,
            ActorEmail = actorEmail,
            Action = action,
            Details = details,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }
}

internal sealed class InMemoryJobSecretStore : IJobSecretStore
{
    private readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public Task<string> SaveAsync(string secret)
    {
        var key = Guid.NewGuid().ToString("N");

        lock (_lock)
        {
            _storage[key] = secret;
        }

        return Task.FromResult(key);
    }

    public Task<string?> GetAsync(string reference)
    {
        lock (_lock)
        {
            _storage.TryGetValue(reference, out var value);
            return Task.FromResult(value);
        }
    }

    public Task RemoveAsync(string reference)
    {
        lock (_lock)
        {
            _storage.Remove(reference);
        }

        return Task.CompletedTask;
    }
}

internal static class JobSecretStorePaths
{
    public static string ResolveFilePath(string connectionString, string contentRootPath)
    {
        var databasePath = SqliteConnectionStringResolver.ResolveDatabasePath(connectionString, contentRootPath);
        var directory = Path.GetDirectoryName(databasePath) ?? contentRootPath;
        var fileName = $"{Path.GetFileNameWithoutExtension(databasePath)}.job-secrets.json";

        return Path.Combine(directory, fileName);
    }
}

internal static class SqliteConnectionStringResolver
{
    public static string Resolve(string connectionString, string contentRootPath)
    {
        var databasePath = ResolveDatabasePath(connectionString, contentRootPath);
        return $"Data Source={databasePath}";
    }

    public static string ResolveDatabasePath(string connectionString, string contentRootPath)
    {
        const string dataSourcePrefix = "Data Source=";
        var dataSource = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
            ?[dataSourcePrefix.Length..]
            ?? "prototype-manager.db";

        if (!Path.IsPathRooted(dataSource))
        {
            dataSource = Path.Combine(contentRootPath, dataSource);
        }

        return dataSource;
    }
}

internal sealed class DbContextJobSecretStore(PrototypeManagerDbContext dbContext) : IJobSecretStore
{
    public async Task<string> SaveAsync(string secret)
    {
        var reference = Guid.NewGuid().ToString("N");
        dbContext.ProvisioningJobSecrets.Add(new ProvisioningJobSecret
        {
            Reference = reference,
            Secret = secret
        });

        await dbContext.SaveChangesAsync();
        return reference;
    }

    public async Task<string?> GetAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var secret = await dbContext.ProvisioningJobSecrets
            .AsNoTracking()
            .Where(item => item.Reference == reference)
            .Select(item => item.Secret)
            .SingleOrDefaultAsync();

        return string.IsNullOrWhiteSpace(secret) ? null : secret;
    }

    public async Task RemoveAsync(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        var secret = await dbContext.ProvisioningJobSecrets.FindAsync(reference);

        if (secret is null)
        {
            return;
        }

        dbContext.ProvisioningJobSecrets.Remove(secret);
        await dbContext.SaveChangesAsync();
    }
}

internal sealed class DeploymentWorkflowGenerator : IDeploymentWorkflowGenerator
{
    public string Generate(Prototype prototype, DeploymentWorkflowOptions? options = null)
    {
        options ??= new DeploymentWorkflowOptions();

        var webAppName = string.IsNullOrWhiteSpace(prototype.AzureWebAppName)
            ? "your-web-app-name"
            : prototype.AzureWebAppName;

        var builder = new StringBuilder();
        builder.AppendLine("name: Deploy prototype");
        builder.AppendLine();
        builder.AppendLine("on:");
        builder.AppendLine("  push:");
        builder.AppendLine("    branches: [ main ]");
        builder.AppendLine("  workflow_dispatch:");
        builder.AppendLine();
        builder.AppendLine("permissions:");

        if (options.UsePublishProfile)
        {
            builder.AppendLine("  contents: read");
        }
        else
        {
            builder.AppendLine("  id-token: write");
            builder.AppendLine("  contents: read");
        }

        builder.AppendLine();
        builder.AppendLine("jobs:");
        builder.AppendLine("  deploy:");
        builder.AppendLine("    runs-on: ubuntu-latest");
        builder.AppendLine("    steps:");
        builder.AppendLine("      - name: Check out repository");
        builder.AppendLine("        uses: actions/checkout@v4");
        builder.AppendLine();
        builder.AppendLine("      - name: Set up Node.js");
        builder.AppendLine("        uses: actions/setup-node@v4");
        builder.AppendLine("        with:");
        builder.AppendLine("          node-version: 24");
        builder.AppendLine("          cache: npm");
        builder.AppendLine();
        builder.AppendLine("      - name: Install dependencies");
        builder.AppendLine("        run: npm install");
        builder.AppendLine();
        builder.AppendLine("      - name: Run tests");
        builder.AppendLine("        run: npm test --if-present");
        builder.AppendLine();

        if (!options.UsePublishProfile)
        {
            builder.AppendLine("      - name: Authenticate to Azure");
            builder.AppendLine("        uses: azure/login@v2");
            builder.AppendLine("        with:");
            builder.AppendLine("          client-id: ${{ secrets.AZURE_CLIENT_ID }}");
            builder.AppendLine("          tenant-id: ${{ secrets.AZURE_TENANT_ID }}");
            builder.AppendLine("          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}");
            builder.AppendLine();
        }

        builder.AppendLine("      - name: Deploy to Azure Web App");
        builder.AppendLine("        uses: azure/webapps-deploy@v3");
        builder.AppendLine("        with:");
        builder.AppendLine($"          app-name: {webAppName}");

        if (options.UsePublishProfile)
        {
            builder.AppendLine("          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}");
        }

        builder.AppendLine("          package: .");

        return builder.ToString();
    }
}

internal sealed class DevelopmentDataSeeder(
    PrototypeManagerDbContext dbContext,
    IOptions<DevelopmentUserOptions> developmentUserOptions)
{
    public async Task SeedAsync()
    {
        var businessAreas = await SeedBusinessAreasAsync();
        await SeedGitHubOrganisationsAsync();
        await SeedUsersAsync(businessAreas);
        await BackfillPrototypeBusinessAreasAsync(businessAreas);

        if (await dbContext.Prototypes.AnyAsync())
        {
            return;
        }

        var prototypes = new[]
        {
            CreatePrototype("Teacher contacts alpha", "DesignOps", "Schools Digital", PrototypeStatus.Live, 45, "teacher-contacts-alpha", businessAreas),
            CreatePrototype("Attendance patterns sandbox", "DesignOps", "Customer Experience and Design", PrototypeStatus.Live, 21, "attendance-patterns-sandbox", businessAreas),
            CreatePrototype("Admissions failure spike", "Platform", "Central Operations", PrototypeStatus.Failed, 14, "admissions-failure-spike", businessAreas),
            CreatePrototype("School onboarding concepts", "Platform", "Children and Families", PrototypeStatus.Live, 5, "school-onboarding-concepts", businessAreas),
            CreatePrototype("Archived form concepts", "Policy", "DDT Strategy and Transformation", PrototypeStatus.Archived, -3, "archived-form-concepts", businessAreas)
        };

        dbContext.Prototypes.AddRange(prototypes);

        foreach (var prototype in prototypes)
        {
            dbContext.ProvisioningJobs.Add(new ProvisioningJob
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.Create,
                Status = prototype.Status == PrototypeStatus.Failed ? ProvisioningJobStatus.Failed : ProvisioningJobStatus.Succeeded,
                AttemptCount = 1,
                CreatedAt = prototype.CreatedAt,
                StartedAt = prototype.CreatedAt.AddMinutes(1),
                CompletedAt = prototype.CreatedAt.AddMinutes(2),
                ErrorMessage = prototype.Status == PrototypeStatus.Failed ? "Mock provisioning failed while deploying the sample prototype." : null
            });

            dbContext.AuditEvents.Add(new AuditEvent
            {
                PrototypeId = prototype.Id,
                ActorEmail = prototype.OwnerEmail,
                Action = "Seeded",
                Details = $"Seed data created with status {prototype.Status}.",
                CreatedAt = prototype.CreatedAt
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task BackfillPrototypeBusinessAreasAsync(IReadOnlyDictionary<string, BusinessArea> businessAreas)
    {
        var prototypes = await dbContext.Prototypes
            .Where(prototype => prototype.BusinessAreaId == null)
            .ToListAsync();

        if (prototypes.Count == 0)
        {
            return;
        }

        foreach (var prototype in prototypes)
        {
            prototype.BusinessAreaId = ResolveBusinessAreaId(prototype, businessAreas);
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task<Dictionary<string, BusinessArea>> SeedBusinessAreasAsync()
    {
        var existingAreas = await dbContext.BusinessAreas.ToListAsync();
        var byName = existingAreas.ToDictionary(area => area.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var businessAreaName in BusinessAreaCatalog.All)
        {
            if (byName.ContainsKey(businessAreaName))
            {
                continue;
            }

            var area = new BusinessArea
            {
                Id = Guid.NewGuid(),
                Name = businessAreaName,
                Slug = SlugGenerator.GenerateSlug(businessAreaName, 60)
            };

            dbContext.BusinessAreas.Add(area);
            byName[businessAreaName] = area;
        }

        await dbContext.SaveChangesAsync();
        return byName;
    }

    private async Task SeedGitHubOrganisationsAsync()
    {
        var existingOrganisations = await dbContext.GitHubOrganisations
            .Select(organisation => organisation.Name)
            .ToListAsync();

        var existingSet = existingOrganisations.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var organisationName in GitHubOrganisationCatalog.Defaults)
        {
            if (existingSet.Contains(organisationName))
            {
                continue;
            }

            dbContext.GitHubOrganisations.Add(new GitHubOrganisation
            {
                Id = Guid.NewGuid(),
                Name = organisationName,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system"
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task SeedUsersAsync(IReadOnlyDictionary<string, BusinessArea> businessAreas)
    {
        var developmentUser = developmentUserOptions.Value;
        var email = developmentUser.Email.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var user = await dbContext.AppUsers
            .Include(x => x.BusinessAreaAssignments)
            .SingleOrDefaultAsync(x => x.Email == email);

        if (user is null)
        {
            user = new AppUser
            {
                Email = email,
                DisplayName = developmentUser.DisplayName,
                IsAdmin = developmentUser.Roles.Contains(AppRoles.Admin),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.AppUsers.Add(user);
        }
        else
        {
            user.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? developmentUser.DisplayName : user.DisplayName;
            user.IsAdmin = user.IsAdmin || developmentUser.Roles.Contains(AppRoles.Admin);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            dbContext.AppUserBusinessAreaAssignments.RemoveRange(user.BusinessAreaAssignments);
        }

        var assignedAreaNames = developmentUser.AssignedBusinessAreas.Count > 0
            ? developmentUser.AssignedBusinessAreas
            : businessAreas.Keys.Take(3).ToList();

        user.BusinessAreaAssignments = assignedAreaNames
            .Where(name => businessAreas.ContainsKey(name))
            .Select(name => new AppUserBusinessAreaAssignment
            {
                AppUser = user,
                BusinessAreaId = businessAreas[name].Id,
                CreatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        await dbContext.SaveChangesAsync();
    }

    private static Prototype CreatePrototype(
        string name,
        string team,
        string businessAreaName,
        PrototypeStatus status,
        int expiryOffsetDays,
        string slugSeed,
        IReadOnlyDictionary<string, BusinessArea> businessAreas)
    {
        var prototypeSlug = SlugGenerator.GenerateSlug(slugSeed);
        var teamSlug = SlugGenerator.GenerateSlug(team, 24);
        var createdAt = DateTimeOffset.UtcNow.AddDays(-7);
        var webAppName = PrototypeResourceNaming.CreateWebAppName(prototypeSlug);

        return new Prototype
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = prototypeSlug,
            BusinessAreaId = businessAreas[businessAreaName].Id,
            ServiceName = name,
            Team = team,
            Directorate = "Digital",
            OwnerEmail = $"{teamSlug}.owner@education.gov.uk",
            DeputyOwnerEmail = $"{teamSlug}.deputy@education.gov.uk",
            GitHubRepositoryUrl = $"https://github.com/department-for-education/{prototypeSlug}",
            AzureSubscriptionId = "local-development",
            AzureResourceGroupName = PrototypeResourceNaming.CreateResourceGroupName(prototypeSlug),
            AzureAppServicePlanName = PrototypeResourceNaming.CreateAppServicePlanName(prototypeSlug),
            AzureWebAppName = webAppName,
            PrototypeUrl = $"https://{webAppName}.azurewebsites.net",
            Status = status,
            CreatedAt = createdAt,
            CreatedBy = $"{teamSlug}.owner@education.gov.uk",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiryOffsetDays),
            LastReviewedAt = createdAt.AddDays(2),
            ContainsRealUserData = false,
            IsProductionService = false,
            Reason = "Development seed data for the Prototype Manager dashboard.",
            Region = "westeurope",
            Sku = "F1",
            ExceptionRequired = false
        };
    }

    private static Guid ResolveBusinessAreaId(Prototype prototype, IReadOnlyDictionary<string, BusinessArea> businessAreas)
    {
        var businessAreaName = prototype.Team switch
        {
            "DesignOps" => "Customer Experience and Design",
            "Platform" => "Technology Operations",
            "Policy" => "DDT Strategy and Transformation",
            _ => BusinessAreaCatalog.All[0]
        };

        return businessAreas[businessAreaName].Id;
    }
}
