using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.IntegrationTests;

public sealed class SubmissionLoggingTests
{
    [Fact]
    public async Task AcceptedAndReplayLogProcessorIdentityAndCurrentRequestCorrelation()
    {
        using var logs = new SubmissionLogProvider();
        using var factory = new SimulatorFactory();
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILoggerProvider>(logs)));
        using var client = application.CreateClient();
        var firstCorrelation = Guid.NewGuid();
        var secondCorrelation = Guid.NewGuid();
        using var first = await client.SendAsync(Request(firstCorrelation), CancellationToken.None);
        using var replay = await client.SendAsync(Request(secondCorrelation), CancellationToken.None);
        var accepted = await first.Content.ReadFromJsonAsync<AcceptedTransactionResponse>(CancellationToken.None);
        Assert.NotNull(accepted);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        var events = logs.Events.ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, entry =>
        {
            Assert.Equal("SimulatorSubmissionHandled", entry["EventName"]);
            Assert.Equal(202, entry["HttpStatus"]);
            Assert.Equal(accepted.TransactionId, entry["TransactionId"]);
            Assert.Equal(accepted.ProcessorTransactionId, entry["ProcessorTransactionId"]);
        });
        Assert.Equal(firstCorrelation, events[0]["RequestCorrelationId"]);
        Assert.Equal(secondCorrelation, events[1]["RequestCorrelationId"]);
        AssertNoPayload(logs);
    }

    [Fact]
    public async Task ValidationAndConflictHaveCorrelatedLogs()
    {
        using var logs = new SubmissionLogProvider();
        using var factory = new SimulatorFactory();
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ILoggerProvider>(logs)));
        using var client = application.CreateClient();
        var correlation = Guid.NewGuid();
        using var first = await client.SendAsync(Request(correlation), CancellationToken.None);
        using var conflict = await client.SendAsync(Request(correlation, amount: "999.99"), CancellationToken.None);
        using var invalid = await client.SendAsync(Request(correlation, amount: "0.00"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var events = logs.Events.ToArray();
        Assert.Equal(3, events.Length);
        Assert.Equal(409, events[1]["HttpStatus"]);
        Assert.Equal(400, events[2]["HttpStatus"]);
        Assert.All(events, entry => Assert.Equal(correlation, entry["RequestCorrelationId"]));
        AssertNoPayload(logs);
    }

    [Fact]
    public async Task InfrastructureFailureLogsTypesWithoutLeakingExceptionMessages()
    {
        using var logs = new SubmissionLogProvider();
        using var factory = new SimulatorFactory();
        using var application = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.RemoveAll<IProcessorOperationRepository>();
            services.AddSingleton<IProcessorOperationRepository, FailingRepository>();
        }));
        using var client = application.CreateClient();
        var correlation = Guid.NewGuid();
        using var request = Request(correlation);
        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain("private-provider-response", responseBody);
        Assert.DoesNotContain("private-provider-timeout", responseBody);
        var entry = Assert.Single(logs.Events);
        Assert.Equal("SimulatorSubmissionFailed", entry["EventName"]);
        Assert.Equal(correlation, entry["RequestCorrelationId"]);
        Assert.Equal(nameof(InvalidOperationException), entry["ErrorType"]);
        Assert.Equal(nameof(TimeoutException), entry["CauseType"]);
        AssertNoPayload(logs);
        Assert.DoesNotContain("private-", logs.Text);
    }

    private static HttpRequestMessage Request(Guid correlation, string amount = "250.75")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/transactions")
        {
            Content = JsonContent.Create(new SubmitTransactionRequest(
                Guid.Parse("11111111-2222-4333-8444-555555555555"), "TRANSFER",
                new Money(amount, "BRL"), new Destination("private-account"),
                new Uri("https://private-callback.invalid/transactions"), "private-reference"))
        };
        request.Headers.Add("Idempotency-Key", "private-idempotency-key");
        request.Headers.Add("X-Correlation-Id", correlation.ToString());
        return request;
    }

    private static void AssertNoPayload(SubmissionLogProvider logs)
    {
        Assert.DoesNotContain("private-account", logs.Text);
        Assert.DoesNotContain("private-reference", logs.Text);
        Assert.DoesNotContain("private-idempotency-key", logs.Text);
        Assert.DoesNotContain("private-callback", logs.Text);
        Assert.DoesNotContain("250.75", logs.Text);
        Assert.DoesNotContain("999.99", logs.Text);
    }

    private sealed class FailingRepository : IProcessorOperationRepository
    {
        public Task<CreateOperationResult> CreateOrGetAsync(ProcessorOperation operation,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "private-provider-response", new TimeoutException("private-provider-timeout"));
    }
}

internal sealed class SubmissionLogProvider : ILoggerProvider
{
    public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Events { get; } = new();
    public string Text => string.Join('\n', Events.SelectMany(entry => entry.Select(pair => $"{pair.Key}={pair.Value}")));
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Events);
    public void Dispose() { }

    private sealed class RecordingLogger(string category, ConcurrentQueue<IReadOnlyDictionary<string, object?>> events) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (category == "SimulatorSubmission" && state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                events.Enqueue(values.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
