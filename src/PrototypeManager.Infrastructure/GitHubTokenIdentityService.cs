using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubTokenIdentityService(
    GitHubApiClient gitHubApiClient,
    IOptions<GitHubOptions> githubOptions) : IGitHubTokenIdentityService
{
    private readonly GitHubOptions _githubOptions = githubOptions.Value;

    public async Task<string?> TryGetAuthenticatedLoginAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(GitHubTokenNormalizer.Normalize(_githubOptions.PersonalAccessToken)))
        {
            return null;
        }

        return await GetAuthenticatedLoginAsync(cancellationToken);
    }

    public async Task<string> ResolveRepositoryOwnerAsync(string selectedOwner, CancellationToken cancellationToken = default)
    {
        var owner = selectedOwner.Trim();
        var authenticatedLogin = await GetAuthenticatedLoginAsync(cancellationToken);

        if (string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase))
        {
            return authenticatedLogin;
        }

        if (await IsOrganisationAsync(owner, cancellationToken))
        {
            return owner;
        }

        if (await IsUserAccountAsync(owner, cancellationToken))
        {
            throw new ValidationException(
                $"The GitHub token belongs to {authenticatedLogin}. " +
                $"Personal repositories can only be created in that account, not {owner}. " +
                "Select your GitHub account from the list, or use a token for the selected account.");
        }

        throw new ValidationException(
            $"GitHub organisation or account '{owner}' was not found, or the personal access token cannot access it. " +
            $"Check the name in Admin and authorize the token for that organisation. " +
            $"The configured token belongs to {authenticatedLogin}.");
    }

    internal async Task<string> GetAuthenticatedLoginAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/user",
            cancellationToken: cancellationToken);

        await gitHubApiClient.EnsureSuccessAsync(response, "read GitHub user profile", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("login", out var loginElement)
            ? loginElement.GetString() ?? string.Empty
            : string.Empty;
    }

    internal async Task<bool> IsOrganisationAsync(string owner, CancellationToken cancellationToken = default)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/orgs/{owner}",
            cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode;
    }

    internal async Task<bool> IsUserAccountAsync(string owner, CancellationToken cancellationToken = default)
    {
        var baseUrl = _githubOptions.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/users/{owner}",
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("type", out var typeElement) &&
               string.Equals(typeElement.GetString(), "User", StringComparison.OrdinalIgnoreCase);
    }
}
