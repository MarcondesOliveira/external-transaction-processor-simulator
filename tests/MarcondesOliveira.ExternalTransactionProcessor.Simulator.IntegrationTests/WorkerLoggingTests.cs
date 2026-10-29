using System.Text.Json;
using Amazon.Lambda.TestUtilities;
using ExternalProcessor.Simulator;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.IntegrationTests;

public sealed class WorkerLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeliveryLogsIdentifiersAndPreservesFailureRetries(bool fail)
    {
        var client = new RecordingClient(fail);
        var worker = new WorkerFunction(new CallbackProcessingService(client, TimeProvider.System, TimeSpan.Zero));
        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext { Logger = logger };
        var input = StreamEvent();
        if (fail)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.FunctionHandler(input, context));
            Assert.Null(exception.InnerException);
            Assert.DoesNotContain("private-", exception.ToString());
        }
        else
        {
            await worker.FunctionHandler(input, context);
        }

        Assert.Equal(1, client.Calls);
        var logs = logger.Buffer.ToString();
        Assert.Contains("SimulatorCallbackStarted", logs);
        Assert.Contains(fail ? "SimulatorCallbackFailed" : "SimulatorCallbackDelivered", logs);
        Assert.DoesNotContain(fail ? "SimulatorCallbackDelivered" : "SimulatorCallbackFailed", logs);
        Assert.Contains("stream-record-001", logs);
        foreach (var field in new[] { "transactionId", "correlationId", "processorTransactionId", "callbackEventId" })
        {
            Assert.Contains(input.GetProperty("Records")[0].GetProperty("dynamodb")
                .GetProperty("NewImage").GetProperty(field).GetProperty("S").GetString()!, logs);
        }
        if (fail)
        {
            Assert.Contains(nameof(HttpRequestException), logs);
        }
        Assert.DoesNotContain("private-", logs);
        Assert.DoesNotContain("250.75", logs);
    }

    [Fact]
    public async Task MalformedImageLogsStreamIdentityAndStillFailsInvocation()
    {
        var logger = new TestLambdaLogger();
        var client = new RecordingClient(false);
        var worker = new WorkerFunction(new CallbackProcessingService(client, TimeProvider.System, TimeSpan.Zero));
        var input = JsonSerializer.SerializeToElement(new
        {
            Records = new[] { new { eventID = "malformed-record", eventName = "INSERT", dynamodb = new { NewImage = new { } } } }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.FunctionHandler(input,
            new TestLambdaContext { Logger = logger }));
        Assert.Equal(0, client.Calls);
        Assert.Contains("SimulatorCallbackFailed", logger.Buffer.ToString());
        Assert.Contains("malformed-record", logger.Buffer.ToString());
        Assert.DoesNotContain("SimulatorCallbackDelivered", logger.Buffer.ToString());
    }

    [Fact]
    public async Task NonInsertDoesNotDeliverOrLogSuccess()
    {
        var client = new RecordingClient(false);
        var logger = new TestLambdaLogger();
        var worker = new WorkerFunction(new CallbackProcessingService(client, TimeProvider.System, TimeSpan.Zero));
        await worker.FunctionHandler(JsonSerializer.SerializeToElement(new
        {
            Records = new[] { new { eventID = "modified-record", eventName = "MODIFY" } }
        }), new TestLambdaContext { Logger = logger });
        Assert.Equal(0, client.Calls);
        Assert.DoesNotContain("SimulatorCallback", logger.Buffer.ToString());
    }

    private static JsonElement StreamEvent()
    {
        var image = new Dictionary<string, object>
        {
            ["idempotencyKey"] = new { S = "private-idempotency" },
            ["requestHash"] = new { S = "private-hash" },
            ["processorTransactionId"] = new { S = Guid.NewGuid().ToString() },
            ["callbackEventId"] = new { S = Guid.NewGuid().ToString() },
            ["transactionId"] = new { S = Guid.NewGuid().ToString() },
            ["correlationId"] = new { S = Guid.NewGuid().ToString() },
            ["transactionType"] = new { S = "TRANSFER" },
            ["amountValue"] = new { S = "250.75" },
            ["amountCurrency"] = new { S = "BRL" },
            ["destinationAccountId"] = new { S = "private-account" },
            ["callbackUrl"] = new { S = "https://private-callback.invalid/transactions" },
            ["externalReference"] = new { S = "private-reference" },
            ["outcome"] = new { S = "Succeeded" },
            ["acceptedAt"] = new { S = "2026-10-29T01:00:00Z" }
        };
        return JsonSerializer.SerializeToElement(new
        {
            Records = new[] { new { eventID = "stream-record-001", eventName = "INSERT", dynamodb = new { NewImage = image } } }
        });
    }

    private sealed class RecordingClient(bool fail) : ICallbackClient
    {
        public int Calls { get; private set; }
        public Task SendAsync(Uri callbackUrl, Guid correlationId, TransactionProcessed callback,
            CancellationToken cancellationToken)
        {
            Calls++;
            return fail ? Task.FromException(new HttpRequestException("private-callback-response")) : Task.CompletedTask;
        }
    }
}
