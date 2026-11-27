using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using System.Net.Http;

namespace PrototypeManager.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddPrototypeManagerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<ProvisioningOptions>(configuration.GetSection(ProvisioningOptions.SectionName));
        services.Configure<CostControlOptions>(configuration.GetSection("CostControl"));
        services.Configure<DevelopmentUserOptions>(configuration.GetSection(DevelopmentUserOptions.SectionName));
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SectionName));
        services.Configure<CompassApiOptions>(configuration.GetSection(CompassApiOptions.SectionName));
        services.Configure<NotifyOptions>(configuration.GetSection(NotifyOptions.SectionName));
        services.PostConfigure<GitHubOptions>(options =>
        {
            options.PersonalAccessToken = GitHubTokenNormalizer.Normalize(options.PersonalAccessToken);
            options.PrivateKey = options.PrivateKey?.Replace("\\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
            options.PrivateKeyPath = options.PrivateKeyPath?.Trim() ?? string.Empty;
        });
        services.PostConfigure<CompassApiOptions>(options =>
        {
            options.BaseUrl = options.BaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            options.ApiToken = options.ApiToken?.Trim() ?? string.Empty;
        });
        services.PostConfigure<NotifyOptions>(options =>
        {
            options.ApiKey = options.ApiKey?.Trim() ?? string.Empty;
            options.PrototypeReadyTemplateId = options.PrototypeReadyTemplateId?.Trim() ?? string.Empty;
            options.HelpEmail = string.IsNullOrWhiteSpace(options.HelpEmail)
                ? "design.ops@education.gov.uk"
                : options.HelpEmail.Trim();
        });
        services.AddMemoryCache();

        var connectionString = configuration.GetConnectionString("PrototypeManager")
            ?? "Data Source=prototype-manager.db";
        var useSqlite = IsSqliteConnectionString(connectionString);

        Action<DbContextOptionsBuilder> configureDbContext = options =>
        {
            if (useSqlite)
            {
                var sqliteConnectionString = SqliteConnectionStringResolver.Resolve(
                    connectionString,
                    environment.ContentRootPath);
                options.UseSqlite(sqliteConnectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        };

        services.AddDbContext<PrototypeManagerDbContext>(configureDbContext);
        services.AddScoped<IJobSecretStore, DbContextJobSecretStore>();
        services.AddHttpClient(GitHubApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false
            });
        services.AddHttpClient(PrototypeSiteVerifier.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false
            });
        services.AddHttpClient(CompassCatalogService.HttpClientName, (serviceProvider, client) =>
            {
                var compassOptions = serviceProvider.GetRequiredService<IOptions<CompassApiOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(compassOptions.BaseUrl))
                {
                    client.BaseAddress = new Uri(compassOptions.BaseUrl.TrimEnd('/') + "/");
                }

                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseProxy = false
            });
        services.AddSingleton<GitHubInstallationTokenProvider>();
        services.AddSingleton<GitHubApiClient>();
        services.AddSingleton<ICompassCatalogService>(serviceProvider =>
        {
            var compassOptions = serviceProvider.GetRequiredService<IOptions<CompassApiOptions>>().Value;
            return compassOptions.IsConfigured
                ? serviceProvider.GetRequiredService<CompassCatalogService>()
                : serviceProvider.GetRequiredService<NullCompassCatalogService>();
        });
        services.AddSingleton<CompassCatalogService>();
        services.AddSingleton<NullCompassCatalogService>();
        services.AddScoped<GitHubRepositoryAvailability>();
        services.AddScoped<NullGitHubRepositoryAvailability>();
        services.AddScoped<IGitHubRepositoryAvailability>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.IsConfigured && options.ValidateWithApi
                ? serviceProvider.GetRequiredService<GitHubRepositoryAvailability>()
                : serviceProvider.GetRequiredService<NullGitHubRepositoryAvailability>();
        });
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<IPrototypeService, PrototypeService>();
        services.AddScoped<IPrototypeAppSettingsService, PrototypeAppSettingsService>();
        services.AddScoped<AzureWebAppResourceResolver>();
        services.AddScoped<MockPrototypeWebAppRuntimeService>();
        services.AddScoped<AzurePrototypeWebAppRuntimeService>();
        services.AddScoped<IPrototypeWebAppRuntimeService, PrototypeWebAppRuntimeService>();
        services.AddScoped<IPrototypeSiteAvailabilityChecker, PrototypeSiteAvailabilityChecker>();
        services.AddScoped<AzureWebAppSettingsSyncService>();
        services.AddScoped<IAccessManagementService, AccessManagementService>();
        services.AddScoped<IGitHubOrganisationService, GitHubOrganisationService>();
        services.AddScoped<GitHubRepositorySearchService>();
        services.AddScoped<NullGitHubRepositorySearchService>();
        services.AddScoped<IGitHubRepositorySearchService>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GitHubRepositorySearchService>()
                : serviceProvider.GetRequiredService<NullGitHubRepositorySearchService>();
        });
        services.AddScoped<GitHubPrototypeCodebaseAssessor>();
        services.AddScoped<NullGitHubPrototypeCodebaseAssessor>();
        services.AddScoped<IGitHubPrototypeCodebaseAssessor>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GitHubPrototypeCodebaseAssessor>()
                : serviceProvider.GetRequiredService<NullGitHubPrototypeCodebaseAssessor>();
        });
        services.AddScoped<ICostControlPolicy, CostControlPolicy>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDeploymentWorkflowGenerator, DeploymentWorkflowGenerator>();
        services.AddScoped<IProvisioningProgressService, ProvisioningProgressService>();
        services.AddScoped<IPrototypeSiteVerifier, PrototypeSiteVerifier>();
        services.AddScoped<IGitHubDeploymentService, GitHubDeploymentService>();
        services.AddScoped<GitHubTokenIdentityService>();
        services.AddScoped<IGitHubTokenIdentityService, GitHubTokenIdentityService>();
        services.AddScoped<MockGitHubRepositoryScaffoldService>();
        services.AddScoped<GitHubRepositoryScaffoldService>();
        services.AddScoped<IGitHubRepositoryScaffoldService, GitHubRepositoryScaffoldServiceSelector>();
        services.AddScoped<MockGitHubRepositoryDeletionService>();
        services.AddScoped<GitHubRepositoryDeletionService>();
        services.AddScoped<IGitHubRepositoryDeletionService, GitHubRepositoryDeletionServiceSelector>();
        services.AddScoped<GitHubRepositoryValidator>();
        services.AddScoped<FormatOnlyGitHubRepositoryValidator>();
        services.AddScoped<GitHubAccessValidator>();
        services.AddScoped<FormatOnlyGitHubAccessValidator>();
        services.AddScoped<IGitHubAccessValidator>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GitHubAccessValidator>()
                : serviceProvider.GetRequiredService<FormatOnlyGitHubAccessValidator>();
        });
        services.AddScoped<IGitHubRepositoryValidator>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.ValidateWithApi && options.IsConfigured
                ? serviceProvider.GetRequiredService<GitHubRepositoryValidator>()
                : serviceProvider.GetRequiredService<FormatOnlyGitHubRepositoryValidator>();
        });
        services.AddScoped<IPrototypeAzureDeletionPolicy, PrototypeAzureDeletionPolicy>();
        services.AddScoped<MockPrototypeProvisioner>();
        services.AddScoped<AzureBicepPrototypeProvisioner>();
        services.AddScoped<IPrototypeProvisioner>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
            return options.IsAzureMode
                ? serviceProvider.GetRequiredService<AzureBicepPrototypeProvisioner>()
                : serviceProvider.GetRequiredService<MockPrototypeProvisioner>();
        });

        services.AddScoped<ProvisioningRollbackService>();
        services.AddScoped<GovukNotifyPrototypeReadyNotifier>();
        services.AddScoped<NullPrototypeReadyNotifier>();
        services.AddScoped<IPrototypeReadyNotifier>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<NotifyOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GovukNotifyPrototypeReadyNotifier>()
                : serviceProvider.GetRequiredService<NullPrototypeReadyNotifier>();
        });
        services.AddScoped<GovukNotifyExceptionRequestNotifier>();
        services.AddScoped<NullExceptionRequestNotifier>();
        services.AddScoped<IExceptionRequestNotifier>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<NotifyOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GovukNotifyExceptionRequestNotifier>()
                : serviceProvider.GetRequiredService<NullExceptionRequestNotifier>();
        });
        services.AddScoped<GovukNotifyFeatureSuggestionNotifier>();
        services.AddScoped<NullFeatureSuggestionNotifier>();
        services.AddScoped<IFeatureSuggestionNotifier>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<NotifyOptions>>().Value;
            return options.IsConfigured
                ? serviceProvider.GetRequiredService<GovukNotifyFeatureSuggestionNotifier>()
                : serviceProvider.GetRequiredService<NullFeatureSuggestionNotifier>();
        });
        services.AddScoped<MockFreeAppServicePlanPool>();
        services.AddScoped<AzureFreeAppServicePlanPool>();
        services.AddScoped<IFreeAppServicePlanPool, FreeAppServicePlanPool>();
        services.AddScoped<MockAppServicePlanSkuService>();
        services.AddScoped<AzureAppServicePlanSkuService>();
        services.AddScoped<IAppServicePlanSkuService, AppServicePlanSkuService>();
        services.AddHostedService<ProvisioningWorker>();

        return services;
    }

    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PrototypeManagerDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        var connectionString = configuration.GetConnectionString("PrototypeManager")
            ?? "Data Source=prototype-manager.db";

        if (IsSqliteConnectionString(connectionString))
        {
            await context.Database.EnsureCreatedAsync();
            await context.ApplyDevelopmentSchemaUpdatesAsync(environment, connectionString);
        }
        else
        {
            // No EF migrations in-repo yet; create schema from the model for Azure SQL.
            await context.Database.EnsureCreatedAsync();
            await context.ApplySqlServerAppUserProfileUpdatesAsync();
        }

        if (environment.IsDevelopment())
        {
            await seeder.SeedAsync();
        }
    }

    internal static bool IsSqliteConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        var hasServer =
            connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Data Source=tcp:", StringComparison.OrdinalIgnoreCase);
        var hasCatalog =
            connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase);

        if (hasServer || hasCatalog)
        {
            return false;
        }

        return connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase);
    }
}
