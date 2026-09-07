# Política de dependencias — BitCode.Framework

**Tarea:** F0-06 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** Aplicada parcialmente en CI — ver sección 5. El criterio de aceptación de F0-06 es "Aplicada en CI"; se agregó un control real y mínimo (`dotnet list package --vulnerable`) al pipeline, pero **no fue validado todavía por una corrida real de GitHub Actions** (solo se ejecutó localmente, ver evidencia en el reporte de cierre de esta tarea). Tratar el gate como "agregado, pendiente de primera verificación en pipeline real".

---

## 1. Alcance

Esta política cubre las dependencias de terceros consumidas por el código del framework (`src/`, `samples/`, `tests/`, `templates/`): paquetes NuGet, y en el futuro imágenes base de contenedor y paquetes npm del frontend Angular (fuera del alcance actual porque el repositorio no tiene todavía frontend, ver `docs/inventario-tecnico.md`). No cubre la licencia del propio producto BitCode, que es una decisión distinta registrada en [`adr/0008-licencias-open-core.md`](adr/0008-licencias-open-core.md) (Proposed, pendiente de aprobación humana) — ambas decisiones son interdependientes, tal como señala ese ADR, pero esta política no adelanta ni condiciona la decisión de licencia del producto.

---

## 2. Licencias de dependencias

**Permitidas sin excepción** (compatibles tanto con una eventual capa Apache-2.0 como con una capa propietaria, sin obligación de redistribuir código fuente propio):

- MIT
- Apache-2.0
- BSD-2-Clause / BSD-3-Clause
- ISC

**Requieren excepción explícita antes de incorporarse** (licencias con copyleft débil que imponen condiciones sobre el binario que las enlaza, o licencias no estándar/ambiguas):

- LGPL (cualquier versión) — el copyleft débil no impide el enlace dinámico, pero sí impone condiciones de redistribución que deben revisarse caso por caso, especialmente para la eventual capa propietaria del Open-Core.
- MPL-2.0.
- Licencias de código abierto no listadas en la SPDX License List, o licencias "custom" de un solo proveedor.

**Prohibidas sin excepción** (copyleft fuerte, incompatibles con distribuir binarios propietarios o con el modelo Open-Core propuesto):

- GPL (cualquier versión).
- AGPL (cualquier versión) — especialmente relevante si BitCode se ofrece como servicio (SaaS), donde AGPL exigiría publicar el código fuente del servicio completo.
- SSPL.
- Cualquier licencia que exija divulgar el código fuente de aplicaciones consumidoras del framework.

**Proceso de excepción:**

1. Quien necesite incorporar una dependencia con licencia en la categoría "requiere excepción" abre un ADR en `docs/adr/` (siguiendo el formato de los ADR ya existentes, por ejemplo `0006-cache-hybridcache-valkey-redis.md`) que documente: la dependencia, la licencia exacta (con enlace a la fuente), qué alternativa con licencia permitida se evaluó y por qué se descarta, y el alcance de uso (¿se enlaza en el núcleo Apache-2.0 o solo en la capa propietaria?).
2. El ADR queda en estado `Proposed` hasta que un responsable de arquitectura (la misma figura que aprueba `architecture-principles.md` y los ADR de la sección 13 del Plan Maestro) lo apruebe explícitamente y lo pase a `Accepted`.
3. Una dependencia con licencia "prohibida" no admite excepción — se rechaza directamente sin necesidad de ADR, salvo que la propia sección 13 del Plan Maestro trate el caso como "aprobación de una excepción de seguridad o licencia", que requiere aprobación humana explícita y no puede resolverla la IA por sí sola.
4. Todo ADR de excepción de licencia se referencia desde este documento y desde el futuro `THIRD-PARTY-NOTICES` (F8-14).

