using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal static class GitHubRepositoryCreator
{
    public static async Task CreateAsync(
        GitHubApiClient gitHubApiClient,
        GitHubTokenIdentityService identityService,
        GitHubOptions githubOptions,
        ILogger logger,
        string owner,
        string repository,
        CancellationToken cancellationToken = default,
        bool allowExisting = false)
    {
        var baseUrl = githubOptions.ApiBaseUrl.TrimEnd('/');
        var createUrl = await ResolveCreateUrlAsync(
            identityService,
            githubOptions,
            baseUrl,
            owner,
            cancellationToken);

        logger.LogInformation(
            "Creating GitHub repository {Owner}/{Repository} via {CreateUrl} using {AuthMode}.",
            owner,
            repository,
            createUrl,
            githubOptions.HasGitHubAppCredentials ? "GitHubApp" : "PersonalAccessToken");

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["name"] = repository,
                ["description"] = "GOV.UK Prototype Kit prototype created via Prototype Manager",
                ["private"] = true,
                // Required so the Git Data API can create blobs; empty repos return 409.
                ["auto_init"] = true
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Post,
            createUrl,
            requestContent,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation(
                "Created GitHub repository {Owner}/{Repository}.",
                owner,
                repository);
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity &&
            responseBody.Contains("name already exists", StringComparison.OrdinalIgnoreCase))
        {
            if (allowExisting)
            {
                logger.LogInformation(
                    "GitHub repository {Owner}/{Repository} already exists; continuing scaffold.",
                    owner,
                    repository);
                return;
            }

            throw new ValidationException(
                $"A GitHub repository named '{repository}' already exists in {owner}. Choose a different repository name.");
        }

        if (createUrl.Contains("/orgs/", StringComparison.Ordinal))
        {
            throw GitHubApiErrors.CreateRepositoryException(
                response.StatusCode,
                owner,
                responseBody,
                githubOptions.HasGitHubAppCredentials);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new ValidationException(
                $"Unable to create GitHub repository {owner}/{repository} ({(int)response.StatusCode}). " +
                "For fine-grained tokens, set repository access to All repositories and grant Administration permission for personal accounts.");
        }

        await gitHubApiClient.EnsureSuccessAsync(response, "create GitHub repository", cancellationToken);
    }

    public static string ResolveGitHost(GitHubOptions githubOptions)
    {
        if (!Uri.TryCreate(githubOptions.ApiBaseUrl, UriKind.Absolute, out var apiUri))
        {
            return "github.com";
        }

        if (string.Equals(apiUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return "github.com";
        }

        return apiUri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase)
            ? apiUri.Host[4..]
            : apiUri.Host;
    }

    private static async Task<string> ResolveCreateUrlAsync(
        GitHubTokenIdentityService identityService,
        GitHubOptions githubOptions,
        string baseUrl,
        string owner,
        CancellationToken cancellationToken)
    {
        // GitHub App installation tokens cannot call /user or /user/repos.
        if (githubOptions.HasGitHubAppCredentials)
        {
            if (!await identityService.IsOrganisationAsync(owner, cancellationToken))
            {
                throw new ValidationException(
                    $"GitHub organisation '{owner}' was not found, or the GitHub App installation cannot access it. " +
                    "Confirm the organisation is listed in Admin and that the app is installed on that organisation.");
            }

            return $"{baseUrl}/orgs/{owner}/repos";
        }

        var authenticatedLogin = await identityService.GetAuthenticatedLoginAsync(cancellationToken);

        if (string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}/user/repos";
        }

        if (await identityService.IsOrganisationAsync(owner, cancellationToken))
        {
            return $"{baseUrl}/orgs/{owner}/repos";
        }

        if (await identityService.IsUserAccountAsync(owner, cancellationToken))
        {
            throw new ValidationException(
                $"The GitHub token belongs to {authenticatedLogin}. " +
                $"Personal repositories can only be created in that account, not {owner}.");
        }

        throw new ValidationException(
            $"GitHub organisation or account '{owner}' was not found, or the personal access token cannot access it. " +
            $"The configured token belongs to {authenticatedLogin}.");
    }
}
