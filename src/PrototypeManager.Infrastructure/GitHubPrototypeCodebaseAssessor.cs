using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GitHubPrototypeCodebaseAssessor(
    GitHubApiClient gitHubApiClient,
    IOptions<GitHubOptions> githubOptions,
    IMemoryCache memoryCache,
    ILogger<GitHubPrototypeCodebaseAssessor> logger) : IGitHubPrototypeCodebaseAssessor
{
    private const string LatestFrontendCacheKey = "govuk-frontend:latest-release";
    private static readonly TimeSpan LatestFrontendCacheDuration = TimeSpan.FromHours(6);
    private static readonly Regex VersionPrefixRegex = new(@"^[~^>=<\s]*", RegexOptions.Compiled);

    private readonly GitHubOptions _options = githubOptions.Value;

    public async Task<PrototypeCodebaseAssessment> AssessAsync(
        string repositoryUrl,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return new PrototypeCodebaseAssessment(
                PrototypeCodebaseSupport.Unknown,
                "We could not check this repository because GitHub access is not configured.");
        }

        string owner;
        string repository;
        try
        {
            (owner, repository) = GitHubRepositoryUrlParser.Parse(repositoryUrl);
        }
        catch
        {
            return new PrototypeCodebaseAssessment(
                PrototypeCodebaseSupport.Unknown,
                "Enter a valid GitHub repository URL before we can check the codebase.");
        }

        try
        {
            var packageJson = await TryReadPackageJsonAsync(owner, repository, cancellationToken);
            if (packageJson is null)
            {
                return new PrototypeCodebaseAssessment(
                    PrototypeCodebaseSupport.Unsupported,
                    "No package.json was found in this repository. GOV.UK Prototype Kit apps need a Node package.json with govuk-prototype-kit.");
            }

            using var document = JsonDocument.Parse(packageJson);
            var root = document.RootElement;

            var kitVersion = FindDependencyVersion(root, "govuk-prototype-kit");
            var frontendVersionRaw = FindDependencyVersion(root, "govuk-frontend");
            var frontendVersion = NormaliseVersion(frontendVersionRaw);
            var hasKitScripts = HasPrototypeKitScripts(root);
            var latestFrontend = await GetLatestGovUkFrontendVersionAsync(cancellationToken);
            var isBehind = IsBehind(frontendVersion, latestFrontend);

            if (!string.IsNullOrWhiteSpace(kitVersion) || hasKitScripts)
            {
                var summary = BuildSupportedSummary(kitVersion, frontendVersion, latestFrontend, isBehind);
                return new PrototypeCodebaseAssessment(
                    PrototypeCodebaseSupport.Supported,
                    summary,
                    kitVersion,
                    frontendVersion,
                    latestFrontend,
                    isBehind);
            }

            var unsupportedSummary =
                "This repository’s package.json does not include govuk-prototype-kit. Prototype Manager hosts GOV.UK Prototype Kit Node apps, so hosting may fail.";
            if (!string.IsNullOrWhiteSpace(frontendVersion))
            {
                unsupportedSummary += $" It does reference govuk-frontend {frontendVersion}.";
            }

            return new PrototypeCodebaseAssessment(
                PrototypeCodebaseSupport.Unsupported,
                unsupportedSummary,
                null,
                frontendVersion,
                latestFrontend,
                isBehind);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to assess prototype codebase for {Owner}/{Repository}.",
                owner,
                repository);

            return new PrototypeCodebaseAssessment(
                PrototypeCodebaseSupport.Unknown,
                "We could not check whether this repository is a GOV.UK Prototype Kit app. You can continue, but hosting may fail if it is not.");
        }
    }

    private static string BuildSupportedSummary(
        string? kitVersion,
        string? frontendVersion,
        string? latestFrontend,
        bool? isBehind)
    {
        var parts = new List<string>();

        parts.Add(string.IsNullOrWhiteSpace(kitVersion)
            ? "This looks like a supported GOV.UK Prototype Kit codebase."
            : $"This looks like a supported GOV.UK Prototype Kit codebase (govuk-prototype-kit {kitVersion}).");

        if (!string.IsNullOrWhiteSpace(frontendVersion))
        {
            if (!string.IsNullOrWhiteSpace(latestFrontend) && isBehind == true)
            {
                parts.Add($"GOV.UK Frontend {frontendVersion} is behind the latest release {latestFrontend}.");
            }
            else if (!string.IsNullOrWhiteSpace(latestFrontend) && isBehind == false)
            {
                parts.Add($"GOV.UK Frontend {frontendVersion} matches the latest release.");
            }
            else
            {
                parts.Add($"GOV.UK Frontend {frontendVersion}.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(latestFrontend))
        {
            parts.Add($"Latest GOV.UK Frontend release is {latestFrontend}.");
        }

        return string.Join(' ', parts);
    }

    private async Task<string?> GetLatestGovUkFrontendVersionAsync(CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(LatestFrontendCacheKey, out string? cached) &&
            !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
            using var response = await gitHubApiClient.SendAsync(
                HttpMethod.Get,
                $"{baseUrl}/repos/alphagov/govuk-frontend/releases/latest",
                cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Could not fetch latest govuk-frontend release. Status {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;

            var version = NormaliseVersion(tag);
            if (!string.IsNullOrWhiteSpace(version))
            {
                memoryCache.Set(LatestFrontendCacheKey, version, LatestFrontendCacheDuration);
            }

            return version;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to resolve latest govuk-frontend version.");
            return null;
        }
    }

    private async Task<string?> TryReadPackageJsonAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
        using var response = await gitHubApiClient.SendAsync(
            HttpMethod.Get,
            $"{baseUrl}/repos/{owner}/{repository}/contents/package.json",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await gitHubApiClient.EnsureSuccessAsync(
            response,
            $"read package.json for {owner}/{repository}",
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("encoding", out var encodingElement) &&
            string.Equals(encodingElement.GetString(), "base64", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("content", out var contentElement))
        {
            var encoded = contentElement.GetString();
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(encoded.Replace("\n", string.Empty, StringComparison.Ordinal));
            return Encoding.UTF8.GetString(bytes);
        }

        if (root.TryGetProperty("download_url", out var downloadUrlElement))
        {
            var downloadUrl = downloadUrlElement.GetString();
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                using var downloadResponse = await gitHubApiClient.SendAsync(
                    HttpMethod.Get,
                    downloadUrl,
                    cancellationToken: cancellationToken);
                await gitHubApiClient.EnsureSuccessAsync(
                    downloadResponse,
                    $"download package.json for {owner}/{repository}",
                    cancellationToken);
                return await downloadResponse.Content.ReadAsStringAsync(cancellationToken);
            }
        }

        return null;
    }

    internal static string? FindDependencyVersion(JsonElement root, string packageName)
    {
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!root.TryGetProperty(section, out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (dependencies.TryGetProperty(packageName, out var versionElement))
            {
                var version = versionElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version.Trim();
                }
            }
        }

        return null;
    }

    internal static bool HasPrototypeKitScripts(JsonElement root)
    {
        if (!root.TryGetProperty("scripts", out var scripts) ||
            scripts.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var script in scripts.EnumerateObject())
        {
            var value = script.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value) &&
                value.Contains("govuk-prototype-kit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? NormaliseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        trimmed = VersionPrefixRegex.Replace(trimmed, string.Empty);
        var match = Regex.Match(trimmed, @"^\d+(?:\.\d+){0,3}");
        return match.Success ? match.Value : null;
    }

    internal static bool? IsBehind(string? current, string? latest)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest))
        {
            return null;
        }

        if (!Version.TryParse(PadVersion(current), out var currentVersion) ||
            !Version.TryParse(PadVersion(latest), out var latestVersion))
        {
            return null;
        }

        return currentVersion < latestVersion;
    }

    private static string PadVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        while (parts.Length < 3)
        {
            version += ".0";
            parts = version.Split('.');
        }

        return string.Join('.', parts.Take(4));
    }
}

internal sealed class NullGitHubPrototypeCodebaseAssessor : IGitHubPrototypeCodebaseAssessor
{
    public Task<PrototypeCodebaseAssessment> AssessAsync(
        string repositoryUrl,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new PrototypeCodebaseAssessment(
            PrototypeCodebaseSupport.Unknown,
            "We could not check this repository because GitHub access is not configured."));
}
