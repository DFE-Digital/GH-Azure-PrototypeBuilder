using System.Net;
using Microsoft.Extensions.Logging;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal static class GitHubRepositoryDeleter
{
    public static async Task DeleteAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        ILogger logger,
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = githubOptions.ApiBaseUrl.TrimEnd('/');
        var deleteUrl = $"{baseUrl}/repos/{owner}/{repository}";

        logger.LogInformation(
            "Deleting GitHub repository {Owner}/{Repository}.",
            owner,
            repository);

        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Delete,
            deleteUrl,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "GitHub repository {Owner}/{Repository} was already deleted.",
                owner,
                repository);
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Unable to delete GitHub repository {owner}/{repository}. " +
                "The configured personal access token needs Administration permission for this repository.");
        }

        await gitHubApiClient.EnsureSuccessAsync(response, "delete GitHub repository", cancellationToken);

        logger.LogInformation(
            "Deleted GitHub repository {Owner}/{Repository}.",
            owner,
            repository);
    }
}
