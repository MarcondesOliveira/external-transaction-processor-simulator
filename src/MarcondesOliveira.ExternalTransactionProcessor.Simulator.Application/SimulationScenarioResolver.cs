namespace MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application;

public static class SimulationScenarioResolver
{
    public static SimulatedOutcome Resolve(string destinationAccountId)
    {
        if (destinationAccountId.StartsWith("reject-", StringComparison.OrdinalIgnoreCase))
        {
            return SimulatedOutcome.Rejected;
        }

        if (destinationAccountId.StartsWith("fail-", StringComparison.OrdinalIgnoreCase))
        {
            return SimulatedOutcome.Failed;
        }

        return SimulatedOutcome.Succeeded;
    }
}
