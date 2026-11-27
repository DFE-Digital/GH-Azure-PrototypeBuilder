using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubApiClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GitHubOptions> githubOptions,
    GitHubInstallationTokenProvider installationTokenProvider)
{
    public const string HttpClientName = "GitHubApi";

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        GetAccessTokenAsync(forceRefresh: false, cancellationToken);

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        var options = githubOptions.CurrentValue;

        if (options.HasGitHubAppCredentials)
        {
            return await installationTokenProvider.GetTokenAsync(forceRefresh, cancellationToken);
        }

        var token = GitHubTokenNormalizer.Normalize(options.PersonalAccessToken);
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        throw new ValidationException(
            "GitHub automation requires a GitHub App (AppId, InstallationId, PrivateKey) or GitHub:PersonalAccessToken.");
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        // Buffer body so we can retry once with a fresh installation token after permission updates.
        byte[]? contentBytes = null;
        MediaTypeHeaderValue? contentType = null;
        if (content is not null)
        {
            contentBytes = await content.ReadAsByteArrayAsync(cancellationToken);
            contentType = content.Headers.ContentType;
        }

        var response = await SendOnceAsync(method, requestUri, contentBytes, contentType, forceRefresh: false, cancellationToken);

        if ((response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) &&
            githubOptions.CurrentValue.HasGitHubAppCredentials)
        {
            response.Dispose();
            response = await SendOnceAsync(method, requestUri, contentBytes, contentType, forceRefresh: true, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string requestUri,
        byte[]? contentBytes,
        MediaTypeHeaderValue? contentType,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(forceRefresh, cancellationToken);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        if (contentBytes is not null)
        {
            request.Content = new ByteArrayContent(contentBytes);
            if (contentType is not null)
            {
                request.Content.Headers.ContentType = contentType;
            }
        }

        return await client.SendAsync(request, cancellationToken);
    }

    public async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var acceptedPermissions = ReadAcceptedPermissions(response);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            throw GitHubApiErrors.CreateValidationException(
                response.StatusCode,
                operation,
                body,
                acceptedPermissions);
        }

        throw new InvalidOperationException($"{operation} failed. GitHub API returned {(int)response.StatusCode}: {body}");
    }

    private static string? ReadAcceptedPermissions(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Accepted-GitHub-Permissions", out var values))
        {
            return null;
        }

        var joined = string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
