# External Transaction Processor Simulator

Independent .NET 10 simulator for the external processor in the Async Transaction Processing portfolio project.

The API accepts a transaction, persists an idempotent operation in its own DynamoDB table and returns `202 Accepted`. A separate Lambda invocation consumes the table's DynamoDB Stream and sends the final result to the supplied HTTPS callback URL.

The simulator owns its storage and does not read or write the main application's transaction table, SQS queues or SNS topic. This models an external integration accessed only through HTTP contracts.

## Responsibilities

- `POST /v1/transactions` with request validation and idempotency.
- Predictable success, rejection and failure scenarios.
- A deterministic pre-acceptance HTTP timeout scenario for resilience testing.
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

The serial build configuration avoids the build-server instability encountered during development. The current suite contains 13 unit tests, 13 integration tests and 4 contract tests (30 total). Repository regression tests exercise DynamoDB responses with both null and empty items, creation, idempotent replay and conflicting requests through a stub AWS client without network access. Timeout tests use a controlled `TimeProvider` to verify the full configured delay without waiting in real time, cancellation, validation before delay and zero persistence calls. Logging tests cover correlated submissions, replay, validation/conflict, delayed timeout, infrastructure failure, worker delivery/failure, malformed Stream images and ignored non-INSERT records.

## Continuous integration

The `CI` workflow runs on pull requests targeting `main`, pushes to `main`, and
manual dispatch. It restores dependencies, verifies formatting, builds Release
with the configured .NET analyzers/warnings as errors, runs unit/integration/contract
tests and audits direct/transitive NuGet dependencies. Reported vulnerabilities
fail the job; the JSON report is printed in the job log for investigation.

The workflow follows the validated application CI pattern: actions pinned to
commit SHAs, read-only repository permissions and no persisted checkout token.
The validation job requests no AWS credentials or OIDC token, publishes no deployment package
and does not modify AWS. Tests use controlled substitutes, including controlled
time for timeout scenarios. No live callback destination or manual Bruno/Swagger
test is needed for this CI-only change. Branch protection is separate and is not
enabled by adding the workflow.

### Versioned Lambda artifact

After validation succeeds on `main` (push or manual dispatch), a separate job
publishes the Host in Release for `linux-arm64`, framework-dependent, without
ReadyToRun or an apphost. Pull requests do not generate deployment artifacts.
GitHub Actions retains the bundle for seven days:

- `external-transaction-processor-simulator.zip`: one Lambda package shared by API and worker;
- `external-transaction-processor-simulator.zip.sha256`: SHA-256 verification file;
- `build-manifest.json`: full source commit, SDK, runtime/architecture, hexadecimal
  and Base64 ZIP hashes, and future S3 key `<service>/<commit>/<service>.zip`.

The artifact name includes commit, run ID and attempt without overwriting earlier
bundles. Build identity is traceable; byte-for-byte reproducibility is not claimed.
The Base64 `sourceCodeHash` refers to the inner Lambda ZIP, not GitHub's outer
download archive/digest. Extract the downloaded Actions bundle and run
`sha256sum --check external-transaction-processor-simulator.zip.sha256` inside it.

`.github/scripts/package.sh` validates the checkout SHA, refuses an existing output
directory and checks `ExternalProcessor.Simulator.dll`, `.deps.json` and
`.runtimeconfig.json` before zipping. Both existing Lambda handlers remain unchanged;
their entry points are in the same assembly and use the same deployment ZIP.
The package job requests no AWS credentials/OIDC and performs no S3 upload or
deployment. No new Bruno/Swagger test is required until a reviewed deployment.

### Manual S3 publication through OIDC

`Publish Simulator artifact` (`.github/workflows/publish.yml`) is dispatch-only and
restricted to `main`. It publishes an existing successful main CI run's bundle;
it does not rebuild source, run Terraform or change the Lambda.

Repository variables hold deployment metadata outside source code:
`AWS_REGION`, `AWS_ACCOUNT_ID`, `ARTIFACT_BUCKET` and `AWS_ARTIFACT_ROLE_ARN`.
They are not AWS access keys. The existing IAM trust must match this repository's
verified immutable OIDC subject, audience STS and `main` branch. No environment
name is configured on this job because that would change its subject format.

1. Wait for the chosen `CI` run on `main` to succeed, including packaging.
2. Open **Actions → Publish Simulator artifact → Run workflow**, select `main`, supply
   the numeric `ci_run_id` and type `PUBLISH` in `confirmation`.
3. The job checks the run's workflow path, repository, event, branch and conclusion,
   downloads its exact commit/run/attempt artifact, and verifies the fixed package
   name, target, ZIP layout, manifest and both checksum representations.
4. Only then it assumes the existing artifact-publisher role via OIDC, checks the
   AWS account and uploads the ZIP plus manifest/checksum to the commit prefix.
   Conditional `If-None-Match: *` writes refuse existing keys. The final ZIP
   checksum, encryption and source-commit metadata are checked against S3.

This job alone receives `id-token: write`; CI jobs remain without AWS credentials.
It requests a 15-minute role session, masks deployment identifiers in logs and
uses actions pinned by SHA. The role remains shared with other application
repositories: per-repository IAM isolation is not introduced here. Conditional
writes protect this workflow, not every possible client of the bucket.

An existing key produces a failure rather than replacement, including on rerun.
The three object writes are not atomic: a partial failure can leave an uploaded
ZIP without its companion files. Inspect the error, object/version and checksums
with the owner before any retry or cleanup; there is no automatic delete/rollback.
An expired GitHub artifact cannot be published with this workflow. Do not rebuild
the same commit and overwrite its existing S3 package to work around either case.

