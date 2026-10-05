using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PrototypePasswordPageDetectorTests
{
    [Fact]
    public void IsPasswordProtectionPage_DetectsGovUkPrototypePasswordPage()
    {
        const string html = """
            <html>
            <body>
              <h1>This prototype is password protected</h1>
              <label for="password">Enter the password you've been given</label>
              <input id="password" name="password" type="password" />
            </body>
            </html>
            """;

        Assert.True(PrototypePasswordPageDetector.IsPasswordProtectionPage(html));
    }

    [Fact]
    public void PrototypeSiteChecker_ReturnsLiveWhenPasswordPagePresent()
    {
        const string html = """
            <html><body>
              <h1>This prototype is password protected</h1>
              <input name="password" type="password" />
            </body></html>
            """;

        Assert.Equal(PrototypeSiteCheckResult.Live, PrototypeSiteChecker.Check(html, responseAvailable: true));
    }

    [Fact]
    public void PrototypeSiteChecker_ReturnsMissingPasswordPageForDeployedHomepageWithoutGate()
    {
        const string html = "<html><body><h1>Teacher contacts</h1></body></html>";

        Assert.Equal(
            PrototypeSiteCheckResult.MissingPasswordPage,
            PrototypeSiteChecker.Check(html, responseAvailable: true));
    }
}
