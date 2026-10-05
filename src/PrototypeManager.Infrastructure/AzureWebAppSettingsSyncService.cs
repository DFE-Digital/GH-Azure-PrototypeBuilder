using Azure.ResourceManager.AppService.Models;
using PrototypeManager.Application;
using PrototypeManager.Domain;

namespace PrototypeManager.Infrastructure;

internal sealed class AzureWebAppSettingsSyncService(AzureWebAppResourceResolver resourceResolver)
{

    public async Task<string?> GetApplicationSettingAsync(
        Prototype prototype,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (!resourceResolver.IsAzureConfigured(prototype))
        {
            return null;
        }

        var webSite = await resourceResolver.GetWebSiteAsync(prototype, cancellationToken);
        var currentSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);

        return currentSettings.Value.Properties.TryGetValue(key, out var value)
            ? value
            : null;
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
            properties[key] = value;
        }

        var updated = new AppServiceConfigurationDictionary();
        foreach (var (key, value) in properties)
        {
            updated.Properties[key] = value;
        }

        await webSite.UpdateApplicationSettingsAsync(updated, cancellationToken);
    }
}