After a successful upload, inspect the workflow and S3 evidence together. A manual
Bruno/Swagger transaction adds no publication coverage because the running Lambda
has not changed; end-to-end testing belongs to the later reviewed deploy.

References: [AWS OIDC action](https://github.com/aws-actions/configure-aws-credentials),
[S3 conditional writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html).

The published ZIP is shared by the simulator API and worker. Publication alone
updates neither function; both keep their existing deployed version until a
separate reviewed Terraform deployment.

## Configuration

| Variable | Used by | Default / requirement |
| --- | --- | --- |
| `PROCESSOR_OPERATIONS_TABLE` | API | Required DynamoDB table name |
| `PROCESSOR_OPERATION_RETENTION_HOURS` | API | `24` |
| `PROCESSING_DELAY_SECONDS` | Worker | `2` |
| `AWS_REGION` | AWS SDK | Configure for the target AWS region |

The table must have string partition and sort keys named `PK` and `SK`, TTL enabled on `expiresAt`, and a Stream containing `NEW_IMAGE`. The API needs `GetItem` and `PutItem` permissions on this table. Infrastructure provisioning belongs to the Terraform repository.

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
| `timeout-` | No callback; operation is not accepted | Delayed HTTP `503 SIMULATED_TIMEOUT` |
| Any other value | `SUCCEEDED` | `APPROVED` |

Prefix matching is case-insensitive. Use a new transaction ID and idempotency key for each independent scenario. `fail-` represents a final business-processing failure; it does not simulate an HTTP outage or timeout.

### Pre-acceptance timeout

For a valid request whose destination starts with `timeout-`, submission waits **12 seconds before any persistence**. The main Request Handler's HTTP client has a ten-second timeout, so it is expected to time out first and fail its SQS record for retry. A client that waits long enough instead receives HTTP `503` with code `SIMULATED_TIMEOUT`. This delayed response is deliberate test behavior, not an accepted transaction or a final processing result.

The timeout branch runs after request/header validation and before generating or reserving an operation. It never writes an operation or idempotency item, produces no Stream insertion and triggers no callback. Cancelling the delay also performs no persistence. Retries of the same reserved destination reproduce the delay rather than finding an accepted operation. This is a demonstration-only fault injection selected by the caller-controlled destination prefix; it is not the usual ambiguous external timeout after a provider may already have accepted a request.

The twelve-second delay fits within the API Lambda's current 30-second timeout and does not change the callback worker's processing-delay setting. See [manual timeout testing](docs/timeout-testing.md).

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

## Operational logs

The HTTP API emits ASP.NET JSON console logs. `SimulatorSubmissionHandled`
records transaction ID, current `RequestCorrelationId`, processor ID when known
and HTTP status (202, 400, 409 or the controlled delayed 503). This event does
not distinguish a new acceptance from an idempotent replay; both return the
same processor operation. A replay's request correlation may differ from the
original operation correlation used later by the worker. The existing simulated
timeout warning is retained as an additional diagnostic event.

`SimulatorSubmissionFailed` records unexpected submission failures with original
exception/root-cause types and stack trace. A fixed-message exception without an
inner exception then preserves server failure without passing provider messages
to automatic runtime logs. In the Development HTTP test host, middleware converts
that exception to HTTP 500; it is not thrown through `HttpClient` to the test.

The worker uses native `ILambdaLogger` events:

- `SimulatorCallbackStarted`: an operation has been decoded and delivery is starting.
- `SimulatorCallbackDelivered`: the callback endpoint acknowledged delivery with
  a successful HTTP status. This does not confirm that the main application's
  asynchronous Callback Handler has already applied the result.
- `SimulatorCallbackFailed`: decoding or delivery failed. Available identifiers,
  exception/root-cause types and stack trace are logged, then a fixed-message
  exception without an inner exception still fails the invocation for the existing
  Stream retry/failure-destination policy. A batch is not partially acknowledged.

Worker events include `StreamEventId`, callback `EventId`, `TransactionId`,
`CorrelationId` and `ProcessorTransactionId` when decoding succeeds. Malformed
images remain traceable by Stream event ID when available. The custom logs omit
payloads, amount, account, callback URL, external reference and idempotency key.
The failure records deliberately omit original exception messages; use the
correlated types and stack trace for diagnosis.

Terraform must keep the HTTP API Lambda's runtime logging format `Text` because
ASP.NET already emits JSON (`State` properties), while enabling native `JSON`
logging on the worker for searchable top-level parameters. These Stage 12 changes
require a reviewed deployment before appearing in AWS.

After deployment, manually create a transaction through the main API using Bruno
or Swagger, then inspect the simulator API and worker log groups for the same
transaction ID. Confirm submission 202, callback start/delivery, and the final
result through the main API GET. Failure, timeout and duplicate scenarios are
separate checks; a successful transaction does not validate their runtime logs.

## Current deployment status and limits

The simulator baseline and `timeout-` branch are deployed and have been validated against AWS for persistence, duplicate/conflicting submissions, callback retries, successful/rejected/failed results through the main callback pipeline, and real Request Handler HTTP timeout with retry exhaustion/DLQ. The Stage 12 log changes described above still require their own deployment and manual validation. No resources are provisioned by cloning, building or running the tests.

The main callback pipeline is deployed. HTTPS is used without HMAC in the current agreed diagram and implementation. Deployment and end-to-end evidence are maintained in the infrastructure repository.

This is a controlled demonstration service. The current implementation accepts a caller-supplied HTTPS callback URL and does not implement public-client authentication or destination allowlisting. Deployment and manual tests must use controlled callers and callback destinations.
