using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubHealthService(
    PrototypeManagerDbContext dbContext,
    IOptionsMonitor<GitHubOptions> githubOptions,
    IHttpClientFactory httpClientFactory,
    GitHubApiClient gitHubApiClient,
    ILogger<GitHubHealthService> logger) : IGitHubHealthService
{
    public async Task<GitHubHealthReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var organisations = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var options = githubOptions.CurrentValue;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        GitHubHealthSnapshot snapshot;
        try
        {
            snapshot = await ProbeAsync(options, organisations, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("GitHub health check timed out.");
            snapshot = BaseSnapshot(options, organisations) with
            {
                AuthenticationError = "GitHub did not respond in time."
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "GitHub health check failed.");
            snapshot = BaseSnapshot(options, organisations) with
            {
                AuthenticationError = SafeMessage(exception)
            };
        }

        var report = GitHubHealthReportBuilder.Build(snapshot);
        logger.LogInformation(
            "GitHub health check completed with status {Status}. Issues {IssueCount}.",
            report.Status,
            report.Issues.Count);
        return report;
    }

    private async Task<GitHubHealthSnapshot> ProbeAsync(
        GitHubOptions options,
        IReadOnlyList<GitHubOrganisation> organisations,
        CancellationToken cancellationToken)
    {
        var snapshot = BaseSnapshot(options, organisations);
        if (!options.IsConfigured)
        {
            return snapshot;
        }

        var baseUrl = options.ApiBaseUrl.TrimEnd('/');
        if (options.HasGitHubAppCredentials)
        {
            var (appJson, _) = await SendAppAsync(options, HttpMethod.Get, $"{baseUrl}/app", cancellationToken);
            var (installationJson, installationError) = await SendAppAsync(
                options,
                HttpMethod.Get,
                $"{baseUrl}/app/installations/{options.InstallationId}",
                cancellationToken);

            string? authenticationError = null;
            try
            {
                await gitHubApiClient.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            }
            catch (Exception exception)
            {
                authenticationError = SafeMessage(exception);
            }

            var access = authenticationError is null
                ? await ReadInstallationAccessAsync(baseUrl, organisations, includeInstallationRepositories: true, cancellationToken)
                : EmptyAccess();

            return snapshot with
            {
                AppJson = appJson,
                InstallationJson = installationJson,
                InstallationReadError = installationError,
                AuthenticationError = authenticationError,
                RateLimitJson = access.RateLimitJson,
                AccessibleRepositoryCount = access.RepositoryCount,
                RepositoryAccessError = access.RepositoryError,
                OrganisationAccessChecked = authenticationError is null,
                OrganisationAccess = access.Organisations
            };
        }

        using var userResponse = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/user",
            cancellationToken: cancellationToken);
        var userBody = await userResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!userResponse.IsSuccessStatusCode)
        {
            return snapshot with
            {
                AuthenticationError = DescribeFailure("authenticate to GitHub", userResponse.StatusCode, userBody)
            };
        }

        var scopes = userResponse.Headers.TryGetValues("X-OAuth-Scopes", out var scopeValues)
            ? string.Join(", ", scopeValues)
            : null;
        var accessForToken = await ReadInstallationAccessAsync(
            baseUrl,
            organisations,
            includeInstallationRepositories: false,
            cancellationToken);

        return snapshot with
        {
            AuthenticatedLogin = ReadLogin(userBody),
            TokenScopes = scopes,
            RateLimitJson = accessForToken.RateLimitJson,
            AccessibleRepositoryCount = accessForToken.RepositoryCount,
            RepositoryAccessError = accessForToken.RepositoryError,
            OrganisationAccessChecked = true,
            OrganisationAccess = accessForToken.Organisations
        };
    }

    private async Task<AccessProbe> ReadInstallationAccessAsync(
        string baseUrl,
        IReadOnlyList<GitHubOrganisation> organisations,
        bool includeInstallationRepositories,
        CancellationToken cancellationToken)
    {
        string? rateLimitJson = null;
        int? repositoryCount = null;
        string? repositoryError = null;
        var organisationAccess = new Dictionary<string, GitHubOrganisationAccessResult>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var rateResponse = await gitHubApiClient.SendAsync(
                HttpMethod.Get,
                $"{baseUrl}/rate_limit",
                cancellationToken: cancellationToken);
            rateLimitJson = await rateResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!rateResponse.IsSuccessStatusCode)
            {
                rateLimitJson = null;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "GitHub rate limit check failed.");
        }

        if (includeInstallationRepositories)
        {
            try
            {
                using var repositoryResponse = await gitHubApiClient.SendAsync(
                    HttpMethod.Get,
                    $"{baseUrl}/installation/repositories?per_page=1",
                    cancellationToken: cancellationToken);
                var body = await repositoryResponse.Content.ReadAsStringAsync(cancellationToken);
                if (repositoryResponse.IsSuccessStatusCode)
                {
                    repositoryCount = ReadTotalCount(body);
                }
                else
                {
                    repositoryError = DescribeFailure("list accessible repositories", repositoryResponse.StatusCode, body);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "GitHub repository access check failed.");
                repositoryError = "Could not list repositories the installation can access.";
            }
        }

        foreach (var organisation in organisations)
        {
            organisationAccess[organisation.Name] = await ReadOrganisationAsync(baseUrl, organisation.Name, cancellationToken);
        }

        return new AccessProbe(rateLimitJson, repositoryCount, repositoryError, organisationAccess);
    }

    private async Task<GitHubOrganisationAccessResult> ReadOrganisationAsync(
        string baseUrl,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await gitHubApiClient.SendAsync(
                HttpMethod.Get,
                $"{baseUrl}/orgs/{Uri.EscapeDataString(name)}",
                cancellationToken: cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new GitHubOrganisationAccessResult
                {
                    CanAccess = false,
                    Error = DescribeFailure("read the organisation", response.StatusCode, body)
                };
            }

            if (!TryParse(body, out var root))
            {
                return new GitHubOrganisationAccessResult
                {
                    CanAccess = true
                };
            }

            var login = ReadString(root, "login");
            var url = ReadString(root, "html_url");
            return new GitHubOrganisationAccessResult
            {
                CanAccess = true,
                CanonicalLogin = string.IsNullOrWhiteSpace(login) ? null : login,
                HtmlUrl = string.IsNullOrWhiteSpace(url) ? null : url
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "GitHub organisation check failed for {Organisation}.", name);
            return new GitHubOrganisationAccessResult
            {
                CanAccess = false,
                Error = "Could not reach GitHub to check this organisation."
            };
        }
    }

    private async Task<(string? Body, string? Error)> SendAppAsync(
        GitHubOptions options,
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            var privateKeyPem = GitHubAppCredentials.ResolvePrivateKeyPem(options);
            var jwt = GitHubAppCredentials.CreateAppJwt(options.AppId, privateKeyPem);
            var client = httpClientFactory.CreateClient(GitHubApiClient.HttpClientName);
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return (body, null);
            }

            return (null, DescribeFailure("read the GitHub App installation", response.StatusCode, body));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "GitHub App request failed for {RequestUri}.", requestUri);
            return (null, SafeMessage(exception));
        }
    }

    private static GitHubHealthSnapshot BaseSnapshot(
        GitHubOptions options,
        IReadOnlyList<GitHubOrganisation> organisations) => new()
    {
        IsConfigured = options.IsConfigured,
        UsesGitHubApp = options.HasGitHubAppCredentials,
        ValidateWithApi = options.ValidateWithApi,
        AppId = options.AppId,
        InstallationId = options.InstallationId,
        PrivateKeyConfigured = !string.IsNullOrWhiteSpace(options.PrivateKey) ||
                               !string.IsNullOrWhiteSpace(options.PrivateKeyPath),
        PersonalAccessTokenConfigured = !string.IsNullOrWhiteSpace(options.PersonalAccessToken),
        Organisations = organisations,
        DefaultCollaborators = options.DefaultCollaborators
            .Where(collaborator => !string.IsNullOrWhiteSpace(collaborator))
            .Select(collaborator => collaborator.Trim())
            .ToList(),
        DefaultCollaboratorPermission = string.IsNullOrWhiteSpace(options.DefaultCollaboratorPermission)
            ? "admin"
            : options.DefaultCollaboratorPermission.Trim(),
        WorkflowBranch = string.IsNullOrWhiteSpace(options.WorkflowBranch) ? "main" : options.WorkflowBranch.Trim(),
        AutoConfigureDeployment = options.AutoConfigureDeployment,
        ApiBaseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "https://api.github.com" : options.ApiBaseUrl.Trim()
    };

    private static AccessProbe EmptyAccess() => new(null, null, null, new Dictionary<string, GitHubOrganisationAccessResult>(StringComparer.OrdinalIgnoreCase));

    private static string DescribeFailure(string operation, HttpStatusCode statusCode, string body)
    {
        var message = GitHubApiErrors.ReadMessage(body);
        if (message.Length > 240)
        {
            message = message[..240];
        }

        if (message.Contains("BEGIN", StringComparison.Ordinal) ||
            message.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            message = string.Empty;
        }

        var detail = string.IsNullOrWhiteSpace(message) ? string.Empty : $" GitHub said: {message}";
        return $"Could not {operation} ({(int)statusCode}).{detail}";
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        if (message.Contains("BEGIN", StringComparison.Ordinal) ||
            message.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub authentication failed. Check the GitHub App credentials.";
        }

        return message.Length > 400 ? message[..400] : message;
    }

    private static string ReadLogin(string json)
    {
        if (!TryParse(json, out var root))
        {
            return string.Empty;
        }

        return ReadString(root, "login");
    }

    private static int? ReadTotalCount(string json)
    {
        if (!TryParse(json, out var root) ||
            !root.TryGetProperty("total_count", out var total) ||
            !total.TryGetInt32(out var count))
        {
            return null;
        }

        return count;
    }

    private static bool TryParse(string json, out JsonElement root)
    {
        root = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private sealed record AccessProbe(
        string? RateLimitJson,
        int? RepositoryCount,
        string? RepositoryError,
        IReadOnlyDictionary<string, GitHubOrganisationAccessResult> Organisations);
}
