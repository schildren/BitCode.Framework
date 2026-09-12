# syntax=docker/dockerfile:1
#
# Containerfile del host independiente de Workflow (F9-05, Fase 9 — "Host independiente"), mismo patrón
# que docker/sample-api/Dockerfile (F4-01): imagen "chiseled-extra" (ICU real, ver esa nota de sección 3
# de docs/politica-contenedores.md; Sample.Workflow.Api usa SQL Server igual que Sample.Api, así que
# necesita el mismo runtime con Globalization real, no invariant mode).
#
# Este Dockerfile NO reemplaza a Sample.Workflow.Api como "host de referencia/demo" (ver el comentario
# de Program.cs de ese proyecto) -- lo que demuestra es que, tal como quedó el módulo Workflow tras
# F9-02 (contract boundary)/F9-03 (data ownership)/F9-04 (eventos), puede arrancar, exponer health
# checks y publicar eventos a Kafka como proceso independiente, sin depender en tiempo de ejecución de
# ningún otro módulo de plataforma (docs/guia-workflow.md, sección "Host independiente (F9-05)").
#
# Build (contexto = raíz del repo, igual que sample-api, por los ProjectReference relativos a src/):
#   docker build -f docker/sample-workflow-api/Dockerfile -t bitcode/sample-workflow-api:0.1.0 .

# ---------------------------------------------------------------------------
# Etapa 1: build/publish.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
# mcr.microsoft.com/dotnet/sdk:10.0 (Debian "bookworm", .NET SDK 10.0.400) -- mismo digest que F4-01.

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props ./
COPY NuGet.Config ./
COPY samples/Directory.Build.props samples/
COPY src/Directory.Build.props src/
COPY samples/Sample.Workflow.Api/Sample.Workflow.Api.csproj samples/Sample.Workflow.Api/

# Todo el árbol de src/ (mismo criterio que docker/sample-api/Dockerfile: copiar completo por
# simplicidad y para no romper el build si el grafo de ProjectReference cambia). Sample.Workflow.Api
# referencia, además de lo que ya usa Sample.Api, BitCode.Platform.Workflow (y transitivamente
# BitCode.Platform.Workflow.Contracts) y Shared.Infrastructure.Messaging.Kafka (F9-05).
COPY src/ src/
COPY samples/Sample.Workflow.Api/ samples/Sample.Workflow.Api/

RUN dotnet publish samples/Sample.Workflow.Api/Sample.Workflow.Api.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Etapa 2: runtime final -- mismo criterio y misma imagen "-extra" (ICU real, no invariant mode) que
# docker/sample-api/Dockerfile, documentado ahí con la evidencia de por qué invariant mode falla contra
# SQL Server real.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet@sha256:f5b3b2e2e548828d50e349726f51a5de001286f02c4bbde77db0dd34eb9f55ff AS final
# mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra (ASP.NET Core Runtime 10.0.11).

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

# ASPNETCORE_HTTP_PORTS=8080 ya viene definido por la imagen base. Sin HTTPS dentro del contenedor
# (TLS se termina en el ingress/gateway, mismo criterio que Fase 4/5).
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sample.Workflow.Api.dll"]
