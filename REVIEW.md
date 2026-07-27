# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M6 — Transparent token and refresh forwarding

### M6-1 (required-test gap): no malformed-form negative test for `POST /token`

**Status:** open

**Where.** `tests/McpOAuthDcrBridge.ContractTests/TokenContractTests.cs`.

**Problem.** M6's required tests include "negative tests for … **malformed
forms**, and body limits". The suite covers wrong content type, duplicated
parameters, smuggled credentials, oversized declared bodies, and oversized
chunked bodies — but no test sends a syntactically malformed form body. The
endpoint parses the raw body with `QueryHelpers.ParseQuery` over a UTF-8
decode (`TokenEndpointExtensions.TokenAsync`), and nothing proves that input
such as invalid percent-encoding (`%zz`), bare `&`/`=` runs, a key with no
value, or invalid UTF-8 bytes yields a bounded `400` (or a semantically
harmless forward) rather than an unhandled `500`, and that the malformed
content never reaches the upstream or the error body.

**Guidance.**

1. Add a theory to `TokenContractTests` posting raw
   `application/x-www-form-urlencoded` bodies with at least: invalid
   percent-encoding in a security field (e.g.
   `grant_type=refresh_token&client_id=fictional-client&refresh_token=a%zzb`),
   a body of only separators (`&&&===`), and raw invalid UTF-8 bytes (use
   `ByteArrayContent` with the form content type).
2. Assert per case: the response is a bounded JSON OAuth error or, where the
   parse is lossless and validation passes, a well-formed forward — never a
   `500`; `fakeUpstream.RequestCount` is `0` for every rejected case; and the
   response body never echoes the malformed input.
3. Result that demonstrates resolution: the contract suite pins the malformed-
   form behavior of `/token`, so a future parser change that starts throwing
   (500) or silently forwarding mangled security fields fails the suite.

## M7 — Certificate-backed private-key JWT authentication

### M7-1 (DRY violation): certificate and JWT test helpers are copy-pasted across test projects

**Status:** open

**Where.**
- `tests/McpOAuthDcrBridge.UnitTests/Configuration/TestCertificates.cs`
- `tests/McpOAuthDcrBridge.IntegrationTests/Configuration/TestCertificates.cs`
- `tests/McpOAuthDcrBridge.ContractTests/TestCertificates.cs`
- `PadBase64Url` / `Base64UrlDecode` / assertion-splitting helpers duplicated in
  `tests/McpOAuthDcrBridge.UnitTests/Token/PrivateKeyJwtAssertionGeneratorTests.cs`
  and `tests/McpOAuthDcrBridge.ContractTests/PrivateKeyJwtTokenContractTests.cs`.

**Problem.** SPEC §9 makes DRY absolute and explicitly includes "test setup …
fixtures, builders, or shared classes with one authoritative definition".
`TestCertificates` now exists three times with the same `CreateRsaPfx` /
`WriteTemporaryPfx` bodies, and the copies have already drifted: only the
UnitTests copy has `CreateEcPfx`/`CreatePublicOnlyPfx`, and the ContractTests
copy lacks the `keyUsage` parameter. The JWT base64url-decode/split/verify
helpers are likewise duplicated between the unit and contract assertion tests.
Drifted copies are precisely the failure mode the DRY commitment exists to
prevent (e.g. a fix to PFX generation applied to one copy only).

**Guidance.**

1. Create one authoritative copy of each helper as shared **linked source**
   (test projects may not be referenced by production code, and the SPEC
   repository layout defines no fourth test project, so linking is the
   conforming mechanism): place the files under `tests/` (e.g.
   `tests/Shared/TestCertificates.cs`, `tests/Shared/JwsAssertionParser.cs`)
   and include them from each test `.csproj` with
   `<Compile Include="..\Shared\TestCertificates.cs" Link="Shared\TestCertificates.cs" />`,
   or hoist the include into a `tests/Directory.Build.props`.
   Keep one top-level type per file and namespace them neutrally
   (e.g. `McpOAuthDcrBridge.TestSupport`).
2. The merged `TestCertificates` must be the superset (keyUsage parameter,
   `CreateEcPfx`, `CreatePublicOnlyPfx`, named temporary files); delete all
   three per-project copies and the duplicated base64url/split/verify helpers,
   updating the four consuming test classes.
3. Result that demonstrates resolution: exactly one definition of
   `TestCertificates` and one of the JWS parse/verify helper exists in the
   repository (`grep` finds a single `class TestCertificates`), all suites
   still pass, and the format/analyzer gate stays green.
