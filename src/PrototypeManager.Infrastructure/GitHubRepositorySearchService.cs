using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubRepositorySearchService(
    GitHubApiClient gitHubApiClient,
    PrototypeManagerDbContext dbContext,
    IOptions<GitHubOptions> githubOptions,
    ILogger<GitHubRepositorySearchService> logger) : IGitHubRepositorySearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GitHubOptions _options = githubOptions.Value;

    public async Task<IReadOnlyList<GitHubRepositorySearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 2 || !_options.IsConfigured)
        {
            return [];
        }

        var organisations = await dbContext.GitHubOrganisations
            .AsNoTracking()
            .Select(organisation => organisation.Name)
            .ToListAsync(cancellationToken);

        if (organisations.Count == 0)
        {
            return [];
        }

        var orgFilters = string.Join(
            ' ',
            organisations
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => $"org:{name.Trim()}"));

        if (string.IsNullOrWhiteSpace(orgFilters))
        {
            return [];
        }

        // Prefer name matches within allowed organisations.
        var searchQuery = $"{trimmed} {orgFilters} in:name";
        var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
        var requestUri =
            $"{baseUrl}/search/repositories?q={Uri.EscapeDataString(searchQuery)}&per_page=10&sort=updated";

        try
        {
            using var response = await gitHubApiClient.SendAsync(
                HttpMethod.Get,
                requestUri,
                cancellationToken: cancellationToken);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "GitHub repository search was rejected ({StatusCode}): {Body}",
                    (int)response.StatusCode,
                    body);
                return [];
            }

            await gitHubApiClient.EnsureSuccessAsync(response, "Search GitHub repositories", cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<GitHubSearchResponse>(stream, JsonOptions, cancellationToken);
            if (payload?.Items is null || payload.Items.Count == 0)
            {
                return [];
            }

            var allowed = new HashSet<string>(organisations, StringComparer.OrdinalIgnoreCase);

            return payload.Items
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.HtmlUrl) &&
                    !string.IsNullOrWhiteSpace(item.Name) &&
                    !string.IsNullOrWhiteSpace(item.FullName) &&
                    allowed.Contains(item.FullName.Split('/')[0]))
                .Select(item => new GitHubRepositorySearchResult(
                    item.Name!.Trim(),
                    item.FullName!.Trim(),
                    PrototypeGitHubRepositoryUrls.Normalize(item.HtmlUrl!)))
                .DistinctBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "GitHub repository search failed for query {Query}.", trimmed);
            return [];
        }
    }

    private sealed class GitHubSearchResponse
    {
        [JsonPropertyName("items")]
        public List<GitHubSearchItem>? Items { get; set; }
    }

    private sealed class GitHubSearchItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}

internal sealed class NullGitHubRepositorySearchService : IGitHubRepositorySearchService
{
    public Task<IReadOnlyList<GitHubRepositorySearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitHubRepositorySearchResult>>([]);
}
