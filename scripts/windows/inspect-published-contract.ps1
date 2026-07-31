[CmdletBinding()]
param(
    [string]$RepoRoot = "Z:\windows-operator"
)

$ErrorActionPreference = "Stop"

$sourcePath = Join-Path $RepoRoot "src\WindowsOperator.Core\Contracts\MailStatusResult.cs"
$buildAssembly = Join-Path $RepoRoot "src\WindowsOperator.Core\bin\Debug\net8.0\WindowsOperator.Core.dll"
$agentAssembly = Join-Path $env:LOCALAPPDATA "WindowsOperator\agent\WindowsOperator.Core.dll"
$hostAssembly = Join-Path $env:ProgramData "WindowsOperator\host\WindowsOperator.Core.dll"
$dotnet = Join-Path $env:LOCALAPPDATA "WindowsOperator\dotnet-sdk\dotnet.exe"

function Get-AssemblyEvidence {
    param([Parameter(Mandatory)][string]$Path)

    [pscustomobject]@{
        exists = Test-Path -LiteralPath $Path -PathType Leaf
        sha256 = if (Test-Path -LiteralPath $Path -PathType Leaf) {
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        } else {
            $null
        }
        lastWriteTimeUtc = if (Test-Path -LiteralPath $Path -PathType Leaf) {
            (Get-Item -LiteralPath $Path).LastWriteTimeUtc
        } else {
            $null
        }
    }
}

$reflection = $null
$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "windows-operator-contract-probe-$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="WindowsOperator.Core">
      <HintPath>$hostAssembly</HintPath>
    </Reference>
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $probeRoot "probe.csproj") -Encoding UTF8
    @'
using System.Text.Json;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

var property = typeof(MailStatusResult).GetProperty(nameof(MailStatusResult.LastWorkerError))!;
var value = new MailStatusResult(true, 1, 0, "private", DateTimeOffset.UtcNow);
Console.Write(JsonSerializer.Serialize(new
{
    attributes = property.GetCustomAttributes(true).Select(item => item.GetType().Name).ToArray(),
    publicJsonHasLastWorkerError = JsonSerializer.Serialize(
        value,
        OperatorJson.PublicSerializerOptions).Contains("lastWorkerError", StringComparison.Ordinal),
}));
'@ | Set-Content -LiteralPath (Join-Path $probeRoot "Program.cs") -Encoding UTF8
    $priorErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $probeOutput = @(& $dotnet run --project (Join-Path $probeRoot "probe.csproj") --nologo --verbosity quiet 2>&1)
    $probeExitCode = $LASTEXITCODE
    $ErrorActionPreference = $priorErrorActionPreference
    if ($probeExitCode -ne 0) {
        throw "Published contract reflection probe failed with exit code $probeExitCode. $($probeOutput -join ' ')"
    }
    $reflection = $probeOutput[-1] | ConvertFrom-Json
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}

[pscustomobject]@{
    observedAtUtc = [DateTimeOffset]::UtcNow
    sourceHasInternalMarker = (Get-Content -LiteralPath $sourcePath -Raw).Contains(
        "[property: OperatorInternal] string? LastWorkerError")
    buildCore = Get-AssemblyEvidence -Path $buildAssembly
    agentCore = Get-AssemblyEvidence -Path $agentAssembly
    hostCore = Get-AssemblyEvidence -Path $hostAssembly
    reflection = $reflection
    localOpenApiHasLastWorkerError = (
        Invoke-RestMethod -Uri "http://127.0.0.1:43117/openapi.json" -TimeoutSec 10
    ).components.schemas.MailStatusResult.properties.PSObject.Properties.Name -contains "lastWorkerError"
    localMailStatusHasLastWorkerError = (
        Invoke-RestMethod -Uri "http://127.0.0.1:43117/v1/mail/status" -TimeoutSec 10
    ).PSObject.Properties.Name -contains "lastWorkerError"
} | ConvertTo-Json -Depth 5
