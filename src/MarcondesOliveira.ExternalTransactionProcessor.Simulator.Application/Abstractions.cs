namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public interface IProcessorOperationRepository
{
    Task<CreateOperationResult> CreateOrGetAsync(
        ProcessorOperation operation,
        CancellationToken cancellationToken);
}

public interface ICallbackClient
{
    Task SendAsync(
        Uri callbackUrl,
        Guid correlationId,
        TransactionProcessed callback,
        CancellationToken cancellationToken);
}
