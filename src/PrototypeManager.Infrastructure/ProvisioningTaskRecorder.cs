using Microsoft.Extensions.Logging;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal static class ProvisioningTaskRecorder
{
    public static Task RecordCompletedAsync(
        IAuditService auditService,
        ILogger logger,
        Guid prototypeId,
        string appName,
        string taskId)
    {
        logger.LogInformation(
            "Prototype {PrototypeId} ({AppName}): provisioning task {TaskId} completed.",
            prototypeId,
            appName,
            taskId);

        return auditService.RecordAsync(prototypeId, "system", ProvisioningTasks.CompletedAction, taskId);
    }
}
