using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class CompassCatalogService(
    IHttpClientFactory httpClientFactory,
    IOptions<CompassApiOptions> options,
    IMemoryCache memoryCache,
    ILogger<CompassCatalogService> logger) : ICompassCatalogService
{
    public const string HttpClientName = "CompassApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CompassApiOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IReadOnlyList<CompassWorkItemOption>> GetActiveWorkItemsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        return await memoryCache.GetOrCreateAsync(
            "compass:work-items:active",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration();
                return await FetchAllWorkItemsAsync(cancellationToken);
            }) ?? [];
    }

    public async Task<IReadOnlyList<CompassProductOption>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        return await memoryCache.GetOrCreateAsync(
            "compass:products:non-enterprise",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration();
                return await FetchAllProductsAsync(cancellationToken);
            }) ?? [];
    }

    public async Task<CompassWorkItemOption?> FindWorkItemAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var items = await GetActiveWorkItemsAsync(cancellationToken);
        return items.FirstOrDefault(item => item.Id == id);
    }

    public async Task<CompassProductOption?> FindProductAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var products = await GetProductsAsync(cancellationToken);
        return products.FirstOrDefault(product =>
            string.Equals(product.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<CompassWorkItemOption>> FetchAllWorkItemsAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<CompassWorkItemOption>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var payload = await GetAsync<CompassPagedResponse<CompassWorkItemDto>>(
                $"/api/v1/WorkItems?status=Active&page={page}&pageSize=100",
                cancellationToken);

            if (payload?.Data is null)
            {
                break;
            }

            results.AddRange(payload.Data
                .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .Select(item => new CompassWorkItemOption
                {
                    Id = item.Id,
                    ProjectCode = item.ProjectCode?.Trim() ?? string.Empty,
                    Title = item.Title.Trim()
                }));

            totalPages = Math.Max(1, payload.Pagination?.TotalPages ?? 1);
            page++;
        }

        return results
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<CompassProductOption>> FetchAllProductsAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<CompassProductOption>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var payload = await GetAsync<CompassPagedResponse<CompassProductDto>>(
                $"/api/v1/ServiceRegister/products?excludeEnterprise=true&page={page}&pageSize=500",
                cancellationToken);

            if (payload?.Data is null)
            {
                break;
            }

            results.AddRange(payload.Data
                .Where(product =>
                    string.Equals(product.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(product.Id) &&
                    !string.IsNullOrWhiteSpace(product.ProductName))
                .Select(product => new CompassProductOption
                {
                    Id = product.Id.Trim(),
                    UniqueId = product.UniqueId,
                    Name = product.ProductName.Trim(),
                    BusinessAreaSummary = FormatBusinessAreas(product)
                }));

            totalPages = Math.Max(1, payload.Pagination?.TotalPages ?? 1);
            page++;
        }

        return results
            .GroupBy(product => product.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "COMPASS API call failed for {Url} with {StatusCode}: {Body}",
                    relativeUrl,
                    (int)response.StatusCode,
                    body);
                return default;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "COMPASS API call failed for {Url}", relativeUrl);
            return default;
        }
    }

    private TimeSpan CacheDuration() =>
        TimeSpan.FromSeconds(Math.Clamp(_options.CatalogCacheSeconds, 30, 3600));

    private sealed class CompassPagedResponse<T>
    {
        public List<T> Data { get; set; } = [];
        public CompassPagination? Pagination { get; set; }
    }

    private sealed class CompassPagination
    {
        public int TotalPages { get; set; }
    }

    private sealed class CompassWorkItemDto
    {
        public int Id { get; set; }
        public string? ProjectCode { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private sealed class CompassProductDto
    {
        public string Id { get; set; } = string.Empty;
        public int UniqueId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? BusinessArea { get; set; }
        public string? BusinessAreaName { get; set; }
        public JsonElement BusinessAreas { get; set; }
    }

    private static string FormatBusinessAreas(CompassProductDto product)
    {
        var names = new List<string>();

        void AddName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (!names.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(trimmed);
            }
        }

        AddName(product.BusinessAreaName);
        AddName(product.BusinessArea);
        AddBusinessAreaElement(product.BusinessAreas, AddName);

        return names.Count == 0 ? string.Empty : string.Join(", ", names);
    }

    private static void AddBusinessAreaElement(JsonElement element, Action<string?> addName)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            addName(element.GetString());
            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                addName(item.GetString());
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (item.TryGetProperty("name", out var name))
            {
                addName(name.GetString());
            }

            if (item.TryGetProperty("Name", out var namePascal))
            {
                addName(namePascal.GetString());
            }

            if (item.TryGetProperty("title", out var title))
            {
                addName(title.GetString());
            }

            if (item.TryGetProperty("Title", out var titlePascal))
            {
                addName(titlePascal.GetString());
            }
        }
    }
}
