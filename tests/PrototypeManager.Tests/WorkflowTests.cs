using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class WorkflowTests
{
    private static readonly Guid TestBusinessAreaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateAsync_CreatesPrototypeAndQueuedJob()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var prototype = await fixture.PrototypeService.CreateAsync(
            CreateRequest(),
            "creator@education.gov.uk");

        var storedPrototype = await fixture.DbContext.Prototypes.SingleAsync(x => x.Id == prototype.Id);
        var job = await fixture.DbContext.ProvisioningJobs.SingleAsync(x => x.PrototypeId == prototype.Id);
        var audits = await fixture.DbContext.AuditEvents.Where(x => x.PrototypeId == prototype.Id).ToListAsync();

        Assert.Equal(PrototypeStatus.Requested, storedPrototype.Status);
        Assert.Equal(ProvisioningJobType.Create, job.Type);
        Assert.Equal(ProvisioningJobStatus.Queued, job.Status);
        Assert.Contains(audits, audit => audit.Action == "Prototype requested");
        Assert.Contains(audits, audit => audit is { Action: ProvisioningTasks.CompletedAction, Details: ProvisioningTasks.Queued });
    }

    [Fact]
    public async Task CreateScaffoldedAsync_CreatesPrototypeScaffoldAndCreateJobs()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var prototype = await fixture.PrototypeService.CreateScaffoldedAsync(
            new CreateScaffoldedPrototypeRequest
            {
                AppName = "new-kit-proto",
                BusinessAreaId = TestBusinessAreaId,
                GitHubOrganisation = "department-for-education",
                GitHubRepositoryName = "new-kit-proto",
                PrototypePassword = "abc",
                DeclarationAgreed = true
            },
            "creator@education.gov.uk");

        var storedPrototype = await fixture.DbContext.Prototypes.SingleAsync(x => x.Id == prototype.Id);
        var jobs = (await fixture.DbContext.ProvisioningJobs
            .Where(x => x.PrototypeId == prototype.Id)
            .ToListAsync())
            .OrderBy(x => x.CreatedAt)
            .ToList();

        Assert.True(storedPrototype.RequiresRepositoryScaffold);
        Assert.Equal("https://github.com/department-for-education/new-kit-proto", storedPrototype.GitHubRepositoryUrl);
        Assert.Single(jobs);
        Assert.Equal(ProvisioningJobType.ScaffoldGitHubRepository, jobs[0].Type);
        Assert.False(string.IsNullOrWhiteSpace(jobs[0].SecretReference));
    }

    [Fact]
    public async Task CreateScaffoldedAsync_QueuesHostingAfterScaffoldCompletes()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var prototype = await fixture.PrototypeService.CreateScaffoldedAsync(
            new CreateScaffoldedPrototypeRequest
            {
                AppName = "queued-hosting-proto",
                BusinessAreaId = TestBusinessAreaId,
                GitHubOrganisation = "department-for-education",
                GitHubRepositoryName = "queued-hosting-proto",
                PrototypePassword = "abc",
                DeclarationAgreed = true
            },
            "creator@education.gov.uk");

        await ProcessNextJobAsync(fixture);

        fixture.DbContext.ChangeTracker.Clear();
        var jobs = (await fixture.DbContext.ProvisioningJobs
                .Where(job => job.PrototypeId == prototype.Id)
                .ToListAsync())
            .OrderBy(job => job.CreatedAt)
            .ToList();

        Assert.Equal(2, jobs.Count);
        Assert.Equal(ProvisioningJobStatus.Succeeded, jobs[0].Status);
        Assert.Equal(ProvisioningJobType.ScaffoldGitHubRepository, jobs[0].Type);
        Assert.Equal(ProvisioningJobType.Create, jobs[1].Type);
        Assert.Equal(ProvisioningJobStatus.Queued, jobs[1].Status);
        Assert.Equal(jobs[0].SecretReference, jobs[1].SecretReference);
    }

    [Fact]
    public async Task Worker_RunsScaffoldBeforeQueuedHostingForLegacyJobs()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var prototype = new Prototype
        {
            Name = "legacy-scaffold-order",
            Slug = "legacy-scaffold-order",
            BusinessAreaId = TestBusinessAreaId,
            ServiceName = "legacy-scaffold-order",
            Team = "DesignOps",
            Directorate = "Digital",
            OwnerEmail = "owner@education.gov.uk",
            GitHubRepositoryUrl = "https://github.com/department-for-education/legacy-scaffold-order",
            RequiresRepositoryScaffold = true,
            AzureSubscriptionId = "local-development",
            Status = PrototypeStatus.Requested,
            CreatedBy = "owner@education.gov.uk",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            Reason = "Legacy queue ordering test",
            Region = "westeurope",
            Sku = "F1"
        };

        fixture.DbContext.Prototypes.Add(prototype);
        var secretReference = await fixture.SecretStore.SaveAsync("very-secure-password");
        var createdAt = DateTimeOffset.UtcNow;

        fixture.DbContext.ProvisioningJobs.AddRange(
            new ProvisioningJob
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.ScaffoldGitHubRepository,
                Status = ProvisioningJobStatus.Queued,
                CreatedAt = createdAt,
                SecretReference = secretReference
            },
            new ProvisioningJob
            {
                PrototypeId = prototype.Id,
                Type = ProvisioningJobType.Create,
                Status = ProvisioningJobStatus.Queued,
                CreatedAt = createdAt.AddMilliseconds(1),
                SecretReference = secretReference
            });

        await fixture.DbContext.SaveChangesAsync();

        await ProcessNextJobAsync(fixture);

        fixture.DbContext.ChangeTracker.Clear();
        var scaffoldJob = await fixture.DbContext.ProvisioningJobs.SingleAsync(job =>
            job.PrototypeId == prototype.Id && job.Type == ProvisioningJobType.ScaffoldGitHubRepository);
        var createJob = await fixture.DbContext.ProvisioningJobs.SingleAsync(job =>
            job.PrototypeId == prototype.Id && job.Type == ProvisioningJobType.Create);

        Assert.Equal(ProvisioningJobStatus.Succeeded, scaffoldJob.Status);
        Assert.Equal(ProvisioningJobStatus.Queued, createJob.Status);
    }

    [Fact]
    public async Task DeleteAsync_QueuesDeletionJob()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prototype = await fixture.PrototypeService.CreateAsync(CreateRequest(), "creator@education.gov.uk");

        await fixture.PrototypeService.DeleteAsync(prototype.Id, "creator@education.gov.uk");

        var storedPrototype = await fixture.DbContext.Prototypes.SingleAsync(x => x.Id == prototype.Id);
        var deleteJob = await fixture.DbContext.ProvisioningJobs
            .Where(x => x.PrototypeId == prototype.Id && x.Type == ProvisioningJobType.Delete)
            .SingleAsync();

        Assert.Equal(PrototypeStatus.Deleting, storedPrototype.Status);
        Assert.Equal(ProvisioningJobStatus.Queued, deleteJob.Status);
    }

    [Fact]
    public async Task ConfigureGitHubDeploymentAsync_QueuesJob()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var prototype = new Prototype
        {
            Name = "Live prototype",
            Slug = "live-prototype",
            BusinessAreaId = TestBusinessAreaId,
            ServiceName = "Live prototype",
            Team = "DesignOps",
            Directorate = "Digital",
            OwnerEmail = "owner@education.gov.uk",
            GitHubRepositoryUrl = "https://github.com/department-for-education/live-prototype",
            AzureSubscriptionId = "local-development",
            AzureResourceGroupName = "s186d01-pt-rg-live-prototype",
            AzureWebAppName = "s186d01-pt-app-live-prototype",
            Status = PrototypeStatus.Live,
            CreatedBy = "owner@education.gov.uk",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            Reason = "GitHub deployment test",
            Region = "westeurope",
            Sku = "F1"
        };

        fixture.DbContext.Prototypes.Add(prototype);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.PrototypeService.ConfigureGitHubDeploymentAsync(prototype.Id, "owner@education.gov.uk");

        var job = await fixture.DbContext.ProvisioningJobs.SingleAsync(x =>
            x.PrototypeId == prototype.Id && x.Type == ProvisioningJobType.ConfigureGitHubDeployment);

        Assert.Equal(ProvisioningJobStatus.Queued, job.Status);
    }

    [Fact]
    public void DeploymentWorkflowGenerator_UsesPublishProfileSecret()
    {
        var generator = new DeploymentWorkflowGenerator();
        var workflow = generator.Generate(
            new Prototype { AzureWebAppName = "s186d01-pt-app-example" },
            new DeploymentWorkflowOptions { UsePublishProfile = true });

        Assert.Contains("publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}", workflow);
        Assert.DoesNotContain("azure/login@v2", workflow);
    }

    [Fact]
    public async Task Worker_RecordsFailedProvisioning()
    {
        await using var fixture = await TestFixture.CreateAsync(useFailingProvisioner: true);

        var prototype = new Prototype
        {
            Name = "Failure prototype",
            Slug = "failure-prototype",
            BusinessAreaId = TestBusinessAreaId,
            ServiceName = "Failure prototype",
            Team = "DesignOps",
            Directorate = "Digital",
            OwnerEmail = "owner@education.gov.uk",
            DeputyOwnerEmail = "deputy@education.gov.uk",
            GitHubRepositoryUrl = "https://github.com/department-for-education/failure-prototype",
            AzureSubscriptionId = "local-development",
            CreatedBy = "owner@education.gov.uk",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            Reason = "Failure path test",
            Region = "westeurope",
            Sku = "F1"
        };

        fixture.DbContext.Prototypes.Add(prototype);
        var secretReference = await fixture.SecretStore.SaveAsync("very-secure-password");
        fixture.DbContext.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototype.Id,
            Type = ProvisioningJobType.Create,
            Status = ProvisioningJobStatus.Queued,
            SecretReference = secretReference
        });
        await fixture.DbContext.SaveChangesAsync();

        var processNextJob = typeof(ProvisioningWorker).GetMethod("ProcessNextJobAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(processNextJob);
        await (Task)processNextJob.Invoke(fixture.Worker, [CancellationToken.None])!;

        fixture.DbContext.ChangeTracker.Clear();
        var failedPrototype = await fixture.DbContext.Prototypes.SingleAsync(x => x.Id == prototype.Id);
        var job = await fixture.DbContext.ProvisioningJobs.SingleAsync(x => x.PrototypeId == prototype.Id);
        var audits = await fixture.DbContext.AuditEvents.Where(x => x.PrototypeId == prototype.Id).ToListAsync();

        Assert.Equal(PrototypeStatus.Failed, failedPrototype.Status);
        Assert.Equal(ProvisioningJobStatus.Failed, job.Status);
        Assert.Contains(audits, x => x.Action.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ProcessNextJobAsync(TestFixture fixture)
    {
        var processNextJob = typeof(ProvisioningWorker).GetMethod(
            "ProcessNextJobAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(processNextJob);
        await (Task)processNextJob.Invoke(fixture.Worker, [CancellationToken.None])!;
    }

    [Fact]
    public async Task AddManagerAsync_AddsContactAndAllowsManagerAccess()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prototype = await CreateStoredPrototypeAsync(fixture);

        await fixture.PrototypeService.AddManagerAsync(
            prototype.Id,
            "manager@education.gov.uk",
            prototype.OwnerEmail,
            isAdmin: false,
            isSupport: false);

        var contacts = await fixture.PrototypeService.GetContactsAsync(prototype.Id);
        var managerView = await fixture.PrototypeService.GetAccessibleAsync(
            prototype.Id,
            "manager@education.gov.uk",
            isAdmin: false,
            isSupport: false);

        Assert.Single(contacts);
        Assert.Equal("manager@education.gov.uk", contacts[0].Email);
        Assert.NotNull(managerView);
    }

    [Fact]
    public async Task AddManagerAsync_RejectsNonOwner()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prototype = await CreateStoredPrototypeAsync(fixture);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.PrototypeService.AddManagerAsync(
                prototype.Id,
                "manager@education.gov.uk",
                "not-the-owner@education.gov.uk",
                isAdmin: false,
                isSupport: false));

        Assert.Contains("Only the prototype owner can manage contacts.", exception.Message);
    }

    [Fact]
    public async Task TransferOwnershipAsync_TransfersToMemberAndDemotesPreviousOwner()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prototype = await CreateStoredPrototypeAsync(fixture);

        await fixture.PrototypeService.AddManagerAsync(
            prototype.Id,
            "manager@education.gov.uk",
            prototype.OwnerEmail,
            isAdmin: false,
            isSupport: false);

        await fixture.PrototypeService.TransferOwnershipAsync(
            prototype.Id,
            "manager@education.gov.uk",
            prototype.OwnerEmail,
            isAdmin: false,
            isSupport: false);

        var updated = await fixture.PrototypeService.GetAsync(prototype.Id);
        var contacts = await fixture.PrototypeService.GetContactsAsync(prototype.Id);

        Assert.NotNull(updated);
        Assert.Equal("manager@education.gov.uk", updated.OwnerEmail);
        Assert.Single(contacts);
        Assert.Equal("owner@education.gov.uk", contacts[0].Email);
    }

    [Fact]
    public async Task ExtendExpiryAsync_RejectsDatesMoreThanTwelveMonthsAway()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var prototype = await CreateStoredPrototypeAsync(fixture);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.PrototypeService.ExtendExpiryAsync(
                prototype.Id,
                DateTimeOffset.UtcNow.AddDays(366),
                prototype.OwnerEmail));

        Assert.Contains("12 months", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Prototype> CreateStoredPrototypeAsync(TestFixture fixture)
    {
        var prototype = new Prototype
        {
            Name = "Contacts prototype",
            Slug = "contacts-prototype",
            BusinessAreaId = TestBusinessAreaId,
            ServiceName = "Contacts prototype",
            Team = "DesignOps",
            Directorate = "Digital",
            OwnerEmail = "owner@education.gov.uk",
            GitHubRepositoryUrl = "https://github.com/department-for-education/contacts-prototype",
            AzureSubscriptionId = "local-development",
            Status = PrototypeStatus.Live,
            CreatedBy = "owner@education.gov.uk",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            Reason = "Contacts test",
            Region = "westeurope",
            Sku = "F1"
        };

        fixture.DbContext.Prototypes.Add(prototype);
        await fixture.DbContext.SaveChangesAsync();
        return prototype;
    }

    private static CreatePrototypeRequest CreateRequest() => new()
    {
        AppName = "teacher-contacts",
        BusinessAreaId = TestBusinessAreaId,
        Project = "Teacher contacts",
        GitHubRepositoryUrl = "https://github.com/department-for-education/teacher-contacts-alpha",
        DeclarationAgreed = true,
        PrototypePassword = "very-secure-password"
    };

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;
        private readonly ProvisioningWorker _worker;

        private TestFixture(SqliteConnection connection, ServiceProvider serviceProvider)
        {
            _connection = connection;
            _serviceProvider = serviceProvider;
            _worker = new ProvisioningWorker(
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<IOptions<GitHubOptions>>(),
                NullLogger<ProvisioningWorker>.Instance);
        }

        public PrototypeManagerDbContext DbContext => _serviceProvider.GetRequiredService<PrototypeManagerDbContext>();
        public IPrototypeService PrototypeService => _serviceProvider.GetRequiredService<IPrototypeService>();
        public InMemoryJobSecretStore SecretStore => (InMemoryJobSecretStore)_serviceProvider.GetRequiredService<IJobSecretStore>();
        public ProvisioningWorker Worker => _worker;

        public static async Task<TestFixture> CreateAsync(bool useFailingProvisioner = false)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<PrototypeManagerDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IJobSecretStore, InMemoryJobSecretStore>();
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
            services.AddScoped<IGitHubDeploymentService, NoOpGitHubDeploymentService>();
            services.AddScoped<MockGitHubRepositoryScaffoldService>();
            services.AddScoped<IGitHubRepositoryScaffoldService, MockGitHubRepositoryScaffoldService>();
            services.AddScoped<MockGitHubRepositoryDeletionService>();
            services.AddScoped<IGitHubRepositoryDeletionService, MockGitHubRepositoryDeletionService>();
            services.AddScoped<IPrototypeProvisioner>(sp =>
                useFailingProvisioner
                    ? new FailingProvisioner()
                    : new MockPrototypeProvisioner(
                        sp.GetRequiredService<IOptions<ProvisioningOptions>>(),
                        sp.GetRequiredService<IOptions<CostControlOptions>>(),
                        sp.GetRequiredService<IAuditService>(),
                        sp.GetRequiredService<ILogger<MockPrototypeProvisioner>>()));
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

            return new TestFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await _serviceProvider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NoOpGitHubDeploymentService : IGitHubDeploymentService
    {
        public Task<GitHubDeploymentResult> ConfigureAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubDeploymentResult
            {
                Succeeded = true,
                Message = "GitHub deployment configured in test."
            });
    }

    private sealed class FailingProvisioner : IPrototypeProvisioner
    {
        public Task<ProvisioningResult> CreatePrototypeAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provisioning failed.");

        public Task DeletePrototypeAsync(Prototype prototype, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ProvisioningResult> UpdatePasswordAsync(Prototype prototype, string prototypePassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProvisioningResult());
    }
}
