# Política de contenedores — BitCode.Framework

**Tarea:** F4-01 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Aplicado. Cierra la brecha #11 registrada en `docs/inventario-tecnico.md` ("Sin Dockerfile/manifiestos de contenedor en el repo relevado"). El criterio de aceptación de la fila F4-01 es "Escaneo sin CVE crítica": se ejecutó un escaneo real local con Trivy contra la imagen construida (evidencia en la sección 5) y se agregó un job `container-scan` a `.github/workflows/ci.yml` que repite ese mismo escaneo en cada build de CI — pero **la validación continua/gate real del pipeline solo queda confirmada la primera vez que ese job corra en GitHub Actions**, igual que el precedente de `docs/politica-dependencias.md` (SCA de NuGet). Tratar el gate de CI como "agregado, pendiente de primera verificación en pipeline real".

---

## 1. Alcance: qué proyectos tienen Containerfile

De los proyectos de la solución (`BitCode.Framework.slnx`), **el único host realmente ejecutable hoy es `samples/Sample.Api`** (`Microsoft.NET.Sdk.Web`, tiene `Program.cs` con `WebApplication.CreateBuilder`/`app.Run()`). Se verificó explícitamente antes de escribir ningún Containerfile:

- `samples/Sample.Eventing` usa `Microsoft.NET.Sdk` (librería de clases, no Web) y **no tiene `Program.cs`** — es un conjunto de contratos/handlers de ejemplo consumido por tests de la plataforma de eventos (Fase 3), no un proceso desplegable por sí solo. No aplica un Containerfile propio.
- Los 11 proyectos de `src/` son *building blocks* de librería (`docs/politica-empaquetado.md`), nunca hosts.

Por eso esta tarea entrega un único Containerfile, `docker/sample-api/Dockerfile`, como implementación de referencia. **Cuando exista un segundo host real** (por ejemplo un worker de Kafka standalone que no viva embebido en el API, `Shared.Infrastructure.Messaging.Kafka`), se agrega un Containerfile hermano en `docker/<host>/Dockerfile` siguiendo exactamente el mismo patrón descripto abajo — no uno genérico ni un único Dockerfile "para toda la solución".

---

## 2. Contexto de build

`docker/sample-api/Dockerfile` referencia proyectos de `src/` por ruta relativa (`ProjectReference`), así que **el contexto de build debe ser la raíz del repositorio**, no `samples/Sample.Api/`:

```powershell
docker build -f docker/sample-api/Dockerfile -t bitcode/sample-api:<version> .
```

`.dockerignore` (raíz del repo) excluye `bin/`, `obj/`, `tests/`, `docs/`, `.git/`, `.github/`, `.claude/` y archivos `.env*`/`appsettings.*.local.json` del contexto enviado al daemon — ningún artefacto de build local ni secreto de desarrollo llega a formar parte de la imagen o del contexto transferido.

---

## 3. Diseño de la imagen

### 3.1 Multi-stage: build vs. runtime

Dos etapas (`AS build`, `AS final`). La etapa `build` usa el SDK completo (compilador, resolución de NuGet, `MinVer`/`SourceLink`); la etapa `final` **solo copia el resultado de `dotnet publish`** (`COPY --from=build /app/publish .`). El SDK, el código fuente completo, los `.pdb`/artefactos intermedios y cualquier caché de NuGet nunca llegan a la imagen final — se descartan junto con la etapa `build` al construir.

### 3.2 Imagen base final: "chiseled" (Ubuntu Noble sin shell/paquetes), no Alpine

Se evaluaron dos opciones para minimizar la imagen final: Alpine (`aspnet:10.0-alpine`) y las imágenes "chiseled" de Microsoft (`aspnet:10.0-noble-chiseled*`), publicadas oficialmente por el equipo de .NET como el equivalente distroless del runtime. Se eligió **chiseled**:

