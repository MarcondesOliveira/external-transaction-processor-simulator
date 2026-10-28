using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

public sealed class DynamoDbProcessorOperationRepository(
    IAmazonDynamoDB dynamoDb,
    DynamoDbOptions options)
    : IProcessorOperationRepository
{
    public async Task<CreateOperationResult> CreateOrGetAsync(
        ProcessorOperation operation,
        CancellationToken cancellationToken)
    {
        var existing = await ReadAsync(operation.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Compare(existing, operation.RequestHash);
        }

        try
        {
            await dynamoDb.PutItemAsync(
                new PutItemRequest
                {
                    TableName = options.TableName,
                    Item = ToItem(operation),
                    ConditionExpression = "attribute_not_exists(PK)"
                },
                cancellationToken);

            return new CreateOperationResult(CreateOperationResultKind.Created, operation);
        }
        catch (ConditionalCheckFailedException)
        {
            var concurrent = await ReadAsync(operation.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The processor operation was created concurrently but could not be read.");
            return Compare(concurrent, operation.RequestHash);
        }
    }

    private async Task<ProcessorOperation?> ReadAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.TableName,
                Key = Key(idempotencyKey),
                ConsistentRead = true
            },
            cancellationToken);

        return response.Item is null || response.Item.Count == 0
            ? null
            : FromItem(response.Item);
    }

    private static CreateOperationResult Compare(
        ProcessorOperation existing,
        string requestHash) =>
        string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
            ? new CreateOperationResult(CreateOperationResultKind.Existing, existing)
            : new CreateOperationResult(CreateOperationResultKind.Conflict);

    private Dictionary<string, AttributeValue> ToItem(ProcessorOperation operation)
    {
        var expiresAt = operation.AcceptedAt.Add(options.Retention).ToUnixTimeSeconds();
        var item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
        {
            ["PK"] = Text(KeyValue(operation.IdempotencyKey)),
            ["SK"] = Text(KeyValue(operation.IdempotencyKey)),
            ["entityType"] = Text("PROCESSOR_OPERATION"),
            ["idempotencyKey"] = Text(operation.IdempotencyKey),
            ["requestHash"] = Text(operation.RequestHash),
            ["processorTransactionId"] = Text(operation.ProcessorTransactionId.ToString()),
            ["callbackEventId"] = Text(operation.CallbackEventId.ToString()),
            ["transactionId"] = Text(operation.TransactionId.ToString()),
            ["correlationId"] = Text(operation.CorrelationId.ToString()),
            ["transactionType"] = Text(operation.TransactionType),
            ["amountValue"] = Text(operation.AmountValue),
            ["amountCurrency"] = Text(operation.AmountCurrency),
            ["destinationAccountId"] = Text(operation.DestinationAccountId),
            ["callbackUrl"] = Text(operation.CallbackUrl.AbsoluteUri),
            ["outcome"] = Text(operation.Outcome.ToString().ToUpperInvariant()),
            ["status"] = Text("ACCEPTED"),
            ["acceptedAt"] = Text(operation.AcceptedAt.ToString("O", CultureInfo.InvariantCulture)),
            ["expiresAt"] = Number(expiresAt)
        };

        if (operation.ExternalReference is not null)
        {
            item["externalReference"] = Text(operation.ExternalReference);
        }

        return item;
    }

    public static ProcessorOperation FromItem(IReadOnlyDictionary<string, AttributeValue> item) =>
        new(
            Required(item, "idempotencyKey"),
            Required(item, "requestHash"),
            Guid.Parse(Required(item, "processorTransactionId")),
            Guid.Parse(Required(item, "callbackEventId")),
            Guid.Parse(Required(item, "transactionId")),
            Guid.Parse(Required(item, "correlationId")),
            Required(item, "transactionType"),
            Required(item, "amountValue"),
            Required(item, "amountCurrency"),
            Required(item, "destinationAccountId"),
            new Uri(Required(item, "callbackUrl"), UriKind.Absolute),
            Optional(item, "externalReference"),
            Enum.Parse<SimulatedOutcome>(Required(item, "outcome"), true),
            DateTimeOffset.Parse(
                Required(item, "acceptedAt"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    private static Dictionary<string, AttributeValue> Key(string idempotencyKey) =>
        new(StringComparer.Ordinal)
        {
            ["PK"] = Text(KeyValue(idempotencyKey)),
            ["SK"] = Text(KeyValue(idempotencyKey))
        };

    private static string KeyValue(string idempotencyKey) => $"IDEMPOTENCY#{idempotencyKey}";
    private static AttributeValue Text(string value) => new() { S = value };
    private static AttributeValue Number(long value) =>
        new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static string Required(
        IReadOnlyDictionary<string, AttributeValue> item,
        string name) =>
        item.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value.S)
            ? value.S
            : throw new InvalidOperationException($"DynamoDB attribute '{name}' is required.");

    private static string? Optional(
        IReadOnlyDictionary<string, AttributeValue> item,
        string name) =>
        item.TryGetValue(name, out var value) ? value.S : null;
}
