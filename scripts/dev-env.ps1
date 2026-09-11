# Scripts de gestión para entorno local de desarrollo — BitCode.Framework (F8-11)
param (
    [ValidateSet("up", "down", "status", "logs", "clean")]
    [string]$Action = "up"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

Set-Location $RepoRoot

switch ($Action) {
    "up" {
        Write-Host "Iniciando infraestructura local de BitCode.Framework..." -ForegroundColor Cyan
        docker compose up -d
        Write-Host ""
        Write-Host "Servicios iniciados en segundo plano." -ForegroundColor Green
        Write-Host "Endpoints locales disponibles:" -ForegroundColor Yellow
        Write-Host " - SQL Server: localhost:1433 (sa / Password123!)"
        Write-Host " - Redis:      localhost:6379"
        Write-Host " - Kafka:      localhost:9092"
        Write-Host " - OTel gRPC:  localhost:4317"
        Write-Host " - OTel HTTP:  localhost:4318"
        Write-Host " - Jaeger UI:  http://localhost:16686"
        Write-Host ""
        Write-Host "Verificando estado de los contenedores..." -ForegroundColor Cyan
        docker compose ps
    }
    "down" {
        Write-Host "Deteniendo contenedores de desarrollo..." -ForegroundColor Yellow
        docker compose down
        Write-Host "Entorno detenido." -ForegroundColor Green
    }
    "status" {
        docker compose ps
    }
    "logs" {
        docker compose logs -f
    }
    "clean" {
        Write-Host "Limpiando contenedores y volúmenes de datos (reset completo)..." -ForegroundColor Red
        docker compose down -v
        Write-Host "Volúmenes eliminados. El entorno arrancará desde cero en el próximo 'up'." -ForegroundColor Green
    }
}
