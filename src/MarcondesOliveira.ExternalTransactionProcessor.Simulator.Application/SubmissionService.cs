using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public sealed class SubmissionService(
    IProcessorOperationRepository repository,
    TimeProvider timeProvider)
{
    public async Task<AcceptedTransactionResponse> SubmitAsync(
        SubmitTransactionRequest request,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request, idempotencyKey, correlationId);
        if (errors.Count > 0)
        {
            throw new SubmissionValidationException(errors);
        }

        var acceptedAt = timeProvider.GetUtcNow();
        var operation = new ProcessorOperation(
            idempotencyKey,
            ComputeRequestHash(request),
            Guid.NewGuid(),
            Guid.NewGuid(),
            request.TransactionId,
            correlationId,
            request.TransactionType,
            request.Amount.Value,
            request.Amount.Currency.ToUpperInvariant(),
            request.Destination.AccountId,
            request.CallbackUrl,
            request.ExternalReference,
            SimulationScenarioResolver.Resolve(request.Destination.AccountId),
            acceptedAt);

        var persistence = await repository.CreateOrGetAsync(operation, cancellationToken);
        return persistence.Kind switch
        {
            CreateOperationResultKind.Created or CreateOperationResultKind.Existing =>
                ToResponse(persistence.Operation!),
            CreateOperationResultKind.Conflict =>
                throw new SubmissionIdempotencyConflictException(),
            _ => throw new InvalidOperationException("Unknown operation persistence result.")
        };
    }

    private static List<ErrorDetail> Validate(
        SubmitTransactionRequest request,
        string idempotencyKey,
        Guid correlationId)
    {
        var errors = new List<ErrorDetail>();
        if (request.TransactionId == Guid.Empty)
        {
            errors.Add(new ErrorDetail("transactionId", "Must be a non-empty UUID"));
        }

        if (correlationId == Guid.Empty)
        {
            errors.Add(new ErrorDetail("X-Correlation-Id", "Must be a non-empty UUID"));
        }

        if (idempotencyKey.Length is < 8 or > 128 ||
            idempotencyKey.Any(character => character is < '!' or > '~'))
        {
            errors.Add(new ErrorDetail(
                "Idempotency-Key",
                "Must contain 8 to 128 printable ASCII characters without spaces"));
        }

        if (string.IsNullOrWhiteSpace(request.TransactionType))
        {
            errors.Add(new ErrorDetail("transactionType", "Is required"));
        }

        if (request.Amount is null ||
            !decimal.TryParse(
                request.Amount.Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount <= 0)
        {
            errors.Add(new ErrorDetail("amount.value", "Must be a positive decimal string"));
        }

        if (request.Amount is null || request.Amount.Currency.Length != 3)
        {
            errors.Add(new ErrorDetail("amount.currency", "Must contain three characters"));
        }

        if (request.Destination is null || string.IsNullOrWhiteSpace(request.Destination.AccountId))
        {
            errors.Add(new ErrorDetail("destination.accountId", "Is required"));
        }

        if (request.CallbackUrl is null ||
            !request.CallbackUrl.IsAbsoluteUri ||
            !string.Equals(request.CallbackUrl.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            errors.Add(new ErrorDetail("callbackUrl", "Must be an absolute HTTPS URL"));
        }

        return errors;
    }

    private static string ComputeRequestHash(SubmitTransactionRequest request)
    {
        var canonical = string.Join(
            '|',
            request.TransactionId.ToString("N"),
            request.TransactionType,
            request.Amount.Value,
            request.Amount.Currency.ToUpperInvariant(),
            request.Destination.AccountId,
            request.CallbackUrl.AbsoluteUri,
            request.ExternalReference ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static AcceptedTransactionResponse ToResponse(ProcessorOperation operation) =>
        new(
            operation.ProcessorTransactionId,
            operation.TransactionId,
            "ACCEPTED",
            operation.AcceptedAt);
}
