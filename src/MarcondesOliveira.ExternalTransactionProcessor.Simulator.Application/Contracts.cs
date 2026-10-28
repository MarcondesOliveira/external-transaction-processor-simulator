namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public sealed record SubmitTransactionRequest(
    Guid TransactionId,
    string TransactionType,
    Money Amount,
    Destination Destination,
    Uri CallbackUrl,
    string? ExternalReference);

public sealed record Money(string Value, string Currency);

public sealed record Destination(string AccountId);

public sealed record AcceptedTransactionResponse(
    Guid ProcessorTransactionId,
    Guid TransactionId,
    string Status,
    DateTimeOffset AcceptedAt);

public sealed record TransactionProcessed(
    int EventVersion,
    string EventType,
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TransactionId,
    Guid CorrelationId,
    Guid ProcessorTransactionId,
    string Status,
    ProcessingResult Result);

public sealed record ProcessingResult(string Code, string Message);

public sealed record ErrorResponse(
    string Code,
    string Message,
    IReadOnlyCollection<ErrorDetail>? Details = null);

public sealed record ErrorDetail(string Field, string Message);
