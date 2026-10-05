using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace PrototypeManager.Domain;

public static partial class GitHubOrganisationNames
{
    public const int MaxLength = 39;

    public static string ParseInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ValidationException("Enter a GitHub organisation or account URL.");
        }

        var trimmed = input.Trim().TrimEnd('/');

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            string.Equals(absoluteUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFromPath(absoluteUri.AbsolutePath);
        }

        if (trimmed.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFromPath(trimmed["github.com".Length..]);
        }

        ValidateName(trimmed);
        return trimmed;
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Enter a GitHub organisation or account name.");
        }

        if (name.Length > MaxLength)
        {
            throw new ValidationException($"GitHub organisation or account names must be {MaxLength} characters or fewer.");
        }

        if (!NameFormat().IsMatch(name))
        {
            throw new ValidationException("Enter a valid GitHub organisation or account name.");
        }
    }

    private static string ParseFromPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new ValidationException("Enter a GitHub organisation or account URL.");
        }

        ValidateName(segments[0]);
        return segments[0];
    }

    [GeneratedRegex("^[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?$")]
    private static partial Regex NameFormat();
}
