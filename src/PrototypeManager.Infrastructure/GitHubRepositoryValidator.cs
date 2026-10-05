using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubRepositoryValidator(
    GitHubApiClient gitHubApiClient,
    IGitHubOrganisationService githubOrganisationService,
    IOptions<GitHubOptions> githubOptions) : IGitHubRepositoryValidator
{
    private readonly GitHubOptions _options = githubOptions.Value;

    public async Task ValidateRepositoryAccessAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        await githubOrganisationService.ValidateRepositoryUrlAsync(repositoryUrl, cancellationToken);
        var (owner, repo) = ParseRepositoryUrl(repositoryUrl);

        if (!_options.ValidateWithApi)
        {
            return;
        }

        var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repo}",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (string.IsNullOrWhiteSpace(GitHubTokenNormalizer.Normalize(_options.PersonalAccessToken)))
            {
                throw new ValidationException(
                    "This GitHub repository could not be found. If it is private, configure a GitHub personal access token with access to the repository.");
            }

            throw new ValidationException("This GitHub repository could not be found or your GitHub token cannot access it.");
        }

        await gitHubApiClient.EnsureSuccessAsync(response, "validate GitHub repository access", cancellationToken);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("private", out var isPrivate) &&
            isPrivate.GetBoolean() &&
            string.IsNullOrWhiteSpace(GitHubTokenNormalizer.Normalize(_options.PersonalAccessToken)))
        {
            throw new ValidationException(
                "This is a private GitHub repository. Configure a GitHub personal access token with access to the repository.");
        }
    }

    internal static (string Owner, string Repository) ParseRepositoryUrl(string repositoryUrl) =>
        GitHubRepositoryUrlParser.Parse(repositoryUrl);
}

internal sealed class FormatOnlyGitHubRepositoryValidator(
    IGitHubOrganisationService githubOrganisationService) : IGitHubRepositoryValidator
{
    public async Task ValidateRepositoryAccessAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        GitHubRepositoryUrlParser.Parse(repositoryUrl);
        await githubOrganisationService.ValidateRepositoryUrlAsync(repositoryUrl, cancellationToken);
    }
}
