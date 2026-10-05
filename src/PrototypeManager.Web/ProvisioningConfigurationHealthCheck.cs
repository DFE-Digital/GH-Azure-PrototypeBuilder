using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Web;

public sealed class ProvisioningConfigurationHealthCheck(IOptions<ProvisioningOptions> provisioningOptions) : IHealthCheck
{
    private readonly ProvisioningOptions _options = provisioningOptions.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Mode))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Provisioning mode is not configured."));
        }

        if (_options.IsAzureMode && string.IsNullOrWhiteSpace(_options.AzureSubscriptionId))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Azure mode is enabled but no Azure subscription ID is configured."));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Provisioning mode: {_options.Mode}"));
    }
}
