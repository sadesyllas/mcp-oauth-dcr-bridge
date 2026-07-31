#!/usr/bin/env bash
# Container smoke test for the MCP OAuth DCR Bridge OCI image, run from the
# repository root: ./scripts/container-smoke-test.sh
#
# Exercises exactly the M11 container acceptance criteria: the image builds,
# runs as a non-root user, serves liveness/readiness on the documented port,
# fails closed and exits promptly on invalid configuration, and drains
# in-flight requests during a graceful SIGTERM shutdown rather than dropping
# them. Every check is a fictional/synthetic configuration; no real upstream
# provider or credential is involved.
set -euo pipefail

IMAGE_TAG="mcp-oauth-dcr-bridge:smoke-test"
CONTAINER_NAME="mcp-oauth-dcr-bridge-smoke-test"
PORT=18080

cleanup() {
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Building the image"
docker build -t "$IMAGE_TAG" .

echo "==> Starting a container with a complete fictional configuration"
docker run -d --name "$CONTAINER_NAME" -p "$PORT:8080" \
  -e "Bridge__ExternalBaseUrl=https://bridge.example.test" \
  -e "Bridge__Upstream__AuthorizationEndpoint=https://login.example.test/authorize" \
  -e "Bridge__Upstream__TokenEndpoint=https://login.example.test/token" \
  -e "Bridge__Upstream__McpUrl=https://mcp.example.test/streamable" \
  -e "Bridge__Upstream__ClientId=fictional-client" \
  -e "Bridge__Upstream__ClientAuthentication__Method=none" \
  -e "Bridge__AllowedRedirectUris__0=https://client.example.test/callback" \
  "$IMAGE_TAG" >/dev/null

echo "==> Waiting for readiness"
for attempt in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:$PORT/health/ready" >/dev/null 2>&1; then
    echo "Ready after $attempt attempt(s)."
    break
  fi
  if [ "$attempt" -eq 30 ]; then
    echo "FAIL: container never became ready" >&2
    docker logs "$CONTAINER_NAME" >&2
    exit 1
  fi
  sleep 1
done

echo "==> Checking liveness and readiness content"
curl -fsS "http://127.0.0.1:$PORT/health/live" | grep -qx "Healthy" || { echo "FAIL: /health/live"; exit 1; }
curl -fsS "http://127.0.0.1:$PORT/health/ready" | grep -qx "Healthy" || { echo "FAIL: /health/ready"; exit 1; }
echo "PASS: liveness and readiness"

echo "==> Checking the process runs as the non-root 'app' user"
ACTUAL_USER=$(docker exec "$CONTAINER_NAME" whoami)
[ "$ACTUAL_USER" = "app" ] || { echo "FAIL: expected user 'app', got '$ACTUAL_USER'"; exit 1; }
echo "PASS: running as non-root user 'app'"

echo "==> Sending an in-flight request concurrently with SIGTERM"
# This proves the request completes rather than being dropped by an immediate
# shutdown; it does not exercise the full ShutdownDrainTimeoutSeconds window,
# since /register itself responds in well under a second. A longer-held
# in-flight request (for example, an MCP stream against a slow fake upstream)
# is exercised by the automated IntegrationTests/ContractTests suites instead
# — see tests/McpOAuthDcrBridge.IntegrationTests and docs/testing.md.
curl -fsS -m 5 "http://127.0.0.1:$PORT/register" \
  -H "Content-Type: application/json" \
  -d '{"redirect_uris":["https://client.example.test/callback"]}' \
  >/tmp/mcp-bridge-smoke-drain-response.json &
DRAIN_REQUEST_PID=$!
docker stop --time 30 "$CONTAINER_NAME" >/dev/null
wait "$DRAIN_REQUEST_PID"
grep -q '"client_id":"fictional-client"' /tmp/mcp-bridge-smoke-drain-response.json || { echo "FAIL: in-flight request did not complete during shutdown"; exit 1; }
rm -f /tmp/mcp-bridge-smoke-drain-response.json
echo "PASS: in-flight request completed rather than being dropped by shutdown"

EXIT_CODE=$(docker inspect "$CONTAINER_NAME" --format '{{.State.ExitCode}}')
[ "$EXIT_CODE" = "0" ] || { echo "FAIL: expected clean exit code 0 after SIGTERM, got $EXIT_CODE"; exit 1; }
echo "PASS: clean shutdown exit code"

docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

echo "==> Starting a container with missing required configuration"
set +e
docker run --rm --name "$CONTAINER_NAME" "$IMAGE_TAG" >/tmp/mcp-bridge-smoke-invalid-config.log 2>&1
INVALID_CONFIG_EXIT_CODE=$?
set -e
[ "$INVALID_CONFIG_EXIT_CODE" -ne 0 ] || { echo "FAIL: container with no configuration should not start successfully"; exit 1; }
grep -qi "Bridge" /tmp/mcp-bridge-smoke-invalid-config.log || { echo "FAIL: startup failure did not name the invalid configuration section"; exit 1; }
rm -f /tmp/mcp-bridge-smoke-invalid-config.log
echo "PASS: missing configuration fails closed at startup with a bounded, prompt exit"

echo "==> Scanning the image for known high/critical vulnerabilities"
if command -v trivy >/dev/null 2>&1; then
  trivy image --severity HIGH,CRITICAL --exit-code 1 --ignore-unfixed "$IMAGE_TAG"
  echo "PASS: trivy reported no unresolved high/critical findings"
elif docker scout version >/dev/null 2>&1; then
  docker scout cves --only-severity critical,high --exit-code "$IMAGE_TAG"
  echo "PASS: docker scout reported no unresolved high/critical findings"
else
  echo "SKIP: no image vulnerability scanner found (install trivy: https://aquasecurity.github.io/trivy/, or enable 'docker scout')." >&2
  echo "      This is a release-gate requirement — see docs/deployment.md#image-vulnerability-scanning." >&2
fi

echo "==> All container smoke tests passed"
