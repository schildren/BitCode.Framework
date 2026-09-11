<#
.SYNOPSIS
    Script de orquestacion de Release local y simulacion para BitCode.Framework (F8-13).
.DESCRIPTION
    Valida el estado del repositorio, ejecuta pre-flight checks, calcula notas de version,
    actualiza CHANGELOG.md y prepara el tag de release SemVer.
.EXAMPLE
    .\scripts\release.ps1 -DryRun
    .\scripts\release.ps1 -Version "0.2.0"
#>

[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [string]$Version,

    [switch]$DryRun,

    [switch]$SkipTests,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "========================================================================" -ForegroundColor Cyan
Write-Host " BitCode Framework - Herramienta de Automatizacion de Release (F8-13)   " -ForegroundColor Cyan
Write-Host "========================================================================" -ForegroundColor Cyan

# 1. Comprobar working tree limpio
Write-Host "`n1. Verificando estado del working tree de Git..." -ForegroundColor White
$gitStatus = & git status --porcelain
if ($gitStatus -and -not $Force) {
    Write-Warning "El working tree contiene cambios no guardados o archivos sin seguimiento:"
    Write-Host $gitStatus
    if (-not $DryRun) {
        Write-Error "El release requiere un working tree limpio. Aplique commit o use -Force / -DryRun."
        exit 1
    }
}
Write-Host "   [OK] Working tree verificado." -ForegroundColor Green

# 2. Pre-flight check de herramientas
Write-Host "`n2. Ejecutando pre-flight check de herramientas (BitCode.Diagnostics)..." -ForegroundColor White
$doctorScript = Join-Path $RepoRoot "scripts\doctor.ps1"
if (Test-Path $doctorScript) {
    & powershell -ExecutionPolicy Bypass -File $doctorScript tools
    if ($LASTEXITCODE -ne 0) {
        Write-Error "El pre-flight check de herramientas fallo con codigo $LASTEXITCODE."
        exit 1
    }
}

# 3. Validacion de pruebas si no se omiten
if (-not $SkipTests) {
    Write-Host "`n3. Ejecutando suite de pruebas de arquitectura..." -ForegroundColor White
    $archTestProject = Join-Path $RepoRoot "tests\BitCode.Architecture.Tests\BitCode.Architecture.Tests.csproj"
    & dotnet test $archTestProject -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Las pruebas de arquitectura fallaron. El release no puede continuar."
        exit 1
    }
    Write-Host "   [OK] Pruebas de arquitectura superadas." -ForegroundColor Green
} else {
    Write-Host "`n3. Omitiendo ejecucion de pruebas (-SkipTests)." -ForegroundColor Yellow
}

# 4. Determinar version
$latestTag = (& git tag --sort=-creatordate | Select-Object -First 1)
Write-Host "`n4. Ultimo tag Git registrado: $latestTag" -ForegroundColor White

if (-not $Version) {
    Write-Host "   No se especifico version explicita. Mostrando notas de la proxima version candidata." -ForegroundColor Yellow
} else {
    Write-Host "   Version objetivo seleccionada: v$Version" -ForegroundColor Green
}

# 5. Generar notas de release
Write-Host "`n5. Generando notas de release a partir de Conventional Commits..." -ForegroundColor White
$changelogScript = Join-Path $RepoRoot "scripts\generate-changelog.mjs"
if (Test-Path $changelogScript) {
    & node $changelogScript --notes-only
}

# 6. Modo Dry-Run o ejecucion real
if ($DryRun) {
    Write-Host "`n------------------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host " [SIMULACION / DRY-RUN FINALIZADA] No se crearon tags ni se realizaron commits." -ForegroundColor Cyan
    Write-Host " Para aplicar el release ejecute:" -ForegroundColor White
    Write-Host "   .\scripts\release.ps1 -Version X.Y.Z" -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

if (-not $Version) {
    Write-Error "Debe especificar un numero de version valido (ej. 0.2.0) para crear el tag de release."
    exit 1
}

$tagName = "v$Version"
Write-Host "`n6. Actualizando CHANGELOG.md..." -ForegroundColor White
& node $changelogScript

Write-Host "`n7. Creando tag Git local: $tagName..." -ForegroundColor White
& git tag -a $tagName -m "Release $tagName"

Write-Host "`n========================================================================" -ForegroundColor Green
Write-Host " Release $tagName preparado localmente con exito!                       " -ForegroundColor Green
Write-Host "========================================================================" -ForegroundColor Green
Write-Host "Para disparar el workflow de CI/CD de GitHub Actions y publicar paquetes:" -ForegroundColor White
Write-Host "   git push origin $tagName" -ForegroundColor Yellow
Write-Host ""
