using System.Net;
using System.Net.Http.Json;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Simulator.Testing;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.IntegrationTests;

public sealed class TimeoutApiTests
{
    [Fact]
    public async Task TimeoutReturnsDelayed503WithoutAcceptingOperation()
    {
        using var clock = new ControlledTimeProvider();
        var repository = new NoPersistenceRepository();
        using var logs = new SubmissionLogProvider();
        using var factory = new SimulatorFactory();
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IProcessorOperationRepository>();
            services.AddSingleton<IProcessorOperationRepository>(repository);
        }));
        using var client = application.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transactions")
        {
            Content = JsonContent.Create(new SubmitTransactionRequest(
                Guid.NewGuid(), "TRANSFER", new Money("125.90", "BRL"),
                new Destination("timeout-api-test"), new Uri("https://callback.invalid/transactions"), null))
        };
        request.Headers.Add("Idempotency-Key", "timeout-api-0001");
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

        var pending = client.SendAsync(request);
        await clock.Scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(12), clock.DueTime);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, repository.Calls);
        clock.CompleteDelay();

        using var response = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("SIMULATED_TIMEOUT", error?.Code);
        Assert.Equal(0, repository.Calls);
        var handled = Assert.Single(logs.Events, entry => entry.ContainsKey("EventName"));
        Assert.Equal("SimulatorSubmissionHandled", handled["EventName"]);
        Assert.Equal(503, handled["HttpStatus"]);
    }

    private sealed class NoPersistenceRepository : IProcessorOperationRepository
    {
        public int Calls { get; private set; }

        public Task<CreateOperationResult> CreateOrGetAsync(
            ProcessorOperation operation,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Timeout scenario must not persist operations.");
        }
    }
}
