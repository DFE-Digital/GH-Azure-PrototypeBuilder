using Microsoft.AspNetCore.Mvc;
using PrototypeManager.Application;
using PrototypeManager.Domain;
using PrototypeManager.Web.Pages.Prototypes;

namespace PrototypeManager.Web.Pages;

internal static class LivePageSnapshotBuilder
{
    private static readonly TimeSpan RecentEventWindow = TimeSpan.FromMinutes(30);

    public static LivePageSnapshot Build(
        LiveOperationsData data,
        IProvisioningProgressService provisioningProgressService,
        IUrlHelper urlHelper)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var prototypes = data.Prototypes;
        var activeOperations = BuildActiveOperations(data, provisioningProgressService, urlHelper);
        var prototypeRows = BuildPrototypeRows(prototypes, data.JobsByPrototypeId, urlHelper);
        var recentEvents = BuildRecentEvents(data, urlHelper);

        return new LivePageSnapshot
        {
            UpdatedAt = updatedAt,
            Summary = new LiveSummary
            {
                Total = prototypes.Count,
                Live = prototypes.Count(prototype => prototype.Status == PrototypeStatus.Live),
                InProgress = prototypes.Count(prototype =>
                    prototype.Status is PrototypeStatus.Requested
                        or PrototypeStatus.Queued
                        or PrototypeStatus.Provisioning
                        or PrototypeStatus.Deleting),
                Failed = prototypes.Count(prototype => prototype.Status == PrototypeStatus.Failed),
                ActiveJobs = activeOperations.Count
            },
            ActiveOperations = activeOperations,
            Prototypes = prototypeRows,
            RecentEvents = recentEvents
        };
    }

    private static IReadOnlyList<LiveActiveOperation> BuildActiveOperations(
        LiveOperationsData data,
        IProvisioningProgressService provisioningProgressService,
        IUrlHelper urlHelper)
    {
        var operations = new List<LiveActiveOperation>();

        foreach (var prototype in data.Prototypes)
        {
            if (!data.JobsByPrototypeId.TryGetValue(prototype.Id, out var jobs))
            {
                continue;
            }

            var activeJobs = jobs
                .Where(job => job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running)
                .ToList();

            foreach (var job in activeJobs)
            {
                data.AuditEventsByPrototypeId.TryGetValue(prototype.Id, out var auditEvents);
                auditEvents ??= [];

                var progress = provisioningProgressService.Build(prototype, jobs, auditEvents);
                var currentStep = progress.Tasks
                    .FirstOrDefault(task => string.Equals(task.Status, "InProgress", StringComparison.Ordinal))
                    ?? progress.Tasks.FirstOrDefault(task => string.Equals(task.Status, "Pending", StringComparison.Ordinal));

                var isSetupJob = job.Type is ProvisioningJobType.Create
                    or ProvisioningJobType.Retry
                    or ProvisioningJobType.ScaffoldGitHubRepository
                    or ProvisioningJobType.ConfigureGitHubDeployment;

                operations.Add(new LiveActiveOperation
                {
                    PrototypeId = prototype.Id,
                    PrototypeName = prototype.Name,
                    BusinessAreaName = prototype.BusinessArea?.Name,
                    StatusLabel = ListModel.GetStatusLabel(prototype.Status),
                    StatusBadgeClass = ListModel.GetStatusBadgeClass(prototype.Status),
                    JobTypeLabel = DetailsModel.GetJobTypeLabel(job.Type),
                    JobStatus = job.Status.ToString(),
                    JobStatusLabel = GetJobStatusLabel(job.Status),
                    JobStatusBadgeClass = GetJobStatusBadgeClass(job.Status),
                    CurrentStepLabel = isSetupJob ? currentStep?.Label : null,
                    StartedAt = job.StartedAt ?? job.CreatedAt,
                    DetailUrl = urlHelper.Page("/Prototypes/Details", new { id = prototype.Id }) ?? string.Empty,
                    SetupUrl = isSetupJob && provisioningProgressService.ShouldShowProgress(prototype, jobs, auditEvents)
                        ? urlHelper.Page("/Prototypes/Setup", new { id = prototype.Id })
                        : null
                });
            }
        }

        return operations
            .OrderByDescending(operation => operation.JobStatus == ProvisioningJobStatus.Running.ToString())
            .ThenByDescending(operation => operation.StartedAt)
            .ToList();
    }

    private static IReadOnlyList<LivePrototypeRow> BuildPrototypeRows(
        IReadOnlyList<Prototype> prototypes,
        IReadOnlyDictionary<Guid, IReadOnlyList<ProvisioningJob>> jobsByPrototypeId,
        IUrlHelper urlHelper)
    {
        return prototypes
            .Select(prototype =>
            {
                jobsByPrototypeId.TryGetValue(prototype.Id, out var jobs);
                jobs ??= [];

                var activeJob = jobs.FirstOrDefault(job =>
                    job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running);

                return new LivePrototypeRow
                {
                    PrototypeId = prototype.Id,
                    PrototypeName = prototype.Name,
                    BusinessAreaName = prototype.BusinessArea?.Name,
                    OwnerEmail = prototype.OwnerEmail,
                    StatusLabel = ListModel.GetStatusLabel(prototype.Status),
                    StatusBadgeClass = ListModel.GetStatusBadgeClass(prototype.Status),
                    HasActiveJob = activeJob is not null,
                    ActiveJobLabel = activeJob is null ? null : DetailsModel.GetJobTypeLabel(activeJob.Type),
                    DetailUrl = urlHelper.Page("/Prototypes/Details", new { id = prototype.Id }) ?? string.Empty,
                    UpdatedAt = prototype.CreatedAt
                };
            })
            .OrderByDescending(row => row.HasActiveJob)
            .ThenByDescending(row => row.UpdatedAt)
            .ToList();
    }

    private static IReadOnlyList<LiveRecentEvent> BuildRecentEvents(
        LiveOperationsData data,
        IUrlHelper urlHelper)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(RecentEventWindow);
        var events = new List<LiveRecentEvent>();
        var prototypeLookup = data.Prototypes.ToDictionary(prototype => prototype.Id, prototype => prototype);

        foreach (var (prototypeId, jobs) in data.JobsByPrototypeId)
        {
            if (!prototypeLookup.TryGetValue(prototypeId, out var prototype))
            {
                continue;
            }

            foreach (var job in jobs.Where(job =>
                         job.Status is ProvisioningJobStatus.Queued or ProvisioningJobStatus.Running ||
                         (job.CompletedAt ?? job.StartedAt ?? job.CreatedAt) >= cutoff))
            {
                events.Add(new LiveRecentEvent
                {
                    Id = $"job-{job.Id}",
                    PrototypeId = prototypeId,
                    PrototypeName = prototype.Name,
                    Action = DetailsModel.GetJobTypeLabel(job.Type),
                    Status = job.Status.ToString(),
                    StatusLabel = GetJobStatusLabel(job.Status),
                    StatusBadgeClass = GetJobStatusBadgeClass(job.Status),
                    OccurredAt = job.CompletedAt ?? job.StartedAt ?? job.CreatedAt,
                    DetailUrl = urlHelper.Page("/Prototypes/Details", new { id = prototypeId }) ?? string.Empty,
                    Summary = BuildJobSummary(job)
                });
            }
        }

        foreach (var (prototypeId, auditEvents) in data.AuditEventsByPrototypeId)
        {
            if (!prototypeLookup.TryGetValue(prototypeId, out var prototype))
            {
                continue;
            }

            foreach (var auditEvent in auditEvents.Where(item =>
                         item.CreatedAt >= cutoff &&
                         !string.Equals(item.Action, ProvisioningActivity.StepAction, StringComparison.Ordinal) &&
                         !string.Equals(item.Action, ProvisioningTasks.CompletedAction, StringComparison.Ordinal)))
            {
                events.Add(new LiveRecentEvent
                {
                    Id = $"audit-{auditEvent.Id}",
                    PrototypeId = prototypeId,
                    PrototypeName = prototype.Name,
                    Action = auditEvent.Action,
                    Status = "Recorded",
                    StatusLabel = "Recorded",
                    StatusBadgeClass = "dfe-c-fe-badge dfe-c-fe-badge--muted",
                    OccurredAt = auditEvent.CreatedAt,
                    DetailUrl = urlHelper.Page("/Prototypes/Details", new { id = prototypeId }) ?? string.Empty,
                    Summary = string.IsNullOrWhiteSpace(auditEvent.Details) ? null : auditEvent.Details.Trim()
                });
            }
        }

        return events
            .OrderByDescending(item => item.OccurredAt)
            .DistinctBy(item => item.Id)
            .Take(30)
            .ToList();
    }

    private static string? BuildJobSummary(ProvisioningJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
        {
            return job.ErrorMessage.Trim();
        }

        return job.Status switch
        {
            ProvisioningJobStatus.Succeeded => "Completed successfully.",
            ProvisioningJobStatus.Running => "Job is currently running.",
            ProvisioningJobStatus.Queued => "Waiting to start.",
            _ => null
        };
    }

    private static string GetJobStatusLabel(ProvisioningJobStatus status) => status switch
    {
        ProvisioningJobStatus.Running => "Running",
        ProvisioningJobStatus.Queued => "Queued",
        ProvisioningJobStatus.Succeeded => "Succeeded",
        ProvisioningJobStatus.Failed => "Failed",
        _ => status.ToString()
    };

    private static string GetJobStatusBadgeClass(ProvisioningJobStatus status) => status switch
    {
        ProvisioningJobStatus.Running => "dfe-c-fe-badge dfe-c-fe-badge--info",
        ProvisioningJobStatus.Queued => "dfe-c-fe-badge dfe-c-fe-badge--muted",
        ProvisioningJobStatus.Succeeded => "dfe-c-fe-badge dfe-c-fe-badge--success",
        ProvisioningJobStatus.Failed => "dfe-c-fe-badge dfe-c-fe-badge--danger",
        _ => "dfe-c-fe-badge dfe-c-fe-badge--muted"
    };
}

