using Microsoft.Extensions.Logging;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal static class ProvisioningActivityRecorder
{
    public static async Task RecordStepAsync(
        IAuditService auditService,
        ILogger logger,
        Guid prototypeId,
        string appName,
        string message)
    {
        logger.LogInformation(
            "Prototype {PrototypeId} ({AppName}): {Message}",
            prototypeId,
            appName,
            message);

        await auditService.RecordAsync(prototypeId, "system", ProvisioningActivity.StepAction, message);
    }
}
