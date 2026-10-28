using System.Net;
using System.Net.Http.Json;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.IntegrationTests;

public sealed class ApiTests : IClassFixture<SimulatorFactory>
{
    private readonly HttpClient client;

    public ApiTests(SimulatorFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task PostReturnsAccepted()
    {
        using var request = Request("request-key-0001");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AcceptedTransactionResponse>();
        Assert.NotNull(body);
        Assert.Equal("ACCEPTED", body.Status);
    }

    [Fact]
    public async Task PostReturnsValidationErrorForMissingHeaders()
    {
        using var response = await client.PostAsJsonAsync("/v1/transactions", Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("VALIDATION_ERROR", body?.Code);
    }

    [Fact]
    public async Task PostReturnsConflictForChangedRequestWithSameKey()
    {
        using var firstRequest = Request("request-key-conflict");
        using var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        var changed = Body() with { TransactionType = "PIX" };
        using var secondRequest = Request("request-key-conflict", changed);
        using var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private static HttpRequestMessage Request(
        string idempotencyKey,
        SubmitTransactionRequest? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transactions")
        {
            Content = JsonContent.Create(body ?? Body())
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        return request;
    }

    private static SubmitTransactionRequest Body() =>
        new(
            Guid.Parse("11111111-2222-4333-8444-555555555555"),
            "TRANSFER",
            new Money("250.75", "BRL"),
            new Destination("portfolio-account-001"),
            new Uri("https://callback.example.test/transactions"),
            "manual-test-0001");
}

public sealed class SimulatorFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProcessorOperationRepository>();
            services.AddSingleton<IProcessorOperationRepository, InMemoryRepository>();
        });
    }

    private sealed class InMemoryRepository : IProcessorOperationRepository
    {
        private readonly Dictionary<string, ProcessorOperation> operations =
            new(StringComparer.Ordinal);

        public Task<CreateOperationResult> CreateOrGetAsync(
            ProcessorOperation operation,
            CancellationToken cancellationToken)
        {
            if (!operations.TryGetValue(operation.IdempotencyKey, out var existing))
            {
                operations[operation.IdempotencyKey] = operation;
                return Task.FromResult(
                    new CreateOperationResult(CreateOperationResultKind.Created, operation));
            }

            return Task.FromResult(
                string.Equals(existing.RequestHash, operation.RequestHash, StringComparison.Ordinal)
                    ? new CreateOperationResult(CreateOperationResultKind.Existing, existing)
                    : new CreateOperationResult(CreateOperationResultKind.Conflict));
        }
    }
}
