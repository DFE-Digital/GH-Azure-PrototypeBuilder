using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PrototypeSiteVerifierTests
{
    [Fact]
    public async Task HasDeployedContentAsync_ReturnsFalseForAzureDefaultPage()
    {
        var verifier = CreateVerifier(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><body>Your web app is running and waiting for your content</body></html>")
            });

        var hasContent = await verifier.HasDeployedContentAsync(
            "https://example.azurewebsites.net",
            CancellationToken.None);

        Assert.False(hasContent);
    }

    [Fact]
    public async Task HasDeployedContentAsync_ReturnsFalseWhenPasswordPageMissing()
    {
        var verifier = CreateVerifier(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><h1>Teacher contacts</h1></body></html>")
            });

        var hasContent = await verifier.HasDeployedContentAsync(
            "https://example.azurewebsites.net",
            CancellationToken.None);

        Assert.False(hasContent);
    }

    [Fact]
    public async Task HasDeployedContentAsync_ReturnsTrueForPasswordProtectionPage()
    {
        var verifier = CreateVerifier(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <html><body>
                      <h1>This prototype is password protected</h1>
                      <input name="password" type="password" />
                    </body></html>
                    """)
            });

        var hasContent = await verifier.HasDeployedContentAsync(
            "https://example.azurewebsites.net",
            CancellationToken.None);

        Assert.True(hasContent);
    }

    [Fact]
    public async Task WaitForDeployedContentAsync_SkipsWhenVerificationDisabled()
    {
        var verifier = CreateVerifier(
            _ => throw new InvalidOperationException("HTTP should not be called when verification is disabled."),
            verifyDeployedContent: false,
            mode: "Azure");

        await verifier.WaitForDeployedContentAsync(
            Guid.NewGuid(),
            "example",
            "https://example.azurewebsites.net",
            new NoOpAuditService(),
            CancellationToken.None);
    }

    private static PrototypeSiteVerifier CreateVerifier(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        bool verifyDeployedContent = true,
        string mode = "Azure")
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handler));
        var httpClientFactory = new StubHttpClientFactory(httpClient);

        return new PrototypeSiteVerifier(
            httpClientFactory,
            Options.Create(new GitHubOptions
            {
                VerifyDeployedContent = verifyDeployedContent,
                DeploymentVerificationPollIntervalSeconds = 1,
                DeploymentVerificationTimeoutMinutes = 1
            }),
            Options.Create(new ProvisioningOptions { Mode = mode }),
            NullLogger<PrototypeSiteVerifier>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task RecordAsync(Guid? prototypeId, string actorEmail, string action, string details) =>
            Task.CompletedTask;
    }
}