- No incluye shell (`/bin/sh` no existe), gestor de paquetes, ni ninguna herramienta más allá del runtime de .NET — superficie de ataque mínima, verificado en esta tarea (`docker run --rm --entrypoint sh ...` falla porque el binario no existe).
- Corre como usuario **non-root por defecto** (`USER 1654`, usuario `app`) sin necesidad de crear un usuario manualmente en el Containerfile — a diferencia de Alpine, donde crear y fijar el usuario non-root es responsabilidad explícita de cada Dockerfile.
- Soportado oficialmente por Microsoft para producción (a diferencia de Alpine, que usa `musl` en vez de `glibc` y puede introducir incompatibilidades sutiles con librerías nativas de terceros).

**Hallazgo real durante la verificación (sección 5):** la variante chiseled *base* (`10.0-noble-chiseled`) fija `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` y no incluye ICU. `Microsoft.Data.SqlClient` (usado por `Shared.Infrastructure.Persistence`, que `Sample.Api` referencia) **falla en runtime** al abrir la conexión a SQL Server con `System.NotSupportedException: Globalization Invariant Mode is not supported`. Por eso `docker/sample-api/Dockerfile` usa la variante **`10.0-noble-chiseled-extra`**, que incluye ICU/tzdata/certificados completos y no invierte el modo de globalización — sigue sin shell ni gestor de paquetes, solo agrega esas tres piezas de runtime. Cualquier host futuro que use SQL Server real (prácticamente todos, dado que `Shared.Infrastructure.Persistence` es la base de persistencia del framework) debe partir de `-extra`, no de la variante base; la variante base queda reservada para un host que demostrablemente no necesite ICU (poco común).

### 3.3 Reproducibilidad: pineado por digest, no por tag

Ambas imágenes base (`sdk` en la etapa `build`, `aspnet` en la etapa `final`) se referencian por **digest SHA-256** (`FROM mcr.microsoft.com/dotnet/sdk@sha256:...`), no por tag flotante (`10.0`) ni por `latest`. Un tag como `10.0` apunta a una imagen distinta cada vez que Microsoft publica un parche de seguridad del SO base — pinear por digest garantiza que un mismo `docker build` produce bit a bit el mismo resultado (dado el mismo código fuente) sin importar cuándo se ejecute, cumpliendo el criterio de "reproducibles" de la fila F4-01.

**Costo de esta decisión, documentado explícitamente:** un digest pineado *no* recibe parches de seguridad del SO base automáticamente. Esto se compensa, no se contradice, con el `container-scan` de CI (sección 4) y con un procedimiento manual de actualización: cuando el escaneo reporte una CVE relevante originada en la imagen base, o de forma periódica (recomendado: mensual), se re-resuelve el digest más reciente del mismo tag —

```powershell
docker pull mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
docker inspect --format='{{index .RepoDigests 0}}' mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
```

— y se actualiza el `FROM` correspondiente en el Containerfile en un commit dedicado (no silencioso, revisable en PR). Automatizar esto con una herramienta de dependency-update (Renovate/Dependabot, que soportan pineado por digest de imágenes Docker) queda fuera de alcance de F4-01 y se deja como brecha explícita para una tarea de mantenimiento continuo.

### 3.4 Usuario non-root

`USER $APP_UID` en la etapa final (variable de entorno `APP_UID=1654` ya definida por la imagen base). No se crea un usuario custom porque la imagen base ya lo provee — crear uno nuevo agregaría complejidad sin beneficio de seguridad adicional. Verificado en runtime (sección 5): el proceso `dotnet Sample.Api.dll` corre con UID 1654, nunca UID 0 (root).

### 3.5 Puerto y TLS

`ASPNETCORE_HTTP_PORTS=8080` (puerto no privilegiado, ya definido por la imagen base — coherente con un proceso que corre sin privilegios de root, que no puede bindear puertos <1024). El Containerfile **no** expone HTTPS ni gestiona certificados TLS dentro del contenedor: la terminación TLS es responsabilidad del ingress/gateway del clúster (YARP, ADR 0007), no de cada pod de aplicación — es la práctica estándar en Kubernetes y evita que cada imagen cargue con la gestión de certificados/rotación.

