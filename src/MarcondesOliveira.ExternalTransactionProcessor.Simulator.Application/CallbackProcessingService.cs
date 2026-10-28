namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public sealed class CallbackProcessingService(
    ICallbackClient callbackClient,
    TimeProvider timeProvider,
    TimeSpan processingDelay)
{
    public async Task ProcessAsync(
        ProcessorOperation operation,
        CancellationToken cancellationToken)
    {
        if (processingDelay > TimeSpan.Zero)
        {
            await Task.Delay(processingDelay, timeProvider, cancellationToken);
        }

        var (status, code, message) = operation.Outcome switch
        {
            SimulatedOutcome.Succeeded =>
                ("SUCCEEDED", "APPROVED", "Transaction processed successfully"),
            SimulatedOutcome.Rejected =>
                ("REJECTED", "DESTINATION_REJECTED", "Destination rejected by simulation rule"),
            SimulatedOutcome.Failed =>
                ("FAILED", "PROCESSING_ERROR", "Simulated final processing failure"),
            _ => throw new InvalidOperationException("Unknown simulated outcome.")
        };

        var callback = new TransactionProcessed(
            1,
            "TransactionProcessed",
            operation.CallbackEventId,
            timeProvider.GetUtcNow(),
            operation.TransactionId,
            operation.CorrelationId,
            operation.ProcessorTransactionId,
            status,
            new ProcessingResult(code, message));

        await callbackClient.SendAsync(
            operation.CallbackUrl,
            operation.CorrelationId,
            callback,
            cancellationToken);
    }
}
