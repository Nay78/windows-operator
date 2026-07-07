# Go Client Generation

Goal: external Go consumers can regenerate Windows Operator bindings from a pinned OpenAPI spec and generator.

## Generator Contract

- Source contracts live in `src/WindowsOperator.Core/Contracts`.
- `WindowsOperator.OpenApi` reflects those contracts and emits OpenAPI 3.0.3 JSON.
- `oapi-codegen` consumes the committed OpenAPI spec and writes the Go client.
- Generated files are committed, but never edited by hand.
- Regeneration must be deterministic from source contracts plus pinned generator version.
- The Go client module stays on Go 1.22 and pins
  `github.com/oapi-codegen/runtime` to a compatible version. Do not let local
  toolchain tidy silently raise it.

## Source Of Truth

The committed spec is:

```text
openapi/windows-operator.openapi.json
```

Generate it from Core contracts:

```bash
scripts/generate-openapi.sh
```

Contract version source:

```text
src/WindowsOperator.Core/Contracts/OperatorContractVersion.cs
```

The OpenAPI generator project is:

```text
src/WindowsOperator.OpenApi
```

The generated Go module lives in:

```text
clients/go
```

Consumer docs and helpers live beside the generated file:

```text
clients/go/README.md
clients/go/helpers.go
```

## Regenerate Bindings

Use the root script:

```bash
scripts/generate-go-client.sh
```

The script is the source of truth for the exact generator command. It regenerates
OpenAPI, invokes the pinned `oapi-codegen` version, formats generated files, and
tidies the Go module at Go 1.22.

From inside `clients/go`, this equivalent command is available:

```bash
go generate ./...
```

`go generate` delegates to the same root script, so both paths produce the same
files.

## Files Owned By Generator

- `openapi/windows-operator.openapi.json`
- `clients/go/windowsoperator.gen.go`
- `clients/go/go.mod`
- `clients/go/go.sum`

Do not manually repair generated type names or paths. Fix the C# contract,
route metadata, or `openapi/go-client.oapi-codegen.yaml`, then regenerate.

Hand-written helpers are allowed only beside the generated client. They must
hide repeated consumer complexity and keep raw generated route access intact.
Current helpers cover operator error decoding, contract-version checks,
artifact download, generic polling, and PowerPoint job polling.

## External Consumer Usage

After the first release tag is created, consumers can depend on that tag:

```bash
go get github.com/Nay78/windows-operator/clients/go@<tag>
```

Import:

```go
import wo "github.com/Nay78/windows-operator/clients/go"
```

Create a client:

```go
client, err := wo.NewClientWithResponses("http://127.0.0.1:43117")
if err != nil {
    panic(err)
}
```

If the repo is hosted elsewhere, update `clients/go/go.mod` to match the final module path before tagging.

## Local Verification

Check committed OpenAPI and Go client against source contracts:

```bash
scripts/check-openapi-contract.sh
scripts/check-readme-route-inventory.sh
```

The contract check script:

- verifies committed `openapi.info.version` matches `OperatorContractVersion.Value`
- verifies `clients/go.SupportedContractVersion` matches `OperatorContractVersion.Value`
- regenerates OpenAPI into temp files and compares with committed spec
- regenerates the Go client into temp files and compares generated artifacts
- exposes optional hook points:
  - `WINDOWS_OPERATOR_LINT_CMD` for OpenAPI lint
  - `WINDOWS_OPERATOR_BREAKING_CMD` for breaking-change checks against `WINDOWS_OPERATOR_PREVIOUS_TAG`

Default check stays offline except normal local generator/toolchain use. If no
tag or hook command exists, hook steps skip without failure.

The README route-inventory check verifies that the public route list documents
every committed OpenAPI method/path entry.

Validate generated bindings compile:

```bash
cd clients/go
go test ./...
```

Validate generator compiles as part of the portable .NET set:

```bash
dotnet build WindowsOperator.Portable.slnf --no-restore
```

## Release Rule

Version rule:

- `OperatorContractVersion.Value` is release contract source of truth.
- Released contracts use plain SemVer like `0.1.0`.
- Unreleased contract work may use SemVer pre-release suffix like `0.1.1-alpha.1`.
- Change the version only when committed OpenAPI or generated client contract changes under the SemVer rules in `docs/external-consumer-integration.md`.

Before tagging a release:

```bash
scripts/check-openapi-contract.sh
scripts/generate-go-client.sh
cd clients/go && go test ./...
cd ../..
dotnet build WindowsOperator.Portable.slnf --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build
git diff --check
git status --short
```

The generated spec and Go client must be committed with the source changes that changed API contracts.
