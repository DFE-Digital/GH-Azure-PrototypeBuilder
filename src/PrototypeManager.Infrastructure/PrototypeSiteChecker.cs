using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal static class PrototypePasswordPageDetector
{
    private static readonly string[] PasswordPageMarkers =
    [
        "Enter the password you've been given",
        "Enter the password you\u2019ve been given",
        "prototype is password protected",
        "This prototype is password protected",
        "manage-prototype/password",
        "name=\"password\"",
        "id=\"password\"",
        "prototype-password"
    ];

    public static bool IsPasswordProtectionPage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        return PasswordPageMarkers.Any(marker =>
            html.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

internal enum PrototypeSiteCheckResult
{
    Live,
    DefaultPage,
    MissingPasswordPage,
    Unavailable
}

internal static class PrototypeSiteChecker
{
    public static PrototypeSiteCheckResult Check(string? html, bool responseAvailable)
    {
        if (!responseAvailable || string.IsNullOrWhiteSpace(html))
        {
            return PrototypeSiteCheckResult.Unavailable;
        }

        if (AzureDefaultPageDetector.IsDefaultPage(html))
        {
            return PrototypeSiteCheckResult.DefaultPage;
        }

        if (!PrototypePasswordPageDetector.IsPasswordProtectionPage(html))
        {
            return PrototypeSiteCheckResult.MissingPasswordPage;
        }

        return PrototypeSiteCheckResult.Live;
    }
}
