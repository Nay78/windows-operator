# Go Client Generation

Goal: external Go consumers can regenerate Windows Operator bindings from a pinned OpenAPI spec and generator.

## Generator Contract

- Source contracts live in `src/WindowsOperator.Core/Contracts`.
- `WindowsOperator.OpenApi` reflects those contracts and emits OpenAPI 3.0.3 JSON.
- `oapi-codegen` consumes the committed OpenAPI spec and writes the Go client.
- Generated files are committed, but never edited by hand.
- Regeneration must be deterministic from source contracts plus pinned generator version.
- The Go client module stays on Go 1.22 and pins `github.com/oapi-codegen/runtime` to a compatible version. Do not let local toolchain tidy silently raise it.

## Source Of Truth

The committed spec is:

```text
openapi/windows-operator.openapi.json
```

Generate it from Core contracts:

```bash
scripts/generate-openapi.sh
```

The OpenAPI generator project is:

```text
src/WindowsOperator.OpenApi
```

The generated Go module lives in:

```text
clients/go
```

## Regenerate Bindings

Use the root script:

```bash
scripts/generate-go-client.sh
```

This runs:

```bash
scripts/generate-openapi.sh
go run github.com/oapi-codegen/oapi-codegen/v2/cmd/oapi-codegen@v2.5.0 \
  -config openapi/go-client.oapi-codegen.yaml \
  openapi/windows-operator.openapi.json
gofmt -w clients/go/windowsoperator.gen.go clients/go/generate.go
(cd clients/go && go mod tidy)
```

From inside `clients/go`, this equivalent command is available:

```bash
go generate ./...
```

`go generate` delegates to the same root script, so both paths produce the same files.

## Files Owned By Generator

- `openapi/windows-operator.openapi.json`
- `clients/go/windowsoperator.gen.go`
- `clients/go/go.mod`
- `clients/go/go.sum`

Do not manually repair generated type names or paths. Fix the C# contract, route metadata, or `openapi/go-client.oapi-codegen.yaml`, then regenerate.

## External Consumer Usage

When this repo is pushed to GitHub and tagged, consumers can depend on:

```bash
go get github.com/alejg/windows-operator/clients/go@v0.1.0
```

Import:

```go
import wo "github.com/alejg/windows-operator/clients/go"
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

Before tagging a release:

```bash
scripts/generate-go-client.sh
cd clients/go && go test ./...
cd ../..
dotnet build WindowsOperator.sln --no-restore
dotnet test WindowsOperator.Portable.slnf --no-build
git diff --check
git status --short
```

The generated spec and Go client must be committed with the source changes that changed API contracts.
