using Azure.ResourceManager.AppService.Models;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class AzureWebAppSettingsSyncService(AzureWebAppResourceResolver resourceResolver)
{
    public bool IsAzureConfigured(Prototype prototype) =>
        resourceResolver.IsAzureConfigured(prototype);

    public async Task<string?> GetApplicationSettingAsync(
        Prototype prototype,
        string key,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetApplicationSettingsAsync(prototype, cancellationToken);
        return settings.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetApplicationSettingsAsync(
        Prototype prototype,
        CancellationToken cancellationToken = default)
    {
        if (!resourceResolver.IsAzureConfigured(prototype))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        var currentSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);

        return currentSettings.Value.Properties.ToDictionary(
            item => item.Key,
            item => item.Value ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetApplicationSettingAsync(
        Prototype prototype,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        var currentSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);
        var properties = currentSettings.Value.Properties.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        properties[key] = value;
        await SaveApplicationSettingsAsync(webSite, properties, cancellationToken);
    }

    public async Task ApplyUserSettingsAsync(
        Prototype prototype,
        IReadOnlyDictionary<string, string> userSettings,
        CancellationToken cancellationToken = default)
    {
        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        var currentSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);
        var properties = currentSettings.Value.Properties.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in properties.Keys.ToList())
        {
            if (!PrototypeAppSettingCatalog.IsProtectedAzureKey(key) && !userSettings.ContainsKey(key))
            {
                properties.Remove(key);
            }
        }

        foreach (var (key, value) in userSettings)
        {
            // Never overwrite PASSWORD or other protected keys from user config-var sync.
            if (PrototypeAppSettingCatalog.IsProtectedAzureKey(key))
            {
                continue;
            }

            properties[key] = value;
        }

        await SaveApplicationSettingsAsync(webSite, properties, cancellationToken);
    }

    private static async Task SaveApplicationSettingsAsync(
        Azure.ResourceManager.AppService.WebSiteResource webSite,
        Dictionary<string, string?> properties,
        CancellationToken cancellationToken)
    {
        var updated = new AppServiceConfigurationDictionary();
        foreach (var (key, value) in properties)
        {
            updated.Properties[key] = value;
        }

        await webSite.UpdateApplicationSettingsAsync(updated, cancellationToken);
    }
}
