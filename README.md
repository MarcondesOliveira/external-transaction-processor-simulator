# External Transaction Processor Simulator

Independent .NET 10 simulator for the external processor in the Async Transaction Processing portfolio project.

The API accepts a transaction, persists an idempotent operation in its own DynamoDB table and returns `202 Accepted`. A separate Lambda invocation consumes the table's DynamoDB Stream and sends the final result to the supplied HTTPS callback URL.

The simulator owns its storage and does not read or write the main application's transaction table, SQS queues or SNS topic. This models an external integration accessed only through HTTP contracts.

## Responsibilities

- `POST /v1/transactions` with request validation and idempotency.
- Predictable success, rejection and failure scenarios.
- Asynchronous callback delivery through a DynamoDB Stream worker.
- Stable processor transaction and callback event identifiers across retries.
- Swagger UI at `/swagger/index.html` and OpenAPI at `/swagger/v1/swagger.json`.
- Health endpoint at `/health`.

## Project structure

- `src/MarcondesOliveira.ExternalTransactionProcessor.Simulator.Application`: contracts, validation, scenario selection and application services.
- `src/MarcondesOliveira.ExternalTransactionProcessor.Simulator.Infrastructure`: DynamoDB persistence and HTTP callback client.
- `src/MarcondesOliveira.ExternalTransactionProcessor.Simulator.Host`: HTTP API and Stream worker entry point.
- `tests`: unit, API integration and contract tests. These tests do not require AWS credentials or deployed resources.

## Build and test

Install the .NET 10 SDK selected by `global.json`, then run from the repository root:

```sh
dotnet restore
dotnet build --no-restore --disable-build-servers -m:1
dotnet test --no-build --disable-build-servers -m:1
dotnet format --no-restore --verify-no-changes
```

The serial build configuration avoids the build-server instability encountered during development. The current suite contains 9 unit tests, 3 API integration tests and 4 contract tests.

## Configuration

| Variable | Used by | Default / requirement |
| --- | --- | --- |
| `PROCESSOR_OPERATIONS_TABLE` | API | Required DynamoDB table name |
| `PROCESSOR_OPERATION_RETENTION_HOURS` | API | `24` |
| `PROCESSING_DELAY_SECONDS` | Worker | `2` |
| `AWS_REGION` | AWS SDK | Configure for the target AWS region |

The table must have string partition and sort keys named `PK` and `SK`, TTL enabled on `expiresAt`, and a Stream containing `NEW_IMAGE`. The API needs `GetItem` and `PutItem` permissions on this table. Infrastructure provisioning belongs to the Terraform repository and is pending at this stage.

For local execution, use the AWS SDK credential chain with a scoped development profile. Do not put AWS credentials in source files or commit them.

## Run locally

```sh
dotnet run --project src/MarcondesOliveira.ExternalTransactionProcessor.Simulator.Host --launch-profile http
```

Open `http://localhost:5076/swagger/index.html` or call `GET http://localhost:5076/health`. These routes can be checked before provisioning the table. A successful POST requires the configured DynamoDB table and AWS access.

Running the HTTP host locally does not start the Stream worker. Callback execution requires a separate Lambda event-source mapping or a manual worker invocation after deployment.

## Manual API test with Bruno

Use `POST http://localhost:5076/v1/transactions` locally, or replace the base URL with the simulator's API Gateway URL after deployment.

Headers:

```text
Content-Type: application/json
Idempotency-Key: simulator-manual-0001
X-Correlation-Id: aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee
```

Body:

```json
{
  "transactionId": "11111111-2222-4333-8444-555555555555",
  "transactionType": "TRANSFER",
  "amount": {
    "value": "250.75",
    "currency": "BRL"
  },
  "destination": {
    "accountId": "portfolio-account-001"
  },
  "callbackUrl": "https://your-callback-endpoint.example/transactions",
  "externalReference": "simulator-manual-0001"
}
```

Replace `callbackUrl` with an endpoint you control before testing callback delivery. The placeholder above is illustrative. `correlationId` is sent in the header, not in the request body; unknown JSON fields are rejected.

Expected response: `202 Accepted` with `processorTransactionId`, `transactionId`, `status: ACCEPTED` and `acceptedAt`. Repeating the same key and body returns the original accepted operation without creating another Stream insertion. Changing the body while keeping the same key returns `409 IDEMPOTENCY_CONFLICT`. Missing or invalid headers return `400 VALIDATION_ERROR`.

Use Bruno for this test: the current Swagger document describes the body and responses but does not expose input controls for the two required custom headers.

## Simulation scenarios

| Destination account prefix | Callback status | Result code |
| --- | --- | --- |
| `reject-` | `REJECTED` | `DESTINATION_REJECTED` |
| `fail-` | `FAILED` | `PROCESSING_ERROR` |
| Any other value | `SUCCEEDED` | `APPROVED` |

Prefix matching is case-insensitive. Use a new transaction ID and idempotency key for each independent scenario. `fail-` represents a final business-processing failure; it does not simulate an HTTP outage or timeout.

## Callback behavior

The worker waits for the configured simulation delay and sends a `TransactionProcessed` event using HTTPS, JSON and `X-Correlation-Id`. The event contains the transaction, processor, correlation and event identifiers, final status and result code/message.

Only a successful HTTP status acknowledges delivery. A network error, timeout or non-success response raises an exception so the Lambda event-source mapping can apply its configured retry policy. Delivery can occur more than once; the callback consumer must deduplicate by event ID. The event ID is persisted with the operation, while `occurredAt` is generated for each delivery attempt.

The worker entry point is:

```text
ExternalProcessor.Simulator::ExternalProcessor.Simulator.WorkerFunction::FunctionHandler
```

The HTTP API uses the executable assembly handler `ExternalProcessor.Simulator`. Both Lambdas can use the same published package, with separate roles and configuration. Stream batch size, retry limits, failure destination and event filtering must be configured in Terraform before deployment.

## Idempotency and retention

One item stores both the accepted operation and its idempotency data. Conditional insertion prevents concurrent requests from creating duplicate operations. The request hash includes the submitted transaction fields, callback URL and optional external reference; the correlation header is not part of that hash.

TTL defaults to 24 hours and applies to the entire simulator operation. DynamoDB TTL deletion is asynchronous, so expiration does not guarantee physical deletion at exactly 24 hours. The implementation continues to recognize the item while it remains in the table. Once removed, that key may create a new operation. This retention is for temporary portfolio demonstrations, not a permanent processing ledger.

## Current deployment status and limits

The implementation builds and passes its local automated tests. The simulator has not yet been deployed, and its DynamoDB storage and Lambda Stream trigger have not yet been validated against AWS. No resources are provisioned by cloning, building or running the tests.

The final callback pipeline in the main application is planned for a later project stage. Callback delivery will be validated separately when the required endpoint exists. HTTPS is used without HMAC in the current agreed diagram and implementation.

This is a controlled demonstration service. The current implementation accepts a caller-supplied HTTPS callback URL and does not implement public-client authentication or destination allowlisting. Deployment and manual tests must use controlled callers and callback destinations.
