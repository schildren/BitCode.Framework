# syntax=docker/dockerfile:1
#
# Containerfile de BitCode.Gateway (F4-08) -- sigue el mismo patrón que docker/sample-api/Dockerfile
# (F4-01): imagen mínima, non-root, reproducible. Ver docs/politica-contenedores.md.
#
# Build (contexto = raíz del repo, imprescindible porque el proyecto referencia otros proyectos de
# src/ por ruta relativa):
#   docker build -f docker/gateway/Dockerfile -t bitcode/gateway:0.1.0 .

# ---------------------------------------------------------------------------
# Etapa 1: build/publish.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
# mcr.microsoft.com/dotnet/sdk:10.0 (Debian "bookworm", .NET SDK 10.0.400) -- mismo digest que
# docker/sample-api/Dockerfile (F4-01) al momento de esta tarea.

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props ./
COPY NuGet.Config ./
COPY src/Directory.Build.props src/
COPY src/BitCode.Gateway/BitCode.Gateway.csproj src/BitCode.Gateway/
COPY src/BitCode.Gateway/Directory.Build.props src/BitCode.Gateway/

# Árbol de src/ referenciado transitivamente por BitCode.Gateway (Shared.Infrastructure.Security y su
# propio árbol de dependencias -- Shared.Kernel/Shared.Domain/Shared.Modularity --, más
# Shared.Infrastructure.Observability): se copia el árbol completo de src/ por simplicidad y para no
# romper el build si el grafo de ProjectReference cambia, mismo criterio que docker/sample-api/Dockerfile.
COPY src/ src/

RUN dotnet publish src/BitCode.Gateway/BitCode.Gateway.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Etapa 2: runtime final -- imagen "chiseled-extra" (ICU/tzdata/certificados completos, sin invariant
# mode), non-root por defecto (UID 1654, "app"). BitCode.Gateway no usa Microsoft.Data.SqlClient
# directamente, pero se mantiene la misma variante "-extra" que docker/sample-api/Dockerfile para
# validación de certificados TLS salientes reales (YARP proxyando hacia destinos HTTPS) sin invariant
# mode -- ver docs/politica-contenedores.md sección 3.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet@sha256:f5b3b2e2e548828d50e349726f51a5de001286f02c4bbde77db0dd34eb9f55ff AS final
# mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra (ASP.NET Core Runtime 10.0.11).

WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

# ASPNETCORE_HTTP_PORTS=8080 ya viene definido por la imagen base. TLS externo se termina delante del
# Gateway (Ingress/LB, Fase 4/5) -- el Gateway mismo escucha HTTP en el cluster.
EXPOSE 8080

ENTRYPOINT ["dotnet", "BitCode.Gateway.dll"]
