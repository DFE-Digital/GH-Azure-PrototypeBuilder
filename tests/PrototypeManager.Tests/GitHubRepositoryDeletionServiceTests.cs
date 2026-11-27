using Microsoft.Extensions.Logging.Abstractions;
using PrototypeManager.Domain;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubRepositoryDeletionServiceTests
{
    [Fact]
    public async Task DeleteScaffoldedRepositoryAsync_DoesNotDeleteAndReturnsManualCleanupMessage()
    {
        var service = new GitHubRepositoryDeletionService(NullLogger<GitHubRepositoryDeletionService>.Instance);

        var message = await service.DeleteScaffoldedRepositoryAsync(new Prototype
        {
            RequiresRepositoryScaffold = true,
            GitHubRepositoryUrl = "https://github.com/DFE-Digital/prototype-xxxx-example"
        });

        Assert.Contains("manual cleanup", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteScaffoldedRepositoryAsync_SkipsHostedPrototypeRepositories()
    {
        var service = new GitHubRepositoryDeletionService(NullLogger<GitHubRepositoryDeletionService>.Instance);

        var message = await service.DeleteScaffoldedRepositoryAsync(new Prototype
        {
            RequiresRepositoryScaffold = false,
            GitHubRepositoryUrl = "https://github.com/department-for-education/existing-repo"
        });

        Assert.Null(message);
    }
}
