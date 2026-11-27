using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notify.Client;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class GovukNotifyExceptionRequestNotifier(
    IOptions<NotifyOptions> notifyOptions,
    ILogger<GovukNotifyExceptionRequestNotifier> logger) : IExceptionRequestNotifier
{
    private readonly NotifyOptions _options = notifyOptions.Value;

    public async Task NotifyExceptionRequestedAsync(
        Prototype prototype,
        ExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogInformation(
                "Skipping exception-request email for {PrototypeId}; GOV.UK Notify is not configured.",
                prototype.Id);
            return;
        }

        var recipient = string.IsNullOrWhiteSpace(_options.HelpEmail)
            ? "design.ops@education.gov.uk"
            : _options.HelpEmail.Trim();

        var subject = $"Prototype resource exception requested: {prototype.Name}";
        var body = BuildBody(prototype, request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new NotificationClient(_options.ApiKey);
            var personalisation = new Dictionary<string, dynamic>
            {
                ["subject"] = subject,
                ["body"] = body
            };

            await Task.Run(
                () => client.SendEmail(
                    emailAddress: recipient,
                    templateId: _options.PrototypeReadyTemplateId,
                    personalisation: personalisation),
                cancellationToken);

            logger.LogInformation(
                "Sent exception-request email for {PrototypeSlug} to {Recipient}.",
                prototype.Slug,
                recipient);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to send exception-request email for {PrototypeId} to {Recipient}.",
                prototype.Id,
                recipient);
        }
    }

    private static string BuildBody(Prototype prototype, ExceptionRequest request)
    {
        var lines = new List<string>
        {
            "A prototype resource exception has been requested in Prototype Manager.",
            string.Empty,
            "Prototype:",
            prototype.Name,
            string.Empty,
            "Requested by:",
            request.RequestedBy,
            string.Empty,
            "Current plan:",
            string.IsNullOrWhiteSpace(prototype.Sku) ? "Unknown" : prototype.Sku,
            string.Empty,
            "Requested plan:",
            request.RequestedSku,
            string.Empty,
            "Reason:",
            request.Reason,
            string.Empty,
            "Owner:",
            string.IsNullOrWhiteSpace(prototype.OwnerEmail) ? "Not set" : prototype.OwnerEmail,
            string.Empty,
            "Prototype URL:",
            string.IsNullOrWhiteSpace(prototype.PrototypeUrl) ? "Not available yet" : prototype.PrototypeUrl
        };

        if (!string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl))
        {
            lines.Add(string.Empty);
            lines.Add("GitHub repository:");
            lines.Add(prototype.GitHubRepositoryUrl);
        }

        lines.Add(string.Empty);
        lines.Add("Review this request in Prototype Manager Admin → Exception requests, then escalate the plan from Admin → Prototypes if approved.");

        return string.Join('\n', lines);
    }
}

internal sealed class NullExceptionRequestNotifier : IExceptionRequestNotifier
{
    public Task NotifyExceptionRequestedAsync(
        Prototype prototype,
        ExceptionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