### 3.6 Qué queda deliberadamente fuera de alcance de F4-01

- **Probes de salud** (`HEALTHCHECK`/liveness/readiness/startup): es la fila F4-04 del backlog, tarea separada. Este Containerfile no declara `HEALTHCHECK` todavía.
- **Shutdown graceful / drenado de tráfico**: fila F4-05.
- **Manifiestos de despliegue (Helm/Kustomize)**: fila F4-02.

No se adelantó ninguna de estas piezas para no mezclar el alcance de tareas distintas del backlog (Plan Maestro, sección 3.2).

---

## 4. Integración en CI (Plan Maestro, sección 7.3, paso "Container scan")

`.github/workflows/ci.yml` agrega el job `container-scan` (además de `build`, `test-unit`, `test-integration`, `dependency-scan` ya existentes):

1. Construye la imagen (`docker build -f docker/sample-api/Dockerfile -t bitcode/sample-api:ci .`).
2. Ejecuta [Trivy](https://trivy.dev/) (`aquasecurity/trivy-action`, pineado por SHA de commit — `ed142fd0673e97e23eac54620cfb913e5ce36c25` = tag `v0.36.0`, confirmado real vía la API de GitHub al momento de esta tarea) contra esa imagen, con los escáneres `vuln,secret,misconfig` y severidades `CRITICAL,HIGH`.
3. Publica el reporte como artefacto de build (`container-vulnerability-report`).
4. **Bloquea el pipeline únicamente si aparece una vulnerabilidad `CRITICAL`** — mismo umbral conservador que `dependency-scan`/`docs/politica-dependencias.md` sección 5: `HIGH` y severidades menores quedan como hallazgo visible en el reporte publicado, no bloquean el merge, para no generar falsos bloqueos por CVEs de la imagen base sin fix disponible todavía.

**Herramienta elegida — Trivy, no Grype:** ambas son opciones válidas y de código abierto (Apache-2.0); se eligió Trivy porque además de CVE de paquetes de SO y `dotnet-core` (vía `*.deps.json`, sin necesitar un SBOM previo) escanea en la misma pasada secretos embebidos y misconfiguraciones del propio Containerfile — cobertura más amplia con una sola herramienta y una sola dependencia nueva en el pipeline. Esta elección puede revisarse con un ADR si en el futuro se necesita alguna capacidad específica de Grype (por ejemplo su integración nativa con Syft/SBOM en formato SPDX para la tarea de SBOM del pipeline, aún no implementada).

**Esta tarea NO declara "sin CVE crítica" como aprobado en base a una corrida real de CI** — GitHub Actions no se ejecutó como parte de este cambio (sin acceso al runner desde este entorno). Lo que sí se ejecutó y quedó como evidencia verificable es un escaneo real, local, con la misma herramienta y los mismos parámetros, contra la imagen realmente construida (sección 5). El job de CI existe y quedará confirmado la primera vez que corra en GitHub Actions tras el merge.

---

## 5. Evidencia de verificación (F4-01, ejecutada en este entorno)

Todo lo siguiente se ejecutó localmente con Docker Desktop 29.4.1 disponible en el entorno de esta tarea (no simulado):

1. **Build real de la imagen:**
   ```
   docker build -f docker/sample-api/Dockerfile -t bitcode/sample-api:0.1.0 .
   ```
   Compiló con éxito los 9 proyectos de `src/` referenciados transitivamente por `Sample.Api` (`Shared.Kernel`, `Shared.Domain`, `Shared.Application`, `Shared.Infrastructure.Persistence`, `Shared.Infrastructure.Caching`, `Shared.Infrastructure.Http`, `Shared.Infrastructure.Security`, `Shared.Infrastructure.Web`, `Shared.Modularity`) más `Sample.Api` mismo, y publicó la imagen final. Tamaño de imagen final: **≈85 MB de contenido** (299 MB reportados como "disk usage" incluye capas compartidas con otras imágenes locales de .NET ya presentes).

2. **Usuario non-root confirmado dos veces:**
   - Estático: `docker inspect bitcode/sample-api:0.1.0 --format '{{.Config.User}}'` → `1654`.
   - En runtime: `docker top <container>` con el contenedor corriendo mostró el proceso `dotnet Sample.Api.dll` con `UID 1654`, nunca `0`/root.

3. **Arranque real contra SQL Server real** (no mockeado): se levantó `mcr.microsoft.com/mssql/server:2022-latest` en un contenedor separado, se corrió `bitcode/sample-api:0.1.0` apuntando `ConnectionStrings__Default` a esa instancia. Logs confirmaron `EnsureCreatedAsync` creando el esquema completo (tablas `IdempotencyKey`, `InboxMessage`, `OutboxMessage`, `Producto`, índices) contra SQL Server real, `Now listening on: http://[::]:8080`, `Application started`. `curl http://localhost:18080/openapi/v1.json` devolvió `HTTP 200`. Esto además fue lo que reveló y permitió corregir el problema de Globalization Invariant Mode descripto en la sección 3.2 (con la variante chiseled base la conexión a SQL Server fallaba; con `-extra` funcionó).

4. **Escaneo real con Trivy** (`aquasec/trivy:latest`, ejecutado vía Docker contra la imagen local, escáneres `vuln,secret,misconfig`, severidad `CRITICAL,HIGH`):
   ```
   Report Summary
   ┌────────────────────────────────────────────────────────┬─────────────┬─────────────────┬─────────┬───────────────────┐
   │                          Target                         │    Type     │ Vulnerabilities │ Secrets │ Misconfigurations │
   ├────────────────────────────────────────────────────────┼─────────────┼─────────────────┼─────────┼───────────────────┤
   │ bitcode/sample-api:0.1.0 (ubuntu 24.04)                 │   ubuntu    │        0        │    -    │         -         │
   │ app/Sample.Api.deps.json                                │ dotnet-core │        0        │    -    │         -         │
   │ .../Microsoft.AspNetCore.App/10.0.11/....deps.json      │ dotnet-core │        0        │    -    │         -         │
   │ .../Microsoft.NETCore.App/10.0.11/....deps.json         │ dotnet-core │        0        │    -    │         -         │
   └────────────────────────────────────────────────────────┴─────────────┴─────────────────┴─────────┴───────────────────┘
   ```
   **Cero vulnerabilidades CRITICAL o HIGH**, cero secretos detectados, cero misconfiguraciones — en la imagen tal como se construye hoy, con la base de datos de vulnerabilidades de Trivy vigente al 2026-09-07. Esto satisface el criterio de aceptación de F4-01 ("Escaneo sin CVE crítica") con evidencia real, no simulada; queda sujeto a que el mismo escaneo se repita en CI (sección 4) y ante cada actualización de dependencias/imagen base.

5. **Limpieza:** todos los contenedores y la red de prueba (`sample-api-test`, `sql-test`, `bitcode-test-net`) se eliminaron al finalizar; no quedó ningún recurso Docker persistente de esta verificación.

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-01 del backlog (Fase 4), sección 7.3 (orden del pipeline, paso "Container scan").
- [`inventario-tecnico.md`](inventario-tecnico.md) — brecha #11 (sin Dockerfile en el repo), ahora cerrada por esta tarea.
- [`politica-dependencias.md`](politica-dependencias.md) — mismo patrón de gate conservador (solo `Critical` bloquea) aplicado aquí al escaneo de contenedor.
- [`adr/0002-persistencia-sql-server.md`](adr/0002-persistencia-sql-server.md) — por qué `Sample.Api` depende de SQL Server real (motivo del hallazgo de Globalization Invariant Mode).
- `docker/sample-api/Dockerfile`, `.dockerignore`, `.github/workflows/ci.yml` (job `container-scan`) — implementación de esta política.
