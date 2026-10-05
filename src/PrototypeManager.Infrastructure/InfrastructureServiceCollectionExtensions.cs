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
        services.PostConfigure<GitHubOptions>(options =>
        {
            options.PersonalAccessToken = GitHubTokenNormalizer.Normalize(options.PersonalAccessToken);
        });

        var connectionString = configuration.GetConnectionString("PrototypeManager")
            ?? "Data Source=prototype-manager.db";
        var sqliteConnectionString = environment.IsDevelopment()
            ? SqliteConnectionStringResolver.Resolve(connectionString, environment.ContentRootPath)
            : connectionString;

        Action<DbContextOptionsBuilder> configureDbContext = options =>
        {
            if (environment.IsDevelopment())
            {
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
        services.AddSingleton<GitHubApiClient>();
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
        services.AddScoped<ICostControlPolicy, CostControlPolicy>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IDeploymentWorkflowGenerator, DeploymentWorkflowGenerator>();
        services.AddScoped<IProvisioningProgressService, ProvisioningProgressService>();
        services.AddScoped<ILiveOperationsService, LiveOperationsService>();
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
            return !string.IsNullOrWhiteSpace(options.PersonalAccessToken)
                ? serviceProvider.GetRequiredService<GitHubAccessValidator>()
                : serviceProvider.GetRequiredService<FormatOnlyGitHubAccessValidator>();
        });
        services.AddScoped<IGitHubRepositoryValidator>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return options.ValidateWithApi
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

        if (environment.IsDevelopment())
        {
            await context.Database.EnsureCreatedAsync();
            await context.ApplyDevelopmentSchemaUpdatesAsync(environment, connectionString);
            await seeder.SeedAsync();
            return;
        }

        await context.Database.MigrateAsync();
    }
}
