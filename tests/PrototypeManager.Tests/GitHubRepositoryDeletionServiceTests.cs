using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubRepositoryDeletionServiceTests
{
    [Fact]
    public async Task MockDeleteScaffoldedRepositoryAsync_SkipsHostedPrototypeRepositories()
    {
        var service = new MockGitHubRepositoryDeletionService(NullLogger<MockGitHubRepositoryDeletionService>.Instance);

        var message = await service.DeleteScaffoldedRepositoryAsync(new Prototype
        {
            RequiresRepositoryScaffold = false,
            GitHubRepositoryUrl = "https://github.com/department-for-education/existing-repo"
        });

        Assert.Null(message);
    }

    [Fact]
    public async Task DeleteScaffoldedRepositoryAsync_SkipsWhenScaffoldDidNotSucceed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PrototypeManagerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new PrototypeManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var prototypeId = Guid.NewGuid();
        context.Prototypes.Add(new Prototype
        {
            Id = prototypeId,
            Name = "failed-scaffold",
            Slug = "failed-scaffold",
            RequiresRepositoryScaffold = true,
            GitHubRepositoryUrl = "https://github.com/andyjones81/failed-scaffold"
        });
        context.ProvisioningJobs.Add(new ProvisioningJob
        {
            PrototypeId = prototypeId,
            Type = ProvisioningJobType.ScaffoldGitHubRepository,
            Status = ProvisioningJobStatus.Failed
        });
        await context.SaveChangesAsync();

        var service = new GitHubRepositoryDeletionService(
            new GitHubApiClient(new TestHttpClientFactory(), new TestOptionsMonitor<Application.GitHubOptions>(
                new Application.GitHubOptions { PersonalAccessToken = "token" })),
            context,
            Options.Create(new Application.GitHubOptions { PersonalAccessToken = "token" }),
            NullLogger<GitHubRepositoryDeletionService>.Instance);

        var message = await service.DeleteScaffoldedRepositoryAsync(
            await context.Prototypes.SingleAsync(prototype => prototype.Id == prototypeId));

        Assert.Null(message);
    }

    [Fact]
    public async Task MockDeleteScaffoldedRepositoryAsync_ReturnsMessageForScaffoldedPrototype()
    {
        var service = new MockGitHubRepositoryDeletionService(NullLogger<MockGitHubRepositoryDeletionService>.Instance);

        var message = await service.DeleteScaffoldedRepositoryAsync(new Prototype
        {
            RequiresRepositoryScaffold = true,
            GitHubRepositoryUrl = "https://github.com/andyjones81/kit-prototype"
        });

        Assert.Contains("mock mode", message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
