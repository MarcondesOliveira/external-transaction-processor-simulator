using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExternalProcessor.Simulator;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.ContractTests;

public sealed class ContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [Fact]
    public void RequestContractDeserializesExpectedShape()
    {
        const string json = """
            {
              "transactionId": "11111111-2222-4333-8444-555555555555",
              "transactionType": "TRANSFER",
              "amount": { "value": "250.75", "currency": "BRL" },
              "destination": { "accountId": "portfolio-account-001" },
              "callbackUrl": "https://callback.example.test/transactions",
              "externalReference": "manual-test-0001"
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitTransactionRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("250.75", request.Amount.Value);
        Assert.Equal("portfolio-account-001", request.Destination.AccountId);
    }

    [Fact]
    public void RequestContractRejectsUnknownFields()
    {
        const string json = """
            {
              "transactionId": "11111111-2222-4333-8444-555555555555",
              "transactionType": "TRANSFER",
              "amount": { "value": "250.75", "currency": "BRL" },
              "destination": { "accountId": "portfolio-account-001" },
              "callbackUrl": "https://callback.example.test/transactions",
              "correlationId": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"
            }
            """;

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitTransactionRequest>(json, Options));
    }

    [Fact]
    public void CallbackContractUsesCamelCase()
    {
        var callback = new TransactionProcessed(
            1,
            "TransactionProcessed",
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-10-28T12:00:00Z", CultureInfo.InvariantCulture),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUCCEEDED",
            new ProcessingResult("APPROVED", "Transaction processed successfully"));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(callback, Options));

        Assert.Equal("TransactionProcessed", document.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("SUCCEEDED", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.TryGetProperty("EventType", out _));
    }

    [Fact]
    public void StreamContractMapsProcessorOperation()
    {
        const string json = """
            {
              "idempotencyKey": { "S": "request-key-0001" },
              "requestHash": { "S": "hash" },
              "processorTransactionId": { "S": "11111111-2222-4333-8444-555555555555" },
              "callbackEventId": { "S": "22222222-3333-4444-8555-666666666666" },
              "transactionId": { "S": "33333333-4444-4555-8666-777777777777" },
              "correlationId": { "S": "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee" },
              "transactionType": { "S": "TRANSFER" },
              "amountValue": { "S": "250.75" },
              "amountCurrency": { "S": "BRL" },
              "destinationAccountId": { "S": "reject-account-001" },
              "callbackUrl": { "S": "https://callback.example.test/transactions" },
              "outcome": { "S": "REJECTED" },
              "acceptedAt": { "S": "2026-10-28T12:00:00.0000000+00:00" }
            }
            """;
        using var document = JsonDocument.Parse(json);

        var operation = WorkerFunction.FromStreamImage(document.RootElement);

        Assert.Equal(SimulatedOutcome.Rejected, operation.Outcome);
        Assert.Equal("request-key-0001", operation.IdempotencyKey);
    }
}
