using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal static partial class GitHubRepositoryUrlParser
{
    public static (string Owner, string Repository) Parse(string repositoryUrl)
    {
        var normalized = PrototypeGitHubRepositoryUrls.Normalize(repositoryUrl);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Enter a valid GitHub repository URL.");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new ValidationException("Enter a valid GitHub repository URL.");
        }

        var owner = segments[0];
        var repository = RepositoryNameRegex().Replace(segments[1], string.Empty);

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            throw new ValidationException("Enter a valid GitHub repository URL.");
        }

        return (owner, repository);
    }

    [GeneratedRegex(@"\.git$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryNameRegex();
}