public sealed class LivePageSnapshot
{
    public required DateTimeOffset UpdatedAt { get; init; }
    public required LiveSummary Summary { get; init; }
    public required IReadOnlyList<LiveActiveOperation> ActiveOperations { get; init; }
    public required IReadOnlyList<LivePrototypeRow> Prototypes { get; init; }
    public required IReadOnlyList<LiveRecentEvent> RecentEvents { get; init; }

    public static LivePageSnapshot Empty { get; } = new()
    {
        UpdatedAt = DateTimeOffset.UtcNow,
        Summary = new LiveSummary(),
        ActiveOperations = [],
        Prototypes = [],
        RecentEvents = []
    };

    public object ToJson() => new
    {
        updatedAt = UpdatedAt.ToString("O"),
        summary = new
        {
            total = Summary.Total,
            live = Summary.Live,
            inProgress = Summary.InProgress,
            failed = Summary.Failed,
            activeJobs = Summary.ActiveJobs
        },
        activeOperations = ActiveOperations.Select(operation => new
        {
            prototypeId = operation.PrototypeId,
            prototypeName = operation.PrototypeName,
            businessAreaName = operation.BusinessAreaName,
            statusLabel = operation.StatusLabel,
            statusBadgeClass = operation.StatusBadgeClass,
            jobTypeLabel = operation.JobTypeLabel,
            jobStatus = operation.JobStatus,
            jobStatusLabel = operation.JobStatusLabel,
            jobStatusBadgeClass = operation.JobStatusBadgeClass,
            currentStepLabel = operation.CurrentStepLabel,
            startedAt = operation.StartedAt.ToString("O"),
            detailUrl = operation.DetailUrl,
            setupUrl = operation.SetupUrl
        }),
        prototypes = Prototypes.Select(prototype => new
        {
            prototypeId = prototype.PrototypeId,
            prototypeName = prototype.PrototypeName,
            businessAreaName = prototype.BusinessAreaName,
            ownerEmail = prototype.OwnerEmail,
            statusLabel = prototype.StatusLabel,
            statusBadgeClass = prototype.StatusBadgeClass,
            hasActiveJob = prototype.HasActiveJob,
            activeJobLabel = prototype.ActiveJobLabel,
            detailUrl = prototype.DetailUrl,
            updatedAt = prototype.UpdatedAt.ToString("O")
        }),
        recentEvents = RecentEvents.Select(item => new
        {
            id = item.Id,
            prototypeId = item.PrototypeId,
            prototypeName = item.PrototypeName,
            action = item.Action,
            status = item.Status,
            statusLabel = item.StatusLabel,
            statusBadgeClass = item.StatusBadgeClass,
            occurredAt = item.OccurredAt.ToString("O"),
            detailUrl = item.DetailUrl,
            summary = item.Summary
        })
    };
}

