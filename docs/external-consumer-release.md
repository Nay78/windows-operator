# External Consumer Release Checklist

First external contract tag:

```text
v0.1.0
```

The Go module path is:

```text
github.com/alejg/windows-operator/clients/go
```

## Pre-Tag Gates

Run from repo root:

```bash
scripts/check-openapi-contract.sh
scripts/check-readme-route-inventory.sh
scripts/generate-go-client.sh
cd clients/go && go test ./...
cd ../..
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build --nologo
dotnet test tests/WindowsOperator.Host.Tests/WindowsOperator.Host.Tests.csproj --no-build --filter OpenApi_ --nologo
git diff --check
git status --short
```

When a previous release tag exists, wire a breaking-change checker through:

```bash
WINDOWS_OPERATOR_BREAKING_CMD='<checker command>' scripts/check-openapi-contract.sh
```

Optional OpenAPI lint hook:

```bash
WINDOWS_OPERATOR_LINT_CMD='<lint command>' scripts/check-openapi-contract.sh
```

## Live Host Gates

Against live Host:

```bash
curl -fsS http://127.0.0.1:43117/v1/health
curl -fsS http://127.0.0.1:43117/v1/capabilities
curl -fsS http://127.0.0.1:43117/openapi.json > live.openapi.json
diff -u openapi/windows-operator.openapi.json live.openapi.json
```

External-consumer smoke:

```bash
scripts/external-consumer-smoke.sh
```

This is a repo-owned release gate. The fresh consumer proof below must run from
a separate Go module and avoid repo scripts.

With artifact proof:

```bash
WINDOWS_OPERATOR_SMOKE_RUN_ID=<run-id> scripts/external-consumer-smoke.sh
```

## Fresh Consumer Proof

Use the commit SHA for a pre-tag dry run, then rerun with `v0.1.0` after the
tag is pushed:

```bash
tmpdir="$(mktemp -d)"
cd "$tmpdir"
go mod init consumer-proof
go get github.com/alejg/windows-operator/clients/go@<tag-or-commit>
```

Use the generated client to call:

- `GET /v1/health`
- `GET /v1/capabilities`
- one negative route that returns `OperatorError`
- `GET /v1/runs/{runId}/artifacts`
- `GET /v1/artifacts/{artifactId}`

For artifact calls, use an existing run id with at least one artifact; the
external-consumer smoke can verify that path when
`WINDOWS_OPERATOR_SMOKE_RUN_ID` points at such a run.

No consumer proof may call `scripts/linux/wo`, `Justfile`, SSH runner scripts,
staged PowerShell, or Windows-local paths.

## Tag

```bash
git tag -a v0.1.0 -m "Windows Operator external contract v0.1.0"
git push origin v0.1.0
```
