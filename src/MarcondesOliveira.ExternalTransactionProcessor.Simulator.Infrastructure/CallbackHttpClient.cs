using System.Net.Http.Json;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

public sealed class CallbackHttpClient(HttpClient httpClient) : ICallbackClient
{
    public async Task SendAsync(
        Uri callbackUrl,
        Guid correlationId,
        TransactionProcessed callback,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, callbackUrl)
        {
            Content = JsonContent.Create(callback)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CallbackDeliveryException(
                    $"Callback endpoint returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (CallbackDeliveryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new CallbackDeliveryException("Callback delivery failed.", exception);
        }
    }
}
