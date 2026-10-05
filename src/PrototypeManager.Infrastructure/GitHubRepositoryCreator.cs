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
        var authenticatedLogin = await identityService.GetAuthenticatedLoginAsync(cancellationToken);
        var createUrl = await ResolveCreateUrlAsync(
            identityService,
            baseUrl,
            owner,
            authenticatedLogin,
            cancellationToken);

        logger.LogInformation(
            "Creating GitHub repository {Owner}/{Repository} using token for {AuthenticatedLogin} via {CreateUrl}.",
            owner,
            repository,
            authenticatedLogin,
            createUrl);

        using var requestContent = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["name"] = repository,
                ["description"] = "GOV.UK Prototype Kit prototype created via Prototype Manager",
                ["private"] = true,
                ["auto_init"] = false
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
                "Created GitHub repository {Owner}/{Repository} using token for {AuthenticatedLogin}.",
                owner,
                repository,
                authenticatedLogin);
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
            throw GitHubApiErrors.CreateRepositoryException(response.StatusCode, owner, responseBody);
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
        string baseUrl,
        string owner,
        string authenticatedLogin,
        CancellationToken cancellationToken)
    {
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
