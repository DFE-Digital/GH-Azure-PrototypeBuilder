using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubInstallationTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GitHubOptions> githubOptions,
    ILogger<GitHubInstallationTokenProvider> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        GetTokenAsync(forceRefresh: false, cancellationToken);

    public async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (forceRefresh)
            {
                Invalidate();
            }
            else if (TryGetCachedToken(out cached))
            {
                return cached;
            }

            var options = githubOptions.CurrentValue;
            if (!options.HasGitHubAppCredentials)
            {
                throw new InvalidOperationException(
                    "GitHub App credentials are not configured. Set GitHub:AppId, GitHub:InstallationId, and GitHub:PrivateKey (or PrivateKeyPath).");
            }

            var privateKeyPem = GitHubAppCredentials.ResolvePrivateKeyPem(options);
            var jwt = GitHubAppCredentials.CreateAppJwt(options.AppId, privateKeyPem);
            var baseUrl = options.ApiBaseUrl.TrimEnd('/');
            var client = httpClientFactory.CreateClient(GitHubApiClient.HttpClientName);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{baseUrl}/app/installations/{options.InstallationId}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to create GitHub App installation token. Status {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body);
                throw new InvalidOperationException(
                    $"Failed to create GitHub App installation token. GitHub API returned {(int)response.StatusCode}: {body}");
            }

            using var document = JsonDocument.Parse(body);
            var token = document.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("GitHub App installation token response did not include a token.");

            var expiresAt = document.RootElement.TryGetProperty("expires_at", out var expiresElement) &&
                            expiresElement.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(expiresElement.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.AddHours(1);

            _cachedToken = token;
            _expiresAt = expiresAt;

            if (document.RootElement.TryGetProperty("permissions", out var permissions) &&
                permissions.ValueKind == JsonValueKind.Object)
            {
                logger.LogInformation(
                    "Created GitHub App installation token expiring at {ExpiresAt:u} with permissions: {Permissions}",
                    expiresAt,
                    permissions.ToString());
            }
            else
            {
                logger.LogInformation("Created GitHub App installation token expiring at {ExpiresAt:u}.", expiresAt);
            }

            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetCachedToken(out string token)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken) &&
            DateTimeOffset.UtcNow < _expiresAt - TimeSpan.FromMinutes(2))
        {
            token = _cachedToken;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
