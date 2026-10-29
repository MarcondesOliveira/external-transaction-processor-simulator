namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public sealed class SubmissionValidationException(
    IReadOnlyCollection<ErrorDetail> errors)
    : Exception("The external transaction request is invalid.")
{
    public IReadOnlyCollection<ErrorDetail> Errors { get; } = errors;
}

public sealed class SubmissionIdempotencyConflictException()
    : Exception("The idempotency key was already used with a different request.");

public sealed class SimulatedSubmissionTimeoutException()
    : Exception("Simulated submission exceeded the request client's timeout; no operation was accepted.");

public sealed class CallbackDeliveryException(
    string message,
    Exception? innerException = null)
    : Exception(message, innerException);
