# Review findings

Open findings from the `spec-exec` reviewer pass. M0 remains unreviewed until
each item below is fixed and the milestone is re-checked.

## M0 — Solution scaffold and engineering gates

### 1. The host smoke test does not prove graceful cancellation completes

- **Location:** `tests/McpOAuthDcrBridge.IntegrationTests/HostLifecycleTests.cs`
- **Problem:** The test subscribes to `ApplicationStopping`, calls
  `WebApplicationFactory.Dispose()`, and only then awaits the stopping signal.
  `ApplicationStopping` is raised when shutdown begins, before hosted services
  and the host have necessarily finished stopping. The five-second bound also
  cannot protect against `Dispose()` itself hanging because the timeout is
  applied afterward. Consequently, the test proves startup and shutdown
  initiation, but not the M0-required graceful process-cancellation path and
  bounded completion.
- **Guidance:** Exercise host cancellation explicitly, observe
  `ApplicationStopped` (or await the host run task), and place the complete
  cancellation-to-stopped sequence under a bounded timeout. Retain the request
  assertion that proves the host started and exposes no product endpoint.

### 2. The coverage collector is not the current stable version selected at scaffolding

- **Location:** `Directory.Packages.props`
- **Problem:** The centrally pinned `coverlet.collector` version is `6.0.4`.
  `dotnet list McpOAuthDcrBridge.sln package --outdated --include-transitive`
  reports `10.0.1` as the latest stable version, published before the M0
  scaffold was committed. This conflicts with the dependency commitment in
  `SPEC.md` that the current stable coverage collector be selected at
  scaffolding.
- **Guidance:** Update the central pin to the current compatible stable version,
  keep the collector private to the test projects, and rerun restore, Release
  build, format verification, the repository-level coverage test command, and
  the vulnerability audit from a clean archive.