**Herramienta:** esta política no introduce todavía un scanner de licencias automatizado en CI (por ejemplo, un license-checker de terceros) — eso excedería el alcance mínimo de F0-06 (sección 9 del Plan Maestro, "elementos que no deben adelantarse": no introducir herramientas complejas sin ADR). La verificación de licencias hoy es manual, en el momento de agregar un `PackageReference` nuevo, siguiendo la acción prohibida explícita de la sección 3.2 del Plan Maestro: "Introducir una dependencia sin revisar licencia, mantenimiento, seguridad y compatibilidad". Automatizar el license scan queda como pendiente explícito (ver sección 6).

---

## 3. Software Composition Analysis (SCA) y CVE

**Regla:**

- Toda dependencia (directa o transitiva) se audita contra vulnerabilidades conocidas (NVD/GitHub Advisory Database) antes de cada release y en cada build de CI, mediante `dotnet list package --vulnerable --include-transitive` (ver sección 5 para el detalle del gate agregado).
- **Priorización de CVE:**
  - **Critical:** bloquea el pipeline (build rojo). No se libera ningún artefacto con una vulnerabilidad Critical sin remediar o sin una excepción documentada y aprobada (ver más abajo).
  - **High:** no bloquea el pipeline en este momento (ver justificación en la sección 5, dado el estado real de vulnerabilidades transitivas heredadas detectado durante esta tarea), pero se registra como hallazgo visible en cada corrida y debe remediarse en un plazo objetivo de **30 días** o documentarse una excepción con causa (por ejemplo, "biblioteca solo se usa en tiempo de test, no llega a producción").
  - **Moderate:** remediar en el ciclo normal de actualización de dependencias (ver sección 4), plazo objetivo 90 días.
  - **Low:** se registra, se prioriza junto con la actualización rutinaria de dependencias, sin plazo estricto.
- Una excepción a un CVE sin parche disponible (por ejemplo, la dependencia no publicó todavía una versión corregida) se documenta explícitamente: dependencia, CVE, severidad, por qué no se puede mitigar hoy (upgrade no disponible, breaking change no evaluado, uso acotado a un contexto no expuesto), y quién la aprueba. Un CVE Critical sin mitigación disponible es candidato a "aprobación de una excepción de seguridad" de la sección 13 del Plan Maestro (aprobación humana explícita), no una decisión unilateral de la IA.
- La ejecución del propio SCA nunca se deshabilita para lograr que el pipeline apruebe (acción prohibida explícita, sección 3.2 del Plan Maestro) — si un cambio introduce una vulnerabilidad nueva de severidad Critical, se corrige la dependencia o se revierte el cambio, no se quita el chequeo.

---

## 4. Actualización de dependencias

**Cadencia:**

