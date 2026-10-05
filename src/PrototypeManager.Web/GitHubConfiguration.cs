using Microsoft.Extensions.Configuration;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Web;

internal static class GitHubConfiguration
{
    public static void ApplyAppsettingsToken(WebApplicationBuilder builder)
    {
        var appsettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");

        if (!File.Exists(appsettingsPath))
        {
            return;
        }

        var token = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .Build()["GitHub:PersonalAccessToken"];

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitHub:PersonalAccessToken"] = GitHubTokenNormalizer.Normalize(token)
        });
    }
}
