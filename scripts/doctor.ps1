<#
.SYNOPSIS
    Script de diagnóstico accionable para BitCode.Framework (F8-12).
.DESCRIPTION
    Ejecuta la herramienta BitCode.Diagnostics para validar el estado de herramientas,
    configuraciones y servicios de infraestructura local.
.EXAMPLE
    .\scripts\doctor.ps1
    .\scripts\doctor.ps1 tools
    .\scripts\doctor.ps1 connectivity
    .\scripts\doctor.ps1 --format json
#>

[CmdletBinding()]
param (
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DiagnosticArgs
)

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "tools\BitCode.Diagnostics\BitCode.Diagnostics.csproj"

if (-not (Test-Path $ProjectPath)) {
    Write-Error "No se encontró el proyecto BitCode.Diagnostics en $ProjectPath"
    exit 1
}

$Arguments = @("run", "--project", $ProjectPath, "--")
if ($DiagnosticArgs) {
    $Arguments += $DiagnosticArgs
}

& dotnet $Arguments
exit $LASTEXITCODE
