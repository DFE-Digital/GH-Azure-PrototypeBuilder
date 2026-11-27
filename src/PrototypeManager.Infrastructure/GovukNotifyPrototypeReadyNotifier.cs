using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notify.Client;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class GovukNotifyPrototypeReadyNotifier(
    IOptions<NotifyOptions> notifyOptions,
    ILogger<GovukNotifyPrototypeReadyNotifier> logger) : IPrototypeReadyNotifier
{
    private readonly NotifyOptions _options = notifyOptions.Value;

    public async Task NotifyPrototypeReadyAsync(
        Prototype prototype,
        string? prototypePassword,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogInformation(
                "Skipping prototype-ready email for {PrototypeId}; GOV.UK Notify is not configured.",
                prototype.Id);
            return;
        }

        var recipient = FirstNonEmpty(prototype.OwnerEmail, prototype.CreatedBy);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning(
                "Skipping prototype-ready email for {PrototypeId}; no owner or creator email was available.",
                prototype.Id);
            return;
        }

        var subject = $"{prototype.Name} is ready";
        var body = BuildBody(prototype, prototypePassword, _options.HelpEmail);

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
                    emailAddress: recipient.Trim(),
                    templateId: _options.PrototypeReadyTemplateId,
                    personalisation: personalisation),
                cancellationToken);

            logger.LogInformation(
                "Sent prototype-ready email for {PrototypeSlug} to {Recipient}.",
                prototype.Slug,
                recipient);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Provisioning already succeeded; do not fail the job because email failed.
            logger.LogError(
                exception,
                "Failed to send prototype-ready email for {PrototypeId} to {Recipient}.",
                prototype.Id,
                recipient);
        }
    }

    private static string BuildBody(Prototype prototype, string? prototypePassword, string helpEmail)
    {
        var lines = new List<string>
        {
            $"Your prototype '{prototype.Name}' is ready to use.",
            string.Empty,
            "Prototype URL:",
            string.IsNullOrWhiteSpace(prototype.PrototypeUrl) ? "Not available yet" : prototype.PrototypeUrl.Trim()
        };

        if (!string.IsNullOrWhiteSpace(prototypePassword))
        {
            lines.Add(string.Empty);
            lines.Add("Password:");
            lines.Add(prototypePassword.Trim());
        }

        if (!string.IsNullOrWhiteSpace(prototype.GitHubRepositoryUrl))
        {
            lines.Add(string.Empty);
            lines.Add("GitHub repository:");
            lines.Add(prototype.GitHubRepositoryUrl.Trim());
            lines.Add("Clone this repository if you need to change the prototype kit code locally.");
        }

        lines.Add(string.Empty);
        lines.Add("Managing access");
        lines.Add(
            "You can add other people from your team as contacts on the prototype details page. Members can manage settings and view the password.");

        lines.Add(string.Empty);
        lines.Add("Need help?");
        lines.Add(
            $"Contact DesignOps at {helpEmail.Trim()} if you need support with this prototype or Prototype Manager.");

        return string.Join('\n', lines);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

internal sealed class NullPrototypeReadyNotifier : IPrototypeReadyNotifier
{
    public Task NotifyPrototypeReadyAsync(
        Prototype prototype,
        string? prototypePassword,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
