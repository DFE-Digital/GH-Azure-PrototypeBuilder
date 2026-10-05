using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubApiClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<GitHubOptions> githubOptions)
{
    public const string HttpClientName = "GitHubApi";

    public string RequireToken()
    {
        var token = GitHubTokenNormalizer.Normalize(githubOptions.CurrentValue.PersonalAccessToken);

        if (string.IsNullOrEmpty(token))
        {
            throw new ValidationException(
                "GitHub deployment automation requires GitHub:PersonalAccessToken with repository, workflow and secrets access.");
        }

        return token;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        var token = RequireToken();
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        if (content is not null)
        {
            request.Content = content;
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

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            throw GitHubApiErrors.CreateValidationException(response.StatusCode, operation, body);
        }

        throw new InvalidOperationException($"{operation} failed. GitHub API returned {(int)response.StatusCode}: {body}");
    }
}
