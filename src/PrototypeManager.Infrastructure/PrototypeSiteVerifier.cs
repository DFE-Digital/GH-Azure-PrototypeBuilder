using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class PrototypeSiteVerifier(
    IHttpClientFactory httpClientFactory,
    IOptions<GitHubOptions> githubOptions,
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<PrototypeSiteVerifier> logger) : IPrototypeSiteVerifier
{
    public const string HttpClientName = "PrototypeSiteVerifier";

    private readonly GitHubOptions _githubOptions = githubOptions.Value;
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    public async Task WaitForDeployedContentAsync(
        Guid prototypeId,
        string appName,
        string prototypeUrl,
        IAuditService auditService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prototypeUrl))
        {
            throw new InvalidOperationException("Prototype URL is not available for deployment verification.");
        }

        if (!_provisioningOptions.IsAzureMode || !_githubOptions.VerifyDeployedContent)
        {
            await ProvisioningTaskRecorder.RecordCompletedAsync(
                auditService,
                logger,
                prototypeId,
                appName,
                ProvisioningTasks.SiteVerify);

            return;
        }

        var timeout = TimeSpan.FromMinutes(_githubOptions.DeploymentVerificationTimeoutMinutes);
        var pollInterval = TimeSpan.FromSeconds(_githubOptions.DeploymentVerificationPollIntervalSeconds);
        var deadline = DateTimeOffset.UtcNow + timeout;

        await ProvisioningActivityRecorder.RecordStepAsync(
            auditService,
            logger,
            prototypeId,
            appName,
            $"Testing live URL {prototypeUrl} for password protection.");

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var checkResult = await CheckSiteAsync(prototypeUrl, cancellationToken);

            if (checkResult == PrototypeSiteCheckResult.Live)
            {
                await ProvisioningActivityRecorder.RecordStepAsync(
                    auditService,
                    logger,
                    prototypeId,
                    appName,
                    $"Live URL verified at {prototypeUrl} (password protection page detected).");

                await ProvisioningTaskRecorder.RecordCompletedAsync(
                    auditService,
                    logger,
                    prototypeId,
                    appName,
                    ProvisioningTasks.SiteVerify);

                return;
            }

            logger.LogInformation(
                "Prototype URL {PrototypeUrl} is not ready yet ({CheckResult}). Retrying in {PollIntervalSeconds} seconds.",
                prototypeUrl,
                checkResult,
                pollInterval.TotalSeconds);

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Prototype URL did not show the password protection page within {timeout.TotalMinutes:0} minutes: {prototypeUrl}");
    }

    internal async Task<PrototypeSiteCheckResult> CheckSiteAsync(string prototypeUrl, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, prototypeUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Unauthorized)
        {
            return PrototypeSiteCheckResult.Unavailable;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        return PrototypeSiteChecker.Check(html, responseAvailable: true);
    }

    internal async Task<bool> HasDeployedContentAsync(string prototypeUrl, CancellationToken cancellationToken) =>
        await CheckSiteAsync(prototypeUrl, cancellationToken) == PrototypeSiteCheckResult.Live;
}
