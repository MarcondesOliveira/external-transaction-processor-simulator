namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public enum SimulatedOutcome
{
    Succeeded,
    Rejected,
    Failed
}

public sealed record ProcessorOperation(
    string IdempotencyKey,
    string RequestHash,
    Guid ProcessorTransactionId,
    Guid CallbackEventId,
    Guid TransactionId,
    Guid CorrelationId,
    string TransactionType,
    string AmountValue,
    string AmountCurrency,
    string DestinationAccountId,
    Uri CallbackUrl,
    string? ExternalReference,
    SimulatedOutcome Outcome,
    DateTimeOffset AcceptedAt);

public enum CreateOperationResultKind
{
    Created,
    Existing,
    Conflict
}

public sealed record CreateOperationResult(
    CreateOperationResultKind Kind,
    ProcessorOperation? Operation = null);
