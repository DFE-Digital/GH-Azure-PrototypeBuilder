using System.Text.Json;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal static class GitHubRepositoryMetadata
{
    public static async Task<(string DefaultBranch, string? WorkflowBranch)> ResolveWorkflowBranchAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var baseUrl = githubOptions.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(response, $"read repository metadata for {owner}/{repository}", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var defaultBranch = document.RootElement.TryGetProperty("default_branch", out var branchElement)
            ? branchElement.GetString() ?? githubOptions.WorkflowBranch
            : githubOptions.WorkflowBranch;

        var configuredBranch = githubOptions.WorkflowBranch;

        if (string.Equals(configuredBranch, defaultBranch, StringComparison.OrdinalIgnoreCase))
        {
            return (defaultBranch, defaultBranch);
        }

        if (await BranchExistsAsync(gitHubApiClient, githubOptions, owner, repository, configuredBranch, cancellationToken))
        {
            return (defaultBranch, configuredBranch);
        }

        return (defaultBranch, defaultBranch);
    }

    public static async Task<bool> BranchExistsAsync(
        GitHubApiClient gitHubApiClient,
        GitHubOptions githubOptions,
        string owner,
        string repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var baseUrl = githubOptions.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/branches/{Uri.EscapeDataString(branch)}",
            cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode;
    }
}
