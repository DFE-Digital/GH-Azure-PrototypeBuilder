using System.Xml.Linq;

namespace PrototypeManager.Infrastructure;

internal static class PublishProfileNormalizer
{
    private static readonly HashSet<string> DeployPublishMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "MSDeploy",
            "MSDeploySite"
        };

    public static string NormalizeForGitHubActions(string publishProfileXml, string webAppName)
    {
        if (string.IsNullOrWhiteSpace(publishProfileXml))
        {
            return publishProfileXml;
        }

        var document = XDocument.Parse(publishProfileXml);
        var scmPublishUrl = $"{webAppName.Trim()}.scm.azurewebsites.net:443";
        XElement? deployProfile = null;

        foreach (var profile in document.Descendants("publishProfile"))
        {
            var publishMethod = profile.Attribute("publishMethod")?.Value;

            if (publishMethod is null || !DeployPublishMethods.Contains(publishMethod))
            {
                continue;
            }

            profile.SetAttributeValue("publishUrl", scmPublishUrl);
            deployProfile ??= profile;
        }

        if (deployProfile?.Parent is not { } parent)
        {
            return publishProfileXml;
        }

        deployProfile.Remove();
        parent.AddFirst(deployProfile);

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
