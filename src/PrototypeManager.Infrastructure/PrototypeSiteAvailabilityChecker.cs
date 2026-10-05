using System.Net;
using PrototypeManager.Application;

namespace PrototypeManager.Infrastructure;

internal sealed class PrototypeSiteAvailabilityChecker(IHttpClientFactory httpClientFactory) : IPrototypeSiteAvailabilityChecker
{
    public async Task<PrototypeSiteAvailability> CheckAsync(string prototypeUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prototypeUrl))
        {
            return PrototypeSiteAvailability.Unavailable;
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient(PrototypeSiteVerifier.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, prototypeUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "PrototypeManager/1.0");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return PrototypeSiteAvailability.Ready;
            }

            return PrototypeSiteAvailability.Unavailable;
        }
        catch (HttpRequestException)
        {
            return PrototypeSiteAvailability.Unavailable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PrototypeSiteAvailability.Unavailable;
        }
    }
}
