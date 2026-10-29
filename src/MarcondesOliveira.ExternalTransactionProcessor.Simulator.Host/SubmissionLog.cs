namespace ExternalProcessor.Simulator;

internal static partial class SubmissionLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Simulated submission timeout for transaction {TransactionId}, correlation {CorrelationId}; no operation accepted.")]
    public static partial void SimulatedTimeout(ILogger logger, Guid transactionId, Guid correlationId);
}
