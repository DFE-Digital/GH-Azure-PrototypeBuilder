using PrototypeManager.Application;

namespace PrototypeManager.Tests;

internal sealed class PassthroughGitHubTokenIdentityService : IGitHubTokenIdentityService
{
    public Task<string?> TryGetAuthenticatedLoginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string> ResolveRepositoryOwnerAsync(string selectedOwner, CancellationToken cancellationToken = default) =>
        Task.FromResult(selectedOwner.Trim());
}
