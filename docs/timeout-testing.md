# Manual HTTP timeout test

The `timeout-` scenario is available in the updated source and local tests. It requires publishing and deploying that version before it can be exercised through the AWS endpoints. Do not test against an older deployment: it would treat `timeout-` as an ordinary success destination.

## Main API test after deployment

Use Bruno to create a **new transaction through the main Transaction API**. Do not post this creation body to the callback endpoint or the simulator.

```text
POST https://8st0dstrp8.execute-api.us-east-1.amazonaws.com/transactions
Content-Type: application/json
Idempotency-Key: e2e-timeout-0001
X-Correlation-Id: ffffffff-aaaa-4bbb-8ccc-dddddddddddd
```

```json
{
  "transactionType": "TRANSFER",
  "amount": { "value": "210.50", "currency": "BRL" },
  "destination": { "accountId": "timeout-portfolio-001" },
  "externalReference": "e2e-timeout-0001"
}
```

Expected:

1. The main API immediately returns `202` / `PENDING`, with a transaction ID. The timeout occurs later in Request Handler → simulator, not on this initial POST.
2. The Request Handler attempts submission and hits its ten-second HTTP client timeout. Inspect its exception chain and execution duration to distinguish this from the earlier `.invalid` DNS failure.
3. The simulator logs the injected submission delay ending without acceptance if the caller's disconnect is not propagated as cancellation; a waiting caller gets `503 SIMULATED_TIMEOUT` after twelve seconds. Cancellation may end the request earlier, still without persistence.
4. With the current queue settings, the Request Handler makes three failed attempts at approximately 90-second visibility intervals. The same original message eventually moves to Failed Requests.
5. A GET for the main transaction continues to return `PENDING`; no processor transaction ID or final result should be present.
6. A consistent read for simulator key `IDEMPOTENCY#transaction:{transactionId}:submit:v1` should find no item. No callback should exist for that operation.

Do not submit the initial POST again with a new key while waiting: that creates a separate demonstration transaction. Allow the queue's configured retries to run. Preserve the DLQ message for diagnosis.

## Optional direct simulator test

Use the simulator's `/v1/transactions` endpoint, the request shape in the README, a new key/transaction ID and destination `timeout-portfolio-001`. Supply both required custom headers. Configure the API client's response timeout above twelve seconds to see `503 SIMULATED_TIMEOUT`; configure it to ten seconds to observe a local client timeout instead.

The direct test does not exercise the main SQS retry/DLQ pipeline. Bruno is preferred because the current Swagger UI does not expose the two required custom-header controls.

## Meaning and limits

This fault injection guarantees no acceptance or side effect before the deliberate delay. It validates the deployed HTTP client's timeout path, retry classification and queue redrive. It does not simulate a provider accepting a transaction and then losing the acknowledgement; external idempotency is still required for that different failure mode.
