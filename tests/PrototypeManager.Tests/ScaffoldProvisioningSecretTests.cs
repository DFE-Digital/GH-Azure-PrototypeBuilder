using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;
using System.Reflection;

namespace PrototypeManager.Tests;

public sealed class ScaffoldProvisioningSecretTests
{
    private static readonly Guid TestBusinessAreaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Worker_CreateReadsPasswordFromDatabaseAfterScaffold()
    {
        await using var fixture = await Fixture.CreateAsync();

        var prototype = await fixture.PrototypeService.CreateScaffoldedAsync(
            new CreateScaffoldedPrototypeRequest
            {
                AppName = "secret-flow-proto",
                BusinessAreaId = TestBusinessAreaId,
                GitHubOrganisation = "department-for-education",
                GitHubRepositoryName = "secret-flow-proto",
                PrototypePassword = "abc",
                DeclarationAgreed = true
            },
            "creator@education.gov.uk");

        await ProcessNextJobAsync(fixture);
        await ProcessNextJobAsync(fixture);

        fixture.DbContext.ChangeTracker.Clear();

        var jobs = (await fixture.DbContext.ProvisioningJobs
            .Where(job => job.PrototypeId == prototype.Id)
            .ToListAsync())
            .OrderBy(job => job.CreatedAt)
            .ToList();

        Assert.Equal(2, jobs.Count);
        Assert.Equal(ProvisioningJobStatus.Succeeded, jobs[0].Status);
        Assert.Equal(ProvisioningJobType.Create, jobs[1].Type);
        Assert.Equal(ProvisioningJobStatus.Succeeded, jobs[1].Status);

        var storedPrototype = await fixture.DbContext.Prototypes.SingleAsync(item => item.Id == prototype.Id);
        Assert.Equal(PrototypeStatus.Live, storedPrototype.Status);
        Assert.False(string.IsNullOrWhiteSpace(storedPrototype.AzureResourceGroupName));
    }

    private static async Task ProcessNextJobAsync(Fixture fixture)
    {
        var processNextJob = typeof(ProvisioningWorker).GetMethod(
            "ProcessNextJobAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(processNextJob);
        await (Task)processNextJob!.Invoke(fixture.Worker, [CancellationToken.None])!;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        private Fixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            Worker = new ProvisioningWorker(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<IOptions<GitHubOptions>>(),
                NullLogger<ProvisioningWorker>.Instance);
        }

        public PrototypeManagerDbContext DbContext => _serviceProvider.GetRequiredService<PrototypeManagerDbContext>();
        public IPrototypeService PrototypeService => _serviceProvider.GetRequiredService<IPrototypeService>();
        public ProvisioningWorker Worker { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<PrototypeManagerDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IJobSecretStore, DbContextJobSecretStore>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IGitHubOrganisationService, GitHubOrganisationService>();
            services.AddScoped<IGitHubTokenIdentityService, PassthroughGitHubTokenIdentityService>();
            services.AddScoped<ICostControlPolicy>(_ => new CostControlPolicy(Options.Create(new CostControlOptions())));
            services.AddScoped<IGitHubRepositoryValidator, FormatOnlyGitHubRepositoryValidator>();
            services.AddScoped<IGitHubAccessValidator, FormatOnlyGitHubAccessValidator>();
            services.AddScoped<IAccessManagementService, AccessManagementService>();
            services.AddScoped<AzureWebAppResourceResolver>();
            services.AddScoped<MockPrototypeWebAppRuntimeService>();
            services.AddScoped<AzurePrototypeWebAppRuntimeService>();
            services.AddScoped<IPrototypeWebAppRuntimeService, PrototypeWebAppRuntimeService>();
            services.AddScoped<IPrototypeSiteAvailabilityChecker, PrototypeSiteAvailabilityChecker>();
            services.AddHttpClient(PrototypeSiteVerifier.HttpClientName);
            services.AddScoped<IPrototypeService, PrototypeService>();
            services.AddScoped<IDeploymentWorkflowGenerator, DeploymentWorkflowGenerator>();
            services.AddScoped<ProvisioningRollbackService>();
            services.AddScoped<IPrototypeAzureDeletionPolicy, PrototypeAzureDeletionPolicy>();
            services.AddScoped<IPrototypeAppSettingsService, PrototypeAppSettingsService>();
            services.AddScoped<AzureWebAppSettingsSyncService>();
            services.AddScoped<IGitHubDeploymentService, TestGitHubDeploymentService>();
            services.AddScoped<MockGitHubRepositoryScaffoldService>();
            services.AddScoped<IGitHubRepositoryScaffoldService, MockGitHubRepositoryScaffoldService>();
            services.AddScoped<MockGitHubRepositoryDeletionService>();
            services.AddScoped<IGitHubRepositoryDeletionService, MockGitHubRepositoryDeletionService>();
            services.AddScoped<IPrototypeProvisioner, MockPrototypeProvisioner>();
            services.AddSingleton(Options.Create(new ProvisioningOptions
            {
                Mode = "Mock",
                AzureSubscriptionId = "local-development",
                Location = "westeurope"
            }));
            services.AddSingleton(Options.Create(new CostControlOptions()));
            services.AddSingleton(Options.Create(new GitHubOptions { AutoConfigureDeployment = false }));

            var provider = services.BuildServiceProvider();
            var context = provider.GetRequiredService<PrototypeManagerDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.BusinessAreas.Add(new BusinessArea
            {
                Id = TestBusinessAreaId,
                Name = "Schools Digital",
                Slug = "schools-digital"
            });
            context.GitHubOrganisations.Add(new GitHubOrganisation
            {
                Id = Guid.NewGuid(),
                Name = "department-for-education",
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "tests"
            });
            await context.SaveChangesAsync();

            return new Fixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestGitHubDeploymentService : IGitHubDeploymentService
    {
        public Task<GitHubDeploymentResult> ConfigureAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubDeploymentResult { Succeeded = true });
    }
}
