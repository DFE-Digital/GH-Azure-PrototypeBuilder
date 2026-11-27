using Microsoft.Extensions.Logging;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

/// <summary>
/// GitHub repository deletion is intentionally disabled. Scaffolded repositories are
/// tracked for manual cleanup in Admin after the Prototype Manager entry is removed.
/// </summary>
internal sealed class GitHubRepositoryDeletionService(
    ILogger<GitHubRepositoryDeletionService> logger) : IGitHubRepositoryDeletionService
{
    public Task<string?> DeleteScaffoldedRepositoryAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default)
    {
        if (!prototype.RequiresRepositoryScaffold ||
            string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl))
        {
            return Task.FromResult<string?>(null);
        }

        logger.LogInformation(
            "Skipping GitHub repository deletion for {RepositoryUrl}. Manual cleanup is required.",
            prototype.GitHubRepositoryUrl);

        return Task.FromResult<string?>(
            "GitHub repository was left in place for manual cleanup.");
    }
}

// Kept for DI compatibility with older registrations/tests.
internal sealed class MockGitHubRepositoryDeletionService(
    ILogger<MockGitHubRepositoryDeletionService> logger) : IGitHubRepositoryDeletionService
{
    public Task<string?> DeleteScaffoldedRepositoryAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default)
    {
        if (!prototype.RequiresRepositoryScaffold ||
            string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl))
        {
            return Task.FromResult<string?>(null);
        }

        logger.LogInformation(
            "Skipping GitHub repository deletion for prototype {PrototypeId} (manual cleanup required).",
            prototype.Id);

        return Task.FromResult<string?>(
            "GitHub repository was left in place for manual cleanup.");
    }
}

internal sealed class GitHubRepositoryDeletionServiceSelector(
    GitHubRepositoryDeletionService liveService) : IGitHubRepositoryDeletionService
{
    public Task<string?> DeleteScaffoldedRepositoryAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default) =>
        liveService.DeleteScaffoldedRepositoryAsync(prototype, cancellationToken);
}
