namespace PrototypeManager.Infrastructure;

internal static class AzureDefaultPageDetector
{
    private static readonly string[] DefaultPageMarkers =
    [
        "Your web app is running and waiting for your content",
        "we don't have your content yet",
        "we don\u2019t have your content yet",
        "Built with NodeJS",
        "Haven't deployed yet?",
        "Haven\u2019t deployed yet?",
        "Use the deployment center to publish code",
        "Starting a new web site?",
        "Follow our Quickstart guide"
    ];

    public static bool IsDefaultPage(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return true;
        }

        return DefaultPageMarkers.Any(marker =>
            html.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
