using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using Simulator.Testing;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.UnitTests;

public sealed class SubmissionTimeoutTests
{
    [Theory]
    [InlineData("timeout-portfolio-001")]
    [InlineData("TIMEOUT-portfolio-001")]
    public async Task TimeoutWaitsTwelveSecondsWithoutPersistence(string accountId)
    {
        using var clock = new ControlledTimeProvider();
        var repository = new NoPersistenceRepository();
        var service = new SubmissionService(repository, clock);

        var pending = service.SubmitAsync(Request(accountId), "timeout-test-0001", Guid.NewGuid(), CancellationToken.None);
        await clock.Scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(12), clock.DueTime);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, repository.Calls);

        clock.CompleteDelay();
        await Assert.ThrowsAsync<SimulatedSubmissionTimeoutException>(() => pending);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task CancellingTimeoutDoesNotPersist()
    {
        using var clock = new ControlledTimeProvider();
        var repository = new NoPersistenceRepository();
        var service = new SubmissionService(repository, clock);
        using var cancellation = new CancellationTokenSource();

        var pending = service.SubmitAsync(Request("timeout-cancel"), "timeout-test-0002", Guid.NewGuid(), cancellation.Token);
        await clock.Scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task InvalidRequestIsRejectedBeforeTimeoutDelay()
    {
        using var clock = new ControlledTimeProvider();
        var repository = new NoPersistenceRepository();
        var service = new SubmissionService(repository, clock);

        await Assert.ThrowsAsync<SubmissionValidationException>(() => service.SubmitAsync(
            Request("timeout-invalid"), "short", Guid.Empty, CancellationToken.None));

        Assert.False(clock.Scheduled.Task.IsCompleted);
        Assert.Equal(0, repository.Calls);
    }

    private static SubmitTransactionRequest Request(string accountId) => new(
        Guid.NewGuid(), "TRANSFER", new Money("125.90", "BRL"), new Destination(accountId),
        new Uri("https://callback.invalid/callbacks/transactions"), "timeout-unit-test");

    private sealed class NoPersistenceRepository : IProcessorOperationRepository
    {
        public int Calls { get; private set; }

        public Task<CreateOperationResult> CreateOrGetAsync(
            ProcessorOperation operation,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Timeout scenarios must never call persistence.");
        }
    }
}
