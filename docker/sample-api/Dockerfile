# syntax=docker/dockerfile:1
#
# Containerfile de referencia para un host ejecutable de BitCode.Framework — F4-01
# (docs/plan-maestro-bitcode-ia.md, Fase 4). Este es el único proyecto realmente
# ejecutable de la solución hoy (samples/Sample.Api, Microsoft.NET.Sdk.Web); el resto
# de samples/Sample.Eventing es una librería sin Program.cs (ver docs/politica-contenedores.md,
# sección 1). Cuando exista un segundo host real (worker de Kafka, etc.) se agrega un
# Containerfile hermano en docker/<host>/Dockerfile siguiendo el mismo patrón.
#
# Build (contexto = raíz del repo, IMPRESCINDIBLE porque el proyecto referencia otros
# proyectos de src/ por ruta relativa):
#   docker build -f docker/sample-api/Dockerfile -t bitcode/sample-api:0.1.0 .
#
# Imagenes base pineadas por digest (no por tag "latest" ni por tag flotante "10.0"),
# ver docs/politica-contenedores.md sección 3 para la justificación y el procedimiento
# de actualización periódica de esos digests.

# ---------------------------------------------------------------------------
# Etapa 1: build/publish — usa el SDK completo (compilador, NuGet, etc.).
# Esta etapa NUNCA llega a la imagen final.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk@sha256:4beef5b8919dcaa2dc924233bd069257e883cc7a061e09088a97d152d6a48510 AS build
# mcr.microsoft.com/dotnet/sdk:10.0 (Debian "bookworm", .NET SDK 10.0.400) al momento de F4-01.

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Metadatos y solución mínimos necesarios para restaurar el grafo de dependencias
# del host (ProjectReference a src/, no todo el .slnx).
COPY Directory.Build.props ./
COPY NuGet.Config ./
COPY samples/Directory.Build.props samples/
COPY src/Directory.Build.props src/
COPY samples/Sample.Api/Sample.Api.csproj samples/Sample.Api/

# Todo el árbol de src/ referenciado transitivamente por Sample.Api (Shared.Kernel,
# Shared.Domain, Shared.Application, Shared.Infrastructure.Persistence,
# Shared.Infrastructure.Caching, Shared.Infrastructure.Http, Shared.Infrastructure.Security,
# Shared.Infrastructure.Web, Shared.Modularity) se copia completo por simplicidad y para
# no romper el build si el grafo de ProjectReference cambia; el resto de src/ (BackgroundJobs,
# Observability, Messaging.Kafka, Testing) no es referenciado por este host y agrega poco
# peso al contexto (ya filtrado por .dockerignore de bin/obj).
COPY src/ src/
COPY samples/Sample.Api/ samples/Sample.Api/

RUN dotnet publish samples/Sample.Api/Sample.Api.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Etapa 2: runtime final — imagen "chiseled" (Ubuntu Noble sin shell, sin gestor de
# paquetes, sin herramientas de build), la variante mínima soportada oficialmente por
# .NET equivalente a "distroless" para este runtime. Corre como usuario non-root por
# defecto (UID 1654, "app"), sin necesidad de crear un usuario manualmente.
#
# Variante "-extra" (no la chiseled base): incluye ICU/tzdata/certificados completos y
# NO fuerza Globalization Invariant Mode. Se probó primero la chiseled base (invariant
# mode por defecto) y `Microsoft.Data.SqlClient` falla en runtime con
# `System.NotSupportedException: Globalization Invariant Mode is not supported` al abrir
# la conexión a SQL Server (docs/politica-contenedores.md, sección 3, deja registrada
# esta evidencia). Cualquier host que use `Shared.Infrastructure.Persistence`/SQL Server
# — como este — necesita ICU real, no el invariant mode.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet@sha256:f5b3b2e2e548828d50e349726f51a5de001286f02c4bbde77db0dd34eb9f55ff AS final
# mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra (ASP.NET Core Runtime 10.0.11) al momento de F4-01.

WORKDIR /app
COPY --from=build /app/publish .

# La imagen chiseled ya define USER 1654 (app) por defecto; se deja explícito por
# claridad y para que quede a prueba de un cambio futuro del valor por defecto.
USER $APP_UID

# ASPNETCORE_HTTP_PORTS=8080 ya viene definido por la imagen base (puerto no privilegiado,
# coherente con un proceso non-root). No se expone HTTPS aquí: TLS se termina en el
# ingress/gateway del clúster (YARP), no dentro del contenedor de la app (Fase 4/5).
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sample.Api.dll"]