- Revisión rutinaria mensual de paquetes desactualizados (`dotnet list package --outdated`), fuera del pipeline automático (proceso manual mientras no exista Dependabot/Renovate, ver sección 6).
- Actualización inmediata (fuera de la cadencia mensual) cuando el SCA reporta un CVE Critical o High con parche disponible.
- Una actualización de versión **mayor** de una dependencia sigue el mismo análisis de compatibilidad que cualquier cambio de contrato (`docs/politica-versionado.md`) antes de aplicarse — no se actualiza una dependencia mayor "de paso" dentro de una tarea no relacionada (acción prohibida: "mezclar refactorizaciones ajenas a la tarea", sección 3.2 del Plan Maestro).
- La deriva de versión ya detectada en el inventario técnico (`docs/inventario-tecnico.md`, brecha #3: paquetes `Microsoft.Extensions.*` en 8.0.x conviviendo con 9.0.0 dentro de proyectos `net8.0`) no se corrige como parte de esta tarea — es un hallazgo de F0-01 cuya remediación (probablemente vía Central Package Management, `Directory.Packages.props`) corresponde a una tarea de Fase 0/Fase 1 dedicada, no a F0-06. Esta política dejará de tolerar nueva deriva de versión una vez exista CPM.

---

## 5. Aplicación en CI (criterio de aceptación de F0-06)

**Cambio realizado:** se agregó el job `dependency-scan` a `.github/workflows/ci.yml`, que ejecuta:

```
dotnet list BitCode.Framework.slnx package --vulnerable --include-transitive
```

y falla el build únicamente si el reporte contiene una vulnerabilidad de severidad **Critical**; para severidades High/Moderate/Low, el step las imprime en el log (visibles como salida del step, no ocultas) pero no bloquea el pipeline.

**Por qué el umbral es "solo Critical" y no "cualquier severidad" (decisión conservadora documentada, no un control debilitado a propósito):**

Se ejecutó el comando localmente contra el estado real del repositorio antes de decidir el umbral (ver comando y salida completa en el reporte de cierre de esta tarea). El resultado muestra **vulnerabilidades transitivas preexistentes de severidad High y Moderate en prácticamente todos los proyectos de test y en los proyectos que dependen de EF Core SQL Server / Testcontainers**, ninguna introducida por esta tarea:

- `SSH.NET 2023.0.0` (High) y `System.Net.Http 4.3.0` / `System.Text.RegularExpressions 4.3.0` (High) — transitivas de `Testcontainers.MsSql`/`Testcontainers.Redis` (`Shared.Testing` y todo proyecto que lo referencia).
- `Azure.Identity 1.10.3` (Moderate) y `Microsoft.Identity.Client 4.56.0` (Low/Moderate) y `System.Formats.Asn1 5.0.0` (High) — transitivas de `Microsoft.EntityFrameworkCore.SqlServer` (autenticación Azure AD opcional de EF Core), presentes en `Shared.Infrastructure.Persistence`, `Shared.Infrastructure.Security` y `Sample.Api`.
- `SQLitePCLRaw.lib.e_sqlite3 2.1.6` (High) — transitiva de `Microsoft.EntityFrameworkCore.Sqlite`, usado solo en proyectos de test.

Ninguna de estas es Critical. Bloquear el pipeline hoy por severidad High/Moderate dejaría el build en rojo en prácticamente todos los jobs de test de forma inmediata, sin que esta tarea (F0-06) tenga el alcance ni la autorización para actualizar de golpe EF Core, Testcontainers o resolver la deriva de versión de la sección 4 — eso violaría la regla de "cambio mínimo y cohesionado" (sección 3.3) y la prohibición de "reescribir módulos completos sin justificación" (sección 3.2). Un umbral que solo bloquea en Critical:

- Es un control real y verificable (no es un placeholder ni un check que siempre pasa): si aparece una vulnerabilidad Critical nueva, el pipeline sí se pone rojo.
- No apaga el pipeline actual "en verde" (`docs/inventario-tecnico.md` confirma CI en verde con build + unit + integration) por deuda técnica preexistente que no es responsabilidad de esta tarea.
- Dejar constancia explícita de las vulnerabilidades High/Moderate detectadas (en vez de ignorarlas silenciosamente) es lo que exige la instrucción de esta tarea y el principio de "no declarar terminada una tarea con controles de calidad deshabilitados" — por eso quedan documentadas en este archivo (arriba) con plazos de remediación (30/90 días) en la sección 3, no descartadas.

**Este umbral es un punto de partida deliberadamente conservador, no la política final.** Se espera que una tarea posterior (F1-01 o una tarea dedicada de actualización de dependencias) resuelva las vulnerabilidades High/Moderate detectadas y luego se pueda subir el umbral bloqueante a "High o superior".

**Pendiente de verificación:** el step se agregó y se probó el comando subyacente **localmente** (ver reporte de cierre), pero el job `dependency-scan` en sí, dentro de GitHub Actions, **no fue ejecutado todavía en una corrida real del pipeline**. Debe tratarse como "agregado, no confirmado en CI real" hasta la primera corrida en `push`/`pull_request` sobre `master`.

---

## 5.1. Registro de evaluación — `Confluent.Kafka` (F3-02)

Primera dependencia nueva agregada al repositorio desde que existe este documento; se deja constancia
del análisis exigido por la sección 3.2 del Plan Maestro ("no introducir una dependencia sin revisar
licencia, mantenimiento, seguridad y compatibilidad"):

- **Paquete:** `Confluent.Kafka` 2.15.0 (`src/Shared.Infrastructure.Messaging.Kafka`).
- **Licencia:** Apache-2.0 — categoría "permitida sin excepción" (sección 2), no requiere ADR de excepción.
- **Mantenimiento:** publicado y mantenido activamente por Confluent Inc.; es el cliente oficial de Kafka para .NET (wrapper de `librdkafka`), sin alternativa con adopción comparable en el ecosistema .NET.
- **Seguridad:** `dotnet list package --vulnerable --include-transitive` (job `dependency-scan`) no reportó ninguna vulnerabilidad conocida para `Confluent.Kafka` 2.15.0 ni sus transitivas al momento de esta tarea.
- **Compatibilidad:** biblioteca `netstandard2.0`/`net6.0`/`net8.0`, compatible con el TFM único del repo (`net10.0`); sin conflicto de versión detectado en la restauración/build de la solución completa.
- **Alcance de uso:** solo `src/Shared.Infrastructure.Messaging.Kafka` la referencia; `Shared.Application` (contratos de F3-01) sigue sin depender de ningún paquete de broker, según su propio criterio de aceptación.

También se agregó `Testcontainers.Kafka` 3.10.0 (MIT, misma familia y versión que `Testcontainers`/`Testcontainers.MsSql`/`Testcontainers.Redis` ya usados en `Shared.Testing`) — mismo criterio de licencia/mantenimiento/compatibilidad ya aplicado a esas dependencias, sin evaluación adicional necesaria más allá de fijar la misma versión mayor.

## 5.2. Registro de evaluación — `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` (F4-03)

Dependencia agregada para cerrar R-TEC-08 (`docs/risk-register.md`): persistir el key ring de Data
Protection de `Shared.Infrastructure.Security` en Redis, mismo backend que `AddSharedCaching` ya usa
como L2 (`Caching:RedisConnectionString`), para que todas las réplicas de un consumidor que despliegue
BFF/Authorization Code compartan el mismo key ring:

- **Paquete:** `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` 10.0.11 (`src/Shared.Infrastructure.Security`).
- **Licencia:** MIT — categoría "permitida sin excepción" (sección 2); no requiere ADR. Es un paquete first-party del repositorio `dotnet/aspnetcore`, publicado por Microsoft con el mismo ciclo de versionado (`10.0.11`) que el resto de los paquetes `Microsoft.AspNetCore.*` ya referenciados por este mismo proyecto (`Microsoft.AspNetCore.DataProtection`, `Microsoft.AspNetCore.Authentication.JwtBearer`, etc.) — no es una dependencia de terceros nueva a efectos de la sección 3.2 del Plan Maestro, ya que el equipo del framework ya evaluó y adoptó ese mismo proveedor/ciclo de mantenimiento para las dependencias hermanas.
- **Mantenimiento:** mantenido por el equipo de ASP.NET Core dentro del propio repositorio `dotnet/aspnetcore`; mismo nivel de soporte que `Microsoft.AspNetCore.DataProtection` (ya en uso desde F2-02).
- **Seguridad:** `dotnet list package --vulnerable --include-transitive` sobre `Shared.Infrastructure.Security` no reportó ninguna vulnerabilidad conocida para este paquete ni sus transitivas (`StackExchange.Redis` incluido) al momento de esta tarea.
- **Compatibilidad:** TFM `net10.0`, compatible con el TFM único del repo; build de la solución completa (`dotnet build BitCode.Framework.slnx`) y de los 433 tests de `Shared.Infrastructure.Security.Tests` + 49 de `Shared.Infrastructure.Web.Tests` sin regresiones.
- **Alcance de uso:** solo `src/Shared.Infrastructure.Security/Oidc/SharedDataProtectionServiceCollectionExtensions.cs` la referencia directamente (vía `PersistKeysToStackExchangeRedis`); condicionado a que `Caching:RedisConnectionString` esté configurado, igual patrón que `AddSharedCaching` (`Shared.Infrastructure.Caching`) — sin Redis configurado, no se abre ninguna conexión nueva y Data Protection cae al almacenamiento por defecto (documentado como válido solo para una instancia/desarrollo).

## 5.3. Registro de evaluación — `Serilog.Sinks.OpenTelemetry` (F4-10)

Dependencia agregada para cerrar la brecha real que dejaba `SerilogHostBuilderExtensions.UseSharedSerilog`
(`Shared.Infrastructure.Observability`): los logs solo se escribían a `Console`, nunca se exportaban vía
OTLP, aunque el nombre de la fila del backlog de F4-10 dice explícitamente "Centralizar exportación de
LOGS, métricas y trazas" (métricas/trazas ya lo hacían desde F3-10):

- **Paquete:** `Serilog.Sinks.OpenTelemetry` 4.2.0 (`src/Shared.Infrastructure.Observability`).
- **Licencia:** MIT (repositorio `serilog/serilog-sinks-opentelemetry`, misma organización GitHub que
  `Serilog`/`Serilog.AspNetCore`/`Serilog.Sinks.Console` ya referenciados por este mismo proyecto) —
  categoría "permitida sin excepción" (sección 2), no requiere ADR.
- **Mantenimiento:** mantenido activamente por la organización `serilog` (autores del propio Serilog),
  versión estable 4.2.0 publicada en NuGet.org, sin señales de abandono.
- **Seguridad:** `dotnet list package --vulnerable --include-transitive` sobre
  `Shared.Infrastructure.Observability` no reportó ninguna vulnerabilidad conocida para este paquete ni
  sus transitivas al momento de esta tarea.
- **Compatibilidad:** TFM `net10.0` (y `net8.0`), compatible con el TFM único del repo; build de la
  solución completa (`dotnet build BitCode.Framework.slnx`) sin regresiones, y los 6 tests de
  `Shared.Infrastructure.Observability.Tests` (2 nuevos de esta tarea) en verde.
- **Alcance de uso:** solo `SerilogHostBuilderExtensions.UseSharedSerilog` la referencia, y solo activa el
  sink `WriteTo.OpenTelemetry(...)` cuando `OpenTelemetry:OtlpEndpoint` está configurado (mismo criterio
  condicional que ya usan `tracing.AddOtlpExporter`/`metrics.AddOtlpExporter` en
  `ObservabilityServiceCollectionExtensions`, F3-10) — un host sin collector configurado (desarrollo
  local) no abre ninguna conexión OTLP nueva, sigue escribiendo únicamente a `Console`.

## 6. Explícitamente fuera de alcance de esta tarea (sección 9 del Plan Maestro)

No se agregan en F0-06, por exceder el alcance de "aplicar una verificación básica en CI" y requerir su propio ADR/evaluación:

- Herramientas externas de SCA/gestión de dependencias (Snyk, Dependabot, Renovate) con configuración completa.
- Un scanner de licencias de terceros automatizado (license-checker, FOSSA, etc.).
- SAST, secret scanning, SBOM y container scan — brechas ya identificadas en `docs/inventario-tecnico.md` (sección 5) para tareas específicas del plan (F8-14 SBOM, F10-04 security assessment).
- Central Package Management (`Directory.Packages.props`) para resolver la deriva de versión de la brecha #3 del inventario técnico.

---

## 7. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — secciones 3.2, 3.5, 9 y 13.
- [`architecture-principles.md`](architecture-principles.md) — sección 3 (Seguridad), que ya remite a esta política como pendiente.
- [`adr/0008-licencias-open-core.md`](adr/0008-licencias-open-core.md) — licencia del producto (decisión distinta, interdependiente).
- [`inventario-tecnico.md`](inventario-tecnico.md) — sección 5 (pipeline CI actual) y sección 7, brechas #2, #3, #4, #7.
- `.github/workflows/ci.yml` — job `dependency-scan` agregado por esta tarea.

## Aprobación

| Campo | Valor |
|---|---|
| Estado | Aplicada parcialmente en CI — gate agregado, pendiente de primera corrida real en GitHub Actions |
| Aprobado por | Pendiente |
| Fecha de aprobación | Pendiente |
