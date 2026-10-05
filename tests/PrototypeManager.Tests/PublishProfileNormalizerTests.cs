using System.Xml.Linq;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PublishProfileNormalizerTests
{
    private const string SampleProfile = """
        <?xml version="1.0" encoding="utf-8"?>
        <publishData>
          <publishProfile profileName="app - Web Deploy" publishMethod="MSDeploy" publishUrl="waws-prod-am2-001.publish.azurewebsites.windows.net:443" msdeploySite="my-prototype" userName="$my-prototype" userPWD="secret" destinationAppUrl="https://my-prototype.azurewebsites.net" />
          <publishProfile profileName="app - FTP" publishMethod="FTP" publishUrl="ftps://waws-prod-am2-001.ftp.azurewebsites.windows.net/site/wwwroot" userName="my-prototype\$my-prototype" userPWD="secret" />
        </publishData>
        """;

    [Fact]
    public void NormalizeForGitHubActions_SetsScmPublishUrlOnMsDeployProfile()
    {
        var normalized = PublishProfileNormalizer.NormalizeForGitHubActions(SampleProfile, "my-prototype");

        var document = XDocument.Parse(normalized);
        var firstProfile = document.Descendants("publishProfile").First();

        Assert.Equal("MSDeploy", firstProfile.Attribute("publishMethod")?.Value);
        Assert.Equal("my-prototype.scm.azurewebsites.net:443", firstProfile.Attribute("publishUrl")?.Value);
    }

    [Fact]
    public void NormalizeForGitHubActions_LeavesFtpProfileUnchanged()
    {
        var normalized = PublishProfileNormalizer.NormalizeForGitHubActions(SampleProfile, "my-prototype");

        var document = XDocument.Parse(normalized);
        var ftpProfile = document.Descendants("publishProfile")
            .Single(profile => profile.Attribute("publishMethod")?.Value == "FTP");

        Assert.Equal(
            "ftps://waws-prod-am2-001.ftp.azurewebsites.windows.net/site/wwwroot",
            ftpProfile.Attribute("publishUrl")?.Value);
    }
}
