using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace PrototypeManager.Domain;

public static partial class PrototypeAppSettingCatalog
{
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 1024;

    public const string PasswordKey = "PASSWORD";

    public static readonly IReadOnlySet<string> SystemManagedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NODE_ENV",
        "SCM_DO_BUILD_DURING_DEPLOYMENT",
        "WEBSITE_WEBDEPLOY_USE_SCM",
        "AZURE_WEBAPP_PUBLISH_PROFILE"
    };

    public static bool IsSystemManaged(string key) =>
        SystemManagedKeys.Contains(key);

    public static bool IsPasswordKey(string key) =>
        string.Equals(key, PasswordKey, StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedAzureKey(string key) =>
        IsSystemManaged(key) || IsPasswordKey(key);

    public static bool IsValidKeyFormat(string key) =>
        !string.IsNullOrWhiteSpace(key) && KeyFormat().IsMatch(NormaliseKey(key));

    public static string NormaliseKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(key.Length);
        var previousWasUnderscore = false;

        foreach (var character in key.Trim())
        {
            char next;
            if (char.IsWhiteSpace(character) || character is '-' or '.')
            {
                next = '_';
            }
            else if (char.IsAsciiLetterOrDigit(character) || character == '_')
            {
                next = char.ToLowerInvariant(character);
            }
            else
            {
                continue;
            }

            if (next == '_')
            {
                if (builder.Length == 0 || previousWasUnderscore)
                {
                    continue;
                }

                previousWasUnderscore = true;
                builder.Append('_');
                continue;
            }

            previousWasUnderscore = false;
            builder.Append(next);
        }

        if (builder.Length > 0 && builder[^1] == '_')
        {
            builder.Length--;
        }

        var normalised = builder.ToString();
        return normalised.Length > MaxKeyLength
            ? normalised[..MaxKeyLength]
            : normalised;
    }

    public static void ValidateUserKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ValidationException("Enter a key for this config var.");
        }

        var normalised = NormaliseKey(key);

        if (string.IsNullOrWhiteSpace(normalised))
        {
            throw new ValidationException("Enter a key for this config var.");
        }

        if (char.IsAsciiDigit(normalised[0]))
        {
            throw new ValidationException("Keys must start with a letter.");
        }

        if (!KeyFormat().IsMatch(normalised))
        {
            throw new ValidationException("Keys must start with a letter and use only lowercase letters, numbers and underscores.");
        }

        if (IsSystemManaged(normalised))
        {
            throw new ValidationException("That key is managed by Prototype Manager and cannot be changed.");
        }

        if (IsPasswordKey(normalised))
        {
            throw new ValidationException("Use Manage on the PASSWORD row to change the prototype password.");
        }
    }

    public static void ValidateUserValue(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ValidationException("Enter a value for this config var.");
        }

        if (trimmed.Length > MaxValueLength)
        {
            throw new ValidationException($"Values must be {MaxValueLength} characters or fewer.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex KeyFormat();
}
