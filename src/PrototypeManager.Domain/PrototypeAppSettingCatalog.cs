using System.ComponentModel.DataAnnotations;
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
        !string.IsNullOrWhiteSpace(key) && KeyFormat().IsMatch(key.Trim());

    public static string NormaliseKey(string key) => key.Trim();

    public static void ValidateUserKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ValidationException("Enter a key for this config var.");
        }

        var trimmed = key.Trim();

        if (trimmed.Length > MaxKeyLength)
        {
            throw new ValidationException($"Keys must be {MaxKeyLength} characters or fewer.");
        }

        if (!IsValidKeyFormat(trimmed))
        {
            throw new ValidationException("Keys must start with a letter and use only letters, numbers and underscores.");
        }

        if (IsSystemManaged(trimmed))
        {
            throw new ValidationException("That key is managed by Prototype Manager and cannot be changed.");
        }

        if (IsPasswordKey(trimmed))
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

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex KeyFormat();
}
