using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubAccessValidator(
    GitHubApiClient gitHubApiClient,
    IOptionsMonitor<GitHubOptions> githubOptions,
    ILogger<GitHubAccessValidator> logger) : IGitHubAccessValidator
{
    public async Task ValidateForRepositoryAsync(
        string repositoryUrl,
        CancellationToken cancellationToken = default)
    {
        var (owner, repository) = GitHubRepositoryValidator.ParseRepositoryUrl(repositoryUrl);
        var baseUrl = githubOptions.CurrentValue.ApiBaseUrl.TrimEnd('/');

        var token = gitHubApiClient.RequireToken();

        logger.LogInformation(
            "Validating GitHub token for {Owner}/{Repository}. Token length {TokenLength}, prefix {TokenPrefix}.",
            owner,
            repository,
            token.Length,
            token[..Math.Min(12, token.Length)]);

        using var userResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/user",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(userResponse, "GitHub token validation", cancellationToken);

        using var repositoryResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}",
            cancellationToken: cancellationToken);

        if (repositoryResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ValidationException(
                $"GitHub repository {owner}/{repository} was not found. " +
                "If you are creating a new prototype, retry GitHub repository setup first. " +
                "For fine-grained tokens, set repository access to All repositories and include Contents, Actions, Secrets, and Workflows permissions.");
        }

        await gitHubApiClient.EnsureSuccessAsync(
            repositoryResponse,
            $"repository access to {owner}/{repository}",
            cancellationToken);

        using var secretsKeyResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/actions/secrets/public-key",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(
            secretsKeyResponse,
            $"Actions secrets access to {owner}/{repository}",
            cancellationToken);

        using var workflowsResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/actions/workflows",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(
            workflowsResponse,
            $"Actions workflows access to {owner}/{repository}",
            cancellationToken);

        var (_, workflowBranch) = await GitHubRepositoryMetadata.ResolveWorkflowBranchAsync(
            gitHubApiClient,
            githubOptions.CurrentValue,
            owner,
            repository,
            cancellationToken);

        if (!await GitHubRepositoryMetadata.BranchExistsAsync(
                gitHubApiClient,
                githubOptions.CurrentValue,
                owner,
                repository,
                workflowBranch ?? githubOptions.CurrentValue.WorkflowBranch,
                cancellationToken))
        {
            throw new ValidationException(
                $"The GitHub repository does not have a '{workflowBranch}' branch yet. Push an initial commit to the repository, then try again.");
        }
    }
}

internal sealed class FormatOnlyGitHubAccessValidator : IGitHubAccessValidator
{
    public Task ValidateForRepositoryAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        GitHubRepositoryValidator.ParseRepositoryUrl(repositoryUrl);
        return Task.CompletedTask;
    }
}
