namespace ExternalProcessor.Simulator;

internal static partial class SubmissionLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Simulated submission timeout for transaction {TransactionId}, correlation {CorrelationId}; no operation accepted.")]
    public static partial void SimulatedTimeout(ILogger logger, Guid transactionId, Guid correlationId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "{EventName} httpStatus={HttpStatus} transactionId={TransactionId} requestCorrelationId={RequestCorrelationId} processorTransactionId={ProcessorTransactionId}")]
    public static partial void Handled(ILogger logger, string eventName, int httpStatus,
        Guid transactionId, Guid requestCorrelationId, Guid? processorTransactionId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error,
        Message = "{EventName} transactionId={TransactionId} requestCorrelationId={RequestCorrelationId} errorType={ErrorType} causeType={CauseType} stackTrace={StackTrace}")]
    public static partial void Failed(ILogger logger, string eventName, Guid transactionId,
        Guid requestCorrelationId, string errorType, string causeType, string? stackTrace);
}
