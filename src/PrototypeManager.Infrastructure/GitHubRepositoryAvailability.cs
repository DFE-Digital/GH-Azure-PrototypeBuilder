using System.Net;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubRepositoryAvailability(
    GitHubApiClient gitHubApiClient,
    IOptions<GitHubOptions> githubOptions) : IGitHubRepositoryAvailability
{
    private readonly GitHubOptions _options = githubOptions.Value;

    public async Task<bool> ExistsAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured || !_options.ValidateWithApi)
        {
            return false;
        }

        try
        {
            var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
            using var response = await gitHubApiClient.SendAsync(
                HttpMethod.Get,
                $"{baseUrl}/repos/{owner}/{repositoryName}",
                cancellationToken: cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class NullGitHubRepositoryAvailability : IGitHubRepositoryAvailability
{
    public Task<bool> ExistsAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class NullCompassCatalogService : ICompassCatalogService
{
    public bool IsConfigured => false;

    public Task<IReadOnlyList<CompassWorkItemOption>> GetActiveWorkItemsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompassWorkItemOption>>([]);

    public Task<IReadOnlyList<CompassProductOption>> GetProductsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompassProductOption>>([]);

    public Task<CompassWorkItemOption?> FindWorkItemAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CompassWorkItemOption?>(null);

    public Task<CompassProductOption?> FindProductAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CompassProductOption?>(null);
}
