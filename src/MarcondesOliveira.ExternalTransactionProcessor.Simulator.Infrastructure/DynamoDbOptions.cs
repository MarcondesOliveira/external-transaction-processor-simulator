namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure;

public sealed record DynamoDbOptions(string TableName, TimeSpan Retention)
{
    public static DynamoDbOptions FromEnvironment()
    {
        var tableName = Environment.GetEnvironmentVariable("PROCESSOR_OPERATIONS_TABLE");
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidOperationException(
                "PROCESSOR_OPERATIONS_TABLE environment variable is required.");
        }

        var retentionHours = int.TryParse(
            Environment.GetEnvironmentVariable("PROCESSOR_OPERATION_RETENTION_HOURS"),
            out var configuredHours)
            ? configuredHours
            : 24;

        return new DynamoDbOptions(tableName, TimeSpan.FromHours(retentionHours));
    }
}
