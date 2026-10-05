using PrototypeManager.Domain;

namespace PrototypeManager.Web.Pages.Prototypes;

public sealed class PrototypeActivityItem
{
    public required string Id { get; init; }
    public required string Action { get; init; }
    public required string Status { get; init; }
    public int? Attempt { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string ActorEmail { get; init; }
    public required string LogDetails { get; init; }
    public bool HasLogs { get; init; }
    public string? Summary { get; init; }
    public bool IsJob => Id.StartsWith("job-", StringComparison.Ordinal);
}

internal static class PrototypeActivityFeedBuilder
{
    public static IReadOnlyList<PrototypeActivityItem> Build(
        IReadOnlyList<ProvisioningJob> jobs,
        IReadOnlyList<AuditEvent> auditEvents)
    {
        var items = new List<PrototypeActivityItem>();

        foreach (var job in jobs)
        {
            items.Add(new PrototypeActivityItem
            {
                Id = $"job-{job.Id}",
                Action = DetailsModel.GetJobTypeLabel(job.Type),
                Status = job.Status.ToString(),
                Attempt = job.AttemptCount,
                OccurredAt = job.CompletedAt ?? job.StartedAt ?? job.CreatedAt,
                ActorEmail = FindJobActor(job, auditEvents),
                LogDetails = BuildJobLogDetails(job),
                HasLogs = true,
                Summary = BuildJobSummary(job)
            });
        }

        foreach (var audit in auditEvents.Where(item =>
                     !string.Equals(item.Action, ProvisioningActivity.StepAction, StringComparison.Ordinal)))
        {
            if (IsJobAuditDuplicate(audit, jobs))
            {
                continue;
            }

            items.Add(new PrototypeActivityItem
            {
                Id = $"audit-{audit.Id}",
                Action = audit.Action,
                Status = "Recorded",
                Attempt = null,
                OccurredAt = audit.CreatedAt,
                ActorEmail = string.IsNullOrWhiteSpace(audit.ActorEmail) ? "System" : audit.ActorEmail,
                LogDetails = audit.Details,
                HasLogs = !string.IsNullOrWhiteSpace(audit.Details),
                Summary = TruncateSummary(audit.Details)
            });
        }

        return items
            .OrderByDescending(item => item.OccurredAt)
            .ToList();
    }

    private static bool IsJobAuditDuplicate(AuditEvent audit, IReadOnlyList<ProvisioningJob> jobs)
    {
        if (audit.PrototypeId is null)
        {
            return false;
        }

        return audit.Action switch
        {
            "Prototype requested" => jobs.Any(job => job.Type == ProvisioningJobType.Create),
            "Password change queued" => jobs.Any(job =>
                job.Type == ProvisioningJobType.UpdatePassword &&
                Math.Abs((job.CreatedAt - audit.CreatedAt).TotalMinutes) < 2),
            "Delete queued" => jobs.Any(job => job.Type == ProvisioningJobType.Delete),
            "Retry queued" => jobs.Any(job => job.Type == ProvisioningJobType.Retry),
            "GitHub deployment queued" => jobs.Any(job => job.Type == ProvisioningJobType.ConfigureGitHubDeployment),
            _ => false
        };
    }

    private static string FindJobActor(ProvisioningJob job, IReadOnlyList<AuditEvent> auditEvents)
    {
        var matchingAudit = auditEvents
            .Where(item => Math.Abs((item.CreatedAt - job.CreatedAt).TotalMinutes) <= 5)
            .OrderBy(item => Math.Abs((item.CreatedAt - job.CreatedAt).Ticks))
            .FirstOrDefault(item => !string.Equals(item.ActorEmail, "system", StringComparison.OrdinalIgnoreCase));

        return matchingAudit?.ActorEmail ?? "System";
    }

    private static string BuildJobLogDetails(ProvisioningJob job)
    {
        var lines = new List<string>
        {
            $"Job type: {job.Type}",
            $"Status: {job.Status}",
            $"Attempt: {job.AttemptCount}",
            $"Queued: {job.CreatedAt:dd MMM yyyy HH:mm} UTC"
        };

        if (job.StartedAt is not null)
        {
            lines.Add($"Started: {job.StartedAt:dd MMM yyyy HH:mm} UTC");
        }

        if (job.CompletedAt is not null)
        {
            lines.Add($"Completed: {job.CompletedAt:dd MMM yyyy HH:mm} UTC");
        }

        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
        {
            lines.Add(string.Empty);
            lines.Add("Error");
            lines.Add(job.ErrorMessage);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? BuildJobSummary(ProvisioningJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
        {
            return TruncateSummary(job.ErrorMessage);
        }

        return job.Status switch
        {
            ProvisioningJobStatus.Succeeded => "Completed successfully.",
            ProvisioningJobStatus.Running => "Job is currently running.",
            ProvisioningJobStatus.Queued => "Waiting to start.",
            _ => null
        };
    }

    private static string? TruncateSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ReplaceLineEndings(" ");
        const int maxLength = 160;

        return trimmed.Length <= maxLength
            ? trimmed
            : $"{trimmed[..(maxLength - 1)].TrimEnd()}…";
    }
}

internal static class PrototypeContactPresentation
{
    public static string GetDisplayName(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "Unknown";
        }

        var localPart = email.Split('@')[0];
        var words = localPart
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return words.Length == 0
            ? email
            : string.Join(' ', words.Select(word =>
                word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}
