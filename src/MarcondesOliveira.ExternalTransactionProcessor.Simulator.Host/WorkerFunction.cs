using System.Globalization;
using System.Text.Json;
using Amazon.Lambda.Core;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

[assembly: LambdaSerializer(
    typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ExternalProcessor.Simulator;

public sealed class WorkerFunction
{
    private readonly CallbackProcessingService service;

    public WorkerFunction()
        : this(CreateService())
    {
    }

    public WorkerFunction(CallbackProcessingService service)
    {
        this.service = service;
    }

    public async Task FunctionHandler(JsonElement dynamoDbEvent, ILambdaContext context)
    {
        if (!dynamoDbEvent.TryGetProperty("Records", out var records) ||
            records.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("DynamoDB event does not contain Records.");
        }

        foreach (var record in records.EnumerateArray())
        {
            if (!record.TryGetProperty("eventName", out var eventName) ||
                !string.Equals(eventName.GetString(), "INSERT", StringComparison.Ordinal))
            {
                continue;
            }

            var operation = FromStreamImage(
                record.GetProperty("dynamodb").GetProperty("NewImage"));

            context.Logger.LogInformation(
                "Delivering callback for transaction {TransactionId} with simulated outcome {Outcome}.",
                operation.TransactionId,
                operation.Outcome);

            await service.ProcessAsync(operation, CancellationToken.None);
        }
    }

    public static ProcessorOperation FromStreamImage(JsonElement image) =>
        new(
            Required(image, "idempotencyKey"),
            Required(image, "requestHash"),
            Guid.Parse(Required(image, "processorTransactionId")),
            Guid.Parse(Required(image, "callbackEventId")),
            Guid.Parse(Required(image, "transactionId")),
            Guid.Parse(Required(image, "correlationId")),
            Required(image, "transactionType"),
            Required(image, "amountValue"),
            Required(image, "amountCurrency"),
            Required(image, "destinationAccountId"),
            new Uri(Required(image, "callbackUrl"), UriKind.Absolute),
            Optional(image, "externalReference"),
            Enum.Parse<SimulatedOutcome>(Required(image, "outcome"), true),
            DateTimeOffset.Parse(
                Required(image, "acceptedAt"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    private static CallbackProcessingService CreateService()
    {
        var delaySeconds = int.TryParse(
            Environment.GetEnvironmentVariable("PROCESSING_DELAY_SECONDS"),
            out var configuredDelay)
            ? configuredDelay
            : 2;
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        return new CallbackProcessingService(
            new CallbackHttpClient(httpClient),
            TimeProvider.System,
            TimeSpan.FromSeconds(delaySeconds));
    }

    private static string Required(JsonElement image, string name)
    {
        if (!image.TryGetProperty(name, out var attribute) ||
            !attribute.TryGetProperty("S", out var value) ||
            value.GetString() is not { Length: > 0 } text)
        {
            throw new InvalidOperationException(
                $"DynamoDB stream attribute '{name}' is required.");
        }

        return text;
    }

    private static string? Optional(JsonElement image, string name) =>
        image.TryGetProperty(name, out var attribute) &&
        attribute.TryGetProperty("S", out var value)
            ? value.GetString()
            : null;
}
