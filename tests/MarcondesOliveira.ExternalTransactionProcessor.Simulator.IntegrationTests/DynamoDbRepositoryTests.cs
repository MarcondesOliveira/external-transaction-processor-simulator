using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;
using MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.IntegrationTests;

public sealed class DynamoDbRepositoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingItemAllowsCreationAndPreservesIdempotency(bool nullItem)
    {
        using var client = new StubDynamoDbClient(nullItem);
        var repository = new DynamoDbProcessorOperationRepository(
            client,
            new DynamoDbOptions("simulator-operations-test", TimeSpan.FromHours(24)));
        var operation = new ProcessorOperation(
            "simulator-regression-0001",
            "original-request-hash",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TRANSFER",
            "250.75",
            "BRL",
            "portfolio-account-001",
            new Uri("https://callback.invalid/v1/callbacks/transactions"),
            "regression-test",
            SimulatedOutcome.Succeeded,
            new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        var created = await repository.CreateOrGetAsync(operation, CancellationToken.None);

        Assert.Equal(CreateOperationResultKind.Created, created.Kind);
        Assert.Equal(operation, created.Operation);
        Assert.NotNull(client.LastPutRequest);
        Assert.Equal("simulator-operations-test", client.LastPutRequest.TableName);
        Assert.Equal("attribute_not_exists(PK)", client.LastPutRequest.ConditionExpression);

        var replay = await repository.CreateOrGetAsync(
            operation with { ProcessorTransactionId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(CreateOperationResultKind.Existing, replay.Kind);
        Assert.Equal(operation, replay.Operation);

        var conflict = await repository.CreateOrGetAsync(
            operation with { RequestHash = "changed-request-hash" },
            CancellationToken.None);

        Assert.Equal(CreateOperationResultKind.Conflict, conflict.Kind);
        Assert.Equal(1, client.PutCount);
    }

    private sealed class StubDynamoDbClient(bool nullItem)
        : AmazonDynamoDBClient(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutItemRequest? LastPutRequest { get; private set; }

        public int PutCount { get; private set; }

        public override Task<GetItemResponse> GetItemAsync(
            GetItemRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetItemResponse
            {
                Item = LastPutRequest?.Item ?? (nullItem
                    ? null
                    : new Dictionary<string, AttributeValue>(StringComparer.Ordinal))
            });

        public override Task<PutItemResponse> PutItemAsync(
            PutItemRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPutRequest = request;
            PutCount++;
            return Task.FromResult(new PutItemResponse());
        }
    }
}
