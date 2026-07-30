# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M8 — YARP MCP reverse proxy and configured headers

### M8-1 (required-test gap): no absolute-form request-target SSRF test

**Status:** resolved (verified 2026-07-30 — raw-socket absolute-form test added;
the foreign authority can never be selected and the behavior is pinned).

### M8-2 (required-test gap): the "no-buffer" half of the no-buffer/no-retry tests is missing

**Status:** resolved (verified 2026-07-30 — first flushed chunk is proven to
reach the client while the upstream handler is still blocked; a buffering
proxy now times out this test).

### M8-3 (minor test-strength gap): no test applies multiple distinct configured headers together

**Status:** resolved (verified 2026-07-30 — two distinct configured headers are
proven applied in one forwarded request, one of them replacing a downstream
spoof).

### M8-4 (DRY violation): the bridge challenge value is constructed independently in two places

**Status:** resolved (verified 2026-07-30 — `BridgeOptions.ProtectedResourceMetadataUri`
plus the single `BearerChallenge.Build` helper are now the one authoritative
definition, used by both the middleware and the response transform).

### M8-5 (updated, second pass): challenge round-trip is still lossy — parsed quotes are not re-escaped on emission

**Status:** open

**Where.** `src/McpOAuthDcrBridge/Mcp/BearerChallenge.cs` (`Build`), with
round-trip evidence in
`tests/McpOAuthDcrBridge.UnitTests/Mcp/BearerChallengeParametersTests.cs` and
`tests/McpOAuthDcrBridge.ContractTests/McpProxyContractTests.cs`.

**Remaining problem (current evidence).** The first-pass fix made
`BearerChallengeParameters.Parse` quote-aware and unescape `\"`, exactly as the
original guidance asked — the comma case is now preserved end to end. But the
original guidance addressed only the parse side, and the symmetric emission
side is now inconsistent: `BearerChallenge.Build` interpolates each preserved
value into `{name}="{value}"` **without re-escaping embedded quotes**. An
upstream challenge of

```
WWW-Authenticate: Bearer error_description="say \"hi\" please"
```

is parsed to the raw value `say "hi" please` and re-emitted as

```
error_description="say "hi" please"
```

— a malformed quoted-string that downstream parsers will truncate at the first
embedded quote. Before the first-pass fix the round trip was accidentally more
faithful (quotes were never unescaped); after it, unescape-without-re-escape
makes emission lossy for exactly the values the parser now handles correctly.

**Why the first attempt did not fully resolve it.** The attempted fix followed
the previous guidance to the letter, and the previous guidance was incomplete:
it required quote-aware parsing and unit tests for `Parse`, but never required
the inverse property — that whatever `Parse` produces, `Build` must emit as a
valid RFC 7235 quoted-string again. This updated finding closes that gap with
explicit round-trip requirements.

**Guidance (ordered, verifiable).**

1. In `BearerChallenge.Build`, escape each preserved value before
   interpolation: backslashes first, then quotes
   (`value.Replace("\\", "\\\\").Replace("\"", "\\\"")`), so every emitted
   parameter is a valid quoted-string. `resource_metadata` comes from a
   validated URI and needs no escaping, but running it through the same helper
   is fine and simpler.
2. Add a unit test asserting `Build` emits
   `error_description="say \"hi\" please"` (escaped form) when given the raw
   value `say "hi" please`.
3. Add the round-trip property where it is cheapest — a unit test that runs
   `BearerChallengeParameters.Parse(BearerChallenge.Build(options, parameters))`
   for values containing commas, quotes, and backslashes, and asserts the
   parsed dictionary equals the input parameters.
4. Extend the existing comma-preservation contract test (or add one case) with
   an upstream `error_description` containing an escaped quote, asserting the
   bridge's rewritten challenge carries the correctly re-escaped form.
5. Result that demonstrates resolution: the round-trip unit test in step 3
   fails on today's code and passes after step 1, and the contract suite
   proves an embedded-quote description survives the rewrite intact.
