using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notify.Client;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class GovukNotifyFeatureSuggestionNotifier(
    IOptions<NotifyOptions> notifyOptions,
    ILogger<GovukNotifyFeatureSuggestionNotifier> logger) : IFeatureSuggestionNotifier
{
    private readonly NotifyOptions _options = notifyOptions.Value;

    public async Task NotifyFeatureSuggestedAsync(
        FeatureSuggestion suggestion,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogInformation("Skipping feature-suggestion email; GOV.UK Notify is not configured.");
            return;
        }

        var recipient = string.IsNullOrWhiteSpace(_options.HelpEmail)
            ? "design.ops@education.gov.uk"
            : _options.HelpEmail.Trim();

        var subject = $"Prototype Manager feature suggestion: {suggestion.Feature}";
        var body = BuildBody(suggestion);

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
                "Sent feature-suggestion email from {SuggestedBy} to {Recipient}.",
                suggestion.SuggestedBy,
                recipient);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to send feature-suggestion email to {Recipient}.",
                recipient);
            throw;
        }
    }

    private static string BuildBody(FeatureSuggestion suggestion) =>
        string.Join(
            '\n',
            [
                "A feature suggestion was submitted from Prototype Manager.",
                string.Empty,
                "What is the feature?",
                suggestion.Feature,
                string.Empty,
                "What problem would it solve?",
                suggestion.Problem,
                string.Empty,
                "Suggested by:",
                suggestion.SuggestedBy
            ]);
}

internal sealed class NullFeatureSuggestionNotifier : IFeatureSuggestionNotifier
{
    public Task NotifyFeatureSuggestedAsync(
        FeatureSuggestion suggestion,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
