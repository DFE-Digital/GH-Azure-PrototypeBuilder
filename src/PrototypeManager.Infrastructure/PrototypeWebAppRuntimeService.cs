using System.Collections.Concurrent;
using Azure.ResourceManager.AppService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class MockPrototypeWebAppRuntimeService : IPrototypeWebAppRuntimeService
{
    private static readonly ConcurrentDictionary<Guid, bool> StoppedPrototypes = new();

    public Task<WebAppRuntimeStatus> GetStatusAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        if (prototype.Status != PrototypeStatus.Live ||
            string.IsNullOrWhiteSpace(prototype.AzureWebAppName))
        {
            return Task.FromResult(UnknownStatus());
        }

        var stopped = StoppedPrototypes.TryGetValue(prototype.Id, out var isStopped) && isStopped;

        return Task.FromResult(stopped
            ? StoppedStatus()
            : RunningStatus());
    }

    public Task StartAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        StoppedPrototypes[prototype.Id] = false;
        return Task.CompletedTask;
    }

    public Task StopAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        StoppedPrototypes[prototype.Id] = true;
        return Task.CompletedTask;
    }

    private static WebAppRuntimeStatus UnknownStatus() => new()
    {
        PowerState = WebAppPowerState.Unknown,
        Label = "Unknown",
        CanStart = false,
        CanStop = false
    };

    private static WebAppRuntimeStatus RunningStatus() => new()
    {
        PowerState = WebAppPowerState.Running,
        Label = "Running",
        CanStart = false,
        CanStop = true
    };

    private static WebAppRuntimeStatus StoppedStatus() => new()
    {
        PowerState = WebAppPowerState.Stopped,
        Label = "Stopped",
        CanStart = true,
        CanStop = false
    };
}

internal sealed class AzurePrototypeWebAppRuntimeService(
    AzureWebAppResourceResolver resourceResolver,
    IOptions<ProvisioningOptions> provisioningOptions,
    ILogger<AzurePrototypeWebAppRuntimeService> logger) : IPrototypeWebAppRuntimeService
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    public async Task<WebAppRuntimeStatus> GetStatusAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        if (!CanManageRuntime(prototype))
        {
            return UnknownStatus();
        }

        try
        {
            var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
            var state = webSite.Data.State?.ToString() ?? string.Empty;

            return state switch
            {
                "Running" => RunningStatus(),
                "Stopped" => StoppedStatus(),
                _ => new WebAppRuntimeStatus
                {
                    PowerState = WebAppPowerState.Unknown,
                    Label = string.IsNullOrWhiteSpace(state) ? "Unknown" : state,
                    CanStart = !string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase),
                    CanStop = string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase)
                }
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to read Azure web app state for prototype {PrototypeId}.", prototype.Id);
            return UnknownStatus();
        }
    }

    public async Task StartAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        EnsureCanManageRuntime(prototype);
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        await webSite.StartAsync(cancellationToken);
    }

    public async Task StopAsync(Prototype prototype, CancellationToken cancellationToken = default)
    {
        EnsureCanManageRuntime(prototype);
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        await webSite.StopAsync(cancellationToken);
    }

    private bool CanManageRuntime(Prototype prototype) =>
        prototype.Status == PrototypeStatus.Live &&
        !string.IsNullOrWhiteSpace(prototype.AzureWebAppName) &&
        resourceResolver.IsAzureConfigured(prototype);

    private void EnsureCanManageRuntime(Prototype prototype)
    {
        if (!CanManageRuntime(prototype))
        {
            throw new InvalidOperationException("This prototype does not have a running Azure web app to control.");
        }
    }

    private static WebAppRuntimeStatus UnknownStatus() => new()
    {
        PowerState = WebAppPowerState.Unknown,
        Label = "Unknown",
        CanStart = false,
        CanStop = false
    };

    private static WebAppRuntimeStatus RunningStatus() => new()
    {
        PowerState = WebAppPowerState.Running,
        Label = "Running",
        CanStart = false,
        CanStop = true
    };

    private static WebAppRuntimeStatus StoppedStatus() => new()
    {
        PowerState = WebAppPowerState.Stopped,
        Label = "Stopped",
        CanStart = true,
        CanStop = false
    };
}

internal sealed class PrototypeWebAppRuntimeService(
    MockPrototypeWebAppRuntimeService mockService,
    AzurePrototypeWebAppRuntimeService azureService,
    IOptions<ProvisioningOptions> provisioningOptions) : IPrototypeWebAppRuntimeService
{
    private readonly ProvisioningOptions _provisioningOptions = provisioningOptions.Value;

    private IPrototypeWebAppRuntimeService Implementation =>
        _provisioningOptions.IsAzureMode ? azureService : mockService;

    public Task<WebAppRuntimeStatus> GetStatusAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.GetStatusAsync(prototype, cancellationToken);

    public Task StartAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.StartAsync(prototype, cancellationToken);

    public Task StopAsync(Prototype prototype, CancellationToken cancellationToken = default) =>
        Implementation.StopAsync(prototype, cancellationToken);
}
