# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M9 — Streaming, session, cancellation, and failure semantics

### M9-1 (test-strength gap): the 100-stream test proves isolation but not concurrency

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/McpStreamingContractTests.cs`,
`AtLeast100ConcurrentActiveStreamsRemainIsolatedPerSession`.

**Problem.** The acceptance criterion is "at least 100 **concurrent active**
streams operate without cross-session leakage". The test fires 120 requests via
`Task.WhenAll`, each upstream handler delays 50 ms and completes, and the whole
batch must finish within 30 seconds. That proves per-session isolation and
bounded total time — but not simultaneity: a bridge that processed requests
**one at a time** would take ~120 × 50 ms ≈ 6 s and still pass every assertion,
including the 30-second bound. Nothing in the test fails unless fewer than the
target number of streams can actually be open at the same instant.

**Guidance.**

1. Make the fake upstream a rendezvous barrier: each handler atomically
   increments a counter (e.g. `Interlocked.Increment`), writes and flushes a
   first chunk, then blocks on a shared `TaskCompletionSource` that is
   completed only when the counter reaches 120. Give the rendezvous a bounded
   `WaitAsync` (e.g. 30 s) so a failure diagnoses cleanly instead of hanging.
2. In the test, open all 120 requests with
   `HttpCompletionOption.ResponseHeadersRead`, read each stream's first chunk
   (proving 120 responses are simultaneously started end to end through the
   proxy), then release the barrier and read each remaining body, asserting
   per-session content isolation as today.
3. Result that demonstrates resolution: a bridge (or proxy configuration) that
   serializes or caps concurrent MCP streams below the target now deadlocks
   the rendezvous and fails the test by timeout, instead of passing on
   sequential throughput.

### M9-2 (required-test gap): no invalid-upstream-response test

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/McpStreamingContractTests.cs`
(new fake needed alongside `FakeUpstreamMcpServer`).

**Problem.** M9's required tests list "Client cancellation, upstream
cancellation, abrupt disconnect, partial body, **invalid response**, and
shutdown-drain tests". Cancellation, disconnect, partial body, and drain are
covered, but no test exercises an upstream that responds with something that is
not valid HTTP at all (garbage status line, malformed framing). The
Kestrel-based `FakeUpstreamMcpServer` cannot produce such output — it always
emits well-formed HTTP — which is presumably why the cell was skipped. Nothing
currently pins that a protocol-invalid upstream reply maps to the documented
bounded gateway error rather than hanging the client, leaking the raw garbage
bytes downstream, or being retried.

**Guidance.**

1. Add a minimal raw fake (one new test-support type, e.g.
   `RawTcpUpstreamServer` beside the existing fakes): a `TcpListener` that
   accepts one connection, reads the request head, writes a configurable raw
   byte payload (default something like `NOT-HTTP/9.9 garbage\r\n\r\n`), counts
   accepted connections, and closes.
2. Add a contract test pointing the bridge's MCP upstream at it, sending an
   authenticated `GET /mcp`, and asserting: the response arrives within a
   bounded window (`WaitAsync`); the status is `502` (or the documented
   `502`/`503`/`504` family); the body never contains the raw upstream bytes;
   and the fake accepted exactly one connection (no retry).
3. Result that demonstrates resolution: the failure matrix covers a
   protocol-invalid upstream, so a future change that lets YARP surface a
   hung connection or relay malformed bytes fails the suite.
