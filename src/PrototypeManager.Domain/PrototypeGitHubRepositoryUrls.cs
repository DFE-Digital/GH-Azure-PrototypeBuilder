namespace PrototypeManager.Domain;

public static class PrototypeGitHubRepositoryUrls
{
    public const string GitHubBaseUrl = "https://github.com/";

    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.TrimEnd('/');
    }

    public static string? TryGetOwner(string url)
    {
        var normalized = Normalize(url);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 1 ? segments[0] : null;
    }

    public static string? TryGetRepositoryName(string url)
    {
        var normalized = Normalize(url);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length >= 2 ? segments[1] : null;
    }

    public static bool HasAllowedOwner(string url, IEnumerable<string> allowedOwners)
    {
        var owner = TryGetOwner(url);

        return owner is not null &&
               allowedOwners.Any(allowedOwner =>
                   string.Equals(allowedOwner, owner, StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatRepositoryExample(string organisationName, string repositoryName = "your-repo") =>
        $"{GitHubBaseUrl}{organisationName}/{repositoryName}";

    public static string BuildRepositoryUrl(string owner, string repositoryName) =>
        $"{GitHubBaseUrl}{owner.Trim().Trim('/')}/{repositoryName.Trim().Trim('/')}";
}
