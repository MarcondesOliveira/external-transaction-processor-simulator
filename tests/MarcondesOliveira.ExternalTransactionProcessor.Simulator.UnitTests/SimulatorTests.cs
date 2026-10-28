using System.Globalization;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.UnitTests;

public sealed class SimulatorTests
{
    [Theory]
    [InlineData("portfolio-account-001", SimulatedOutcome.Succeeded)]
    [InlineData("reject-account-001", SimulatedOutcome.Rejected)]
    [InlineData("fail-account-001", SimulatedOutcome.Failed)]
    public void ScenarioResolverUsesDestinationPrefix(
        string accountId,
        SimulatedOutcome expected)
    {
        Assert.Equal(expected, SimulationScenarioResolver.Resolve(accountId));
    }

    [Fact]
    public async Task SubmissionReturnsAcceptedOperation()
    {
        var repository = new StubRepository(CreateOperationResultKind.Created);
        var service = new SubmissionService(
            repository,
            new FixedTimeProvider(
                DateTimeOffset.Parse("2026-10-28T12:00:00Z", CultureInfo.InvariantCulture)));

        var response = await service.SubmitAsync(
            ValidRequest(),
            "request-key-0001",
            Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"),
            CancellationToken.None);

        Assert.Equal("ACCEPTED", response.Status);
        Assert.Equal(ValidRequest().TransactionId, response.TransactionId);
        Assert.NotEqual(Guid.Empty, response.ProcessorTransactionId);
    }

    [Fact]
    public async Task SubmissionRejectsInvalidHeaders()
    {
        var service = new SubmissionService(
            new StubRepository(CreateOperationResultKind.Created),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<SubmissionValidationException>(
            () => service.SubmitAsync(
                ValidRequest(),
                "short",
                Guid.Empty,
                CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "Idempotency-Key");
        Assert.Contains(exception.Errors, error => error.Field == "X-Correlation-Id");
    }

    [Fact]
    public async Task SubmissionRejectsReusedKeyWithDifferentRequest()
    {
        var service = new SubmissionService(
            new StubRepository(CreateOperationResultKind.Conflict),
            TimeProvider.System);

        await Assert.ThrowsAsync<SubmissionIdempotencyConflictException>(
            () => service.SubmitAsync(
                ValidRequest(),
                "request-key-0001",
                Guid.NewGuid(),
                CancellationToken.None));
    }

    [Theory]
    [InlineData(SimulatedOutcome.Succeeded, "SUCCEEDED", "APPROVED")]
    [InlineData(SimulatedOutcome.Rejected, "REJECTED", "DESTINATION_REJECTED")]
    [InlineData(SimulatedOutcome.Failed, "FAILED", "PROCESSING_ERROR")]
    public async Task CallbackUsesExpectedOutcome(
        SimulatedOutcome outcome,
        string expectedStatus,
        string expectedCode)
    {
        var client = new RecordingCallbackClient();
        var service = new CallbackProcessingService(
            client,
            new FixedTimeProvider(
                DateTimeOffset.Parse("2026-10-28T12:01:00Z", CultureInfo.InvariantCulture)),
            TimeSpan.Zero);

        await service.ProcessAsync(Operation(outcome), CancellationToken.None);

        Assert.NotNull(client.Callback);
        Assert.Equal(expectedStatus, client.Callback.Status);
        Assert.Equal(expectedCode, client.Callback.Result.Code);
    }

    private static SubmitTransactionRequest ValidRequest() =>
        new(
            Guid.Parse("11111111-2222-4333-8444-555555555555"),
            "TRANSFER",
            new Money("250.75", "BRL"),
            new Destination("portfolio-account-001"),
            new Uri("https://callback.example.test/transactions"),
            "manual-test-0001");

    private static ProcessorOperation Operation(SimulatedOutcome outcome) =>
        new(
            "request-key-0001",
            "hash",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TRANSFER",
            "250.75",
            "BRL",
            "portfolio-account-001",
            new Uri("https://callback.example.test/transactions"),
            null,
            outcome,
            DateTimeOffset.UtcNow);

    private sealed class StubRepository(CreateOperationResultKind resultKind)
        : IProcessorOperationRepository
    {
        public Task<CreateOperationResult> CreateOrGetAsync(
            ProcessorOperation operation,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new CreateOperationResult(
                    resultKind,
                    resultKind == CreateOperationResultKind.Conflict ? null : operation));
    }

    private sealed class RecordingCallbackClient : ICallbackClient
    {
        public TransactionProcessed? Callback { get; private set; }

        public Task SendAsync(
            Uri callbackUrl,
            Guid correlationId,
            TransactionProcessed callback,
            CancellationToken cancellationToken)
        {
            Callback = callback;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
