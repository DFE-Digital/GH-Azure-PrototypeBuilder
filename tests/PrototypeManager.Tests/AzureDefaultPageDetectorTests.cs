using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class AzureDefaultPageDetectorTests
{
    private const string DefaultPageHtml = """
        <!DOCTYPE html>
        <html>
        <body>
          <h1>Your web app is running and waiting for your content</h1>
          <p>Your web app is live, but we don't have your content yet.</p>
          <p>Built with NodeJS</p>
        </body>
        </html>
        """;

    private const string DeployedPageHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head><title>Teacher contacts</title></head>
        <body><h1 class="govuk-heading-xl">Teacher contacts</h1></body>
        </html>
        """;

    [Fact]
    public void IsDefaultPage_ReturnsTrueForAzurePlaceholder()
    {
        Assert.True(AzureDefaultPageDetector.IsDefaultPage(DefaultPageHtml));
    }

    [Fact]
    public void IsDefaultPage_ReturnsFalseForDeployedPrototype()
    {
        Assert.False(AzureDefaultPageDetector.IsDefaultPage(DeployedPageHtml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDefaultPage_ReturnsTrueForMissingContent(string? html)
    {
        Assert.True(AzureDefaultPageDetector.IsDefaultPage(html));
    }
}