public sealed class LiveSummary
{
    public int Total { get; init; }
    public int Live { get; init; }
    public int InProgress { get; init; }
    public int Failed { get; init; }
    public int ActiveJobs { get; init; }
}

public sealed class LiveActiveOperation
{
    public required Guid PrototypeId { get; init; }
    public required string PrototypeName { get; init; }
    public string? BusinessAreaName { get; init; }
    public required string StatusLabel { get; init; }
    public required string StatusBadgeClass { get; init; }
    public required string JobTypeLabel { get; init; }
    public required string JobStatus { get; init; }
    public required string JobStatusLabel { get; init; }
    public required string JobStatusBadgeClass { get; init; }
    public string? CurrentStepLabel { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required string DetailUrl { get; init; }
    public string? SetupUrl { get; init; }
}

public sealed class LivePrototypeRow
{
    public required Guid PrototypeId { get; init; }
    public required string PrototypeName { get; init; }
    public string? BusinessAreaName { get; init; }
    public required string OwnerEmail { get; init; }
    public required string StatusLabel { get; init; }
    public required string StatusBadgeClass { get; init; }
    public required bool HasActiveJob { get; init; }
    public string? ActiveJobLabel { get; init; }
    public required string DetailUrl { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed class LiveRecentEvent
{
    public required string Id { get; init; }
    public required Guid PrototypeId { get; init; }
    public required string PrototypeName { get; init; }
    public required string Action { get; init; }
    public required string Status { get; init; }
    public required string StatusLabel { get; init; }
    public required string StatusBadgeClass { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string DetailUrl { get; init; }
    public string? Summary { get; init; }
}
