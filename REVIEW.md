# REVIEW — open findings

> Reviewer-owned. The coder fixes the code these findings identify but never
> edits statuses, resolves findings, or deletes this file.

## M10 — Security and operational hardening (also gates M11's release audit)

### M10-1 (required-gate gap): container image vulnerability scanning is neither documented nor performed

**Status:** open

**Where.**
- `docs/security.md` § "Dependency and container vulnerability management"
  (dangling cross-reference)
- `docs/deployment.md` (missing scan documentation)
- `scripts/container-smoke-test.sh` (single operator entry point without a
  scan step)

**Problem.** M10's acceptance criteria require "Dependency and **container**
vulnerability checks run in the documented quality gate and have no unresolved
high or critical findings", and its required tests list "Dependency and
container vulnerability scans". The dependency half is done and verified
(NuGet audit at high severity wired into warnings-as-errors; `dotnet list
package --vulnerable --include-transitive` re-run clean during this review).
The container half is absent twice over:

1. `docs/security.md` defers container scanning to the deployment
   documentation ("…is described there rather than here"), but
   `docs/deployment.md` contains no scanning guidance at all — the
   cross-reference dangles, and no documented gate anywhere includes an image
   scan command.
2. No scan was ever executed: the operator ran
   `scripts/container-smoke-test.sh` (confirmed passing), but that script
   builds and probes the image without scanning it, so M11's "clean release
   audit" currently rests on an image whose OS/runtime layers were never
   checked for known CVEs.

**Guidance.**

1. Document the scan in `docs/deployment.md` (new "Image vulnerability
   scanning" section): the exact command (e.g.
   `docker scout cves --only-severity critical,high mcp-oauth-dcr-bridge:local`
   or `trivy image --severity HIGH,CRITICAL --exit-code 1 <tag>`), when it
   runs (image build/release, plus on base-image updates since
   `aspnet:10.0` is a floating tag), and that an unresolved high or critical
   finding blocks release. Fix the `docs/security.md` cross-reference so it
   points at the real section, and add the command to the release gate list in
   `docs/testing.md` beside the NuGet audit.
2. Add the scan to `scripts/container-smoke-test.sh` as a final step so the
   one documented operator command covers it: run the scanner if available,
   fail the script on high/critical findings, and print an explicit SKIP
   warning when no scanner is installed (so absence is visible, never
   silent).
3. Execution (operator, Docker-capable machine): re-run
   `bash scripts/container-smoke-test.sh` and report the scan outcome so the
   review can record it; zero unresolved high/critical findings closes this,
   otherwise the findings come back here for triage.
4. Result that demonstrates resolution: the documented quality gate names the
   container scan with a concrete failing command, the smoke script executes
   or visibly skips it, `docs/security.md` no longer references a section
   that does not exist, and one scan run against the current image is
   recorded with no unresolved high or critical findings.
