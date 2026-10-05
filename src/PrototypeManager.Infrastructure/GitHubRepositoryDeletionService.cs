using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockGitHubRepositoryDeletionService(
    ILogger<MockGitHubRepositoryDeletionService> logger) : IGitHubRepositoryDeletionService
{
    public Task<string?> DeleteScaffoldedRepositoryAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        if (!prototype.RequiresRepositoryScaffold)
        {
            return Task.FromResult<string?>(null);
        }

        logger.LogInformation(
            "Mock GitHub repository deletion skipped for prototype {PrototypeId}.",
            prototype.Id);

        return Task.FromResult<string?>("GitHub repository deletion skipped in mock mode.");
    }
}

internal sealed class GitHubRepositoryDeletionService(
    GitHubApiClient gitHubApiClient,
    PrototypeManagerDbContext dbContext,
    IOptions<GitHubOptions> githubOptions,
    ILogger<GitHubRepositoryDeletionService> logger) : IGitHubRepositoryDeletionService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    public async Task<string?> DeleteScaffoldedRepositoryAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default)
    {
        if (!prototype.RequiresRepositoryScaffold ||
            string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl))
        {
            return null;
        }

        var scaffoldSucceeded = await dbContext.ProvisioningJobs.AnyAsync(
            job => job.PrototypeId == prototype.Id &&
                   job.Type == ProvisioningJobType.ScaffoldGitHubRepository &&
                   job.Status == ProvisioningJobStatus.Succeeded,
            cancellationToken);

        if (!scaffoldSucceeded)
        {
            logger.LogInformation(
                "Skipping GitHub repository deletion for prototype {PrototypeId} because repository setup did not complete successfully.",
                prototype.Id);
            return null;
        }

        gitHubApiClient.RequireToken();
        var (owner, repository) = GitHubRepositoryValidator.ParseRepositoryUrl(prototype.GitHubRepositoryUrl);

        await GitHubRepositoryDeleter.DeleteAsync(
            gitHubApiClient,
            _githubOptions,
            logger,
            owner,
            repository,
            cancellationToken);

        return $"Deleted GitHub repository {owner}/{repository}.";
    }
}

internal sealed class GitHubRepositoryDeletionServiceSelector(
    MockGitHubRepositoryDeletionService mockService,
    GitHubRepositoryDeletionService liveService,
    IOptions<GitHubOptions> githubOptions) : IGitHubRepositoryDeletionService
{
    private IGitHubRepositoryDeletionService Implementation =>
        string.IsNullOrWhiteSpace(githubOptions.Value.PersonalAccessToken)
            ? mockService
            : liveService;

    public Task<string?> DeleteScaffoldedRepositoryAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.DeleteScaffoldedRepositoryAsync(prototype, cancellationToken);
}
