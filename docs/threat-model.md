# Threat Model — BitCode.Framework

**Tarea:** F0-07 (Fase 0 — Gobierno, arquitectura y línea base) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-05
**Estado:** **Completado — pendiente de revisión de seguridad.** El criterio de aceptación de F0-07 exige revisión de seguridad humana explícita; este documento no debe tratarse como aprobado hasta que un responsable de seguridad lo revise (ver sección "Revisión" al final). Ningún hallazgo de este documento autoriza por sí mismo un cambio de código; las mitigaciones "pendientes" quedan como candidatas a `docs/risk-register.md` (F0-12, aún no creado) y a tareas de fase concretas.

**Método:** análisis STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) adaptado al estado real del código, no a un modelo objetivo aspiracional. Cada amenaza se ancla a un archivo, componente o decisión ya existente (`docs/inventario-tecnico.md`, ADR en `docs/adr/`, `docs/convenciones.md`, `docs/architecture-principles.md`) o a una ausencia explícita confirmada por lectura directa del código (`src/Shared.Infrastructure.Security`, `src/Shared.Infrastructure.Persistence/MultiTenancy`, `.github/workflows/ci.yml`). No se modelan componentes que no existen todavía (Kafka, gateway YARP, IdP externo) como si estuvieran implementados — se los trata como amenazas futuras/planeadas, coherente con el estado `Proposed` de los ADR 0004/0005/0007.

Este documento **no contradice** las decisiones ya registradas en `docs/adr/`; las usa como fuente de verdad de "qué está decidido" vs. "qué sigue abierto". Tampoco repite gates que ya están asignados a otra tarea del plan (p. ej. SAST/SCA/secret scanning en CI son brecha ya documentada en `docs/inventario-tecnico.md` sección 5 y se referencian aquí, no se re-litigan).

---

## 1. Activos a proteger

| # | Activo | Dónde vive hoy | Por qué importa |
|---|---|---|---|
| A1 | Datos de negocio por tenant (entidades `ITenantEntity`) | SQL Server, vía `MultiTenantDbContext` (`src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenantDbContext.cs`) | Confidencialidad e integridad entre clientes distintos del mismo despliegue compartido (ADR 0003). |
| A2 | Credenciales de usuario final (hash de contraseña, tokens de sesión) | ASP.NET Identity (`Shared.Infrastructure.Security/Identity/ApplicationUser.cs`) + JWT propio (`Jwt/JwtTokenGenerator.cs`) + `RefreshToken` | Compromiso permite suplantación de identidad y escalamiento de privilegios. |
| A3 | Secreto de firma JWT (`JwtOptions.SecretKey`) | Configuración del consumidor (no está en el repo; `JwtOptions` exige el valor en `appsettings`/secret manager del proyecto que use el framework) | Quien lo posee puede forjar tokens válidos para cualquier usuario/rol sin pasar por Identity. |
| A4 | Permisos y roles (claims `permission` sobre `AspNetRoleClaims`) | `PermissionService`, `RoleManagerPermissionExtensions` (`Shared.Infrastructure.Security/Permissions`) | Base de la autorización *default deny* del framework (`docs/architecture-principles.md`, sección 3). |
| A5 | Cadenas de conexión a SQL Server / Redis | Configuración del consumidor (`ConnectionStrings:Default` vacío en `samples/Sample.Api/appsettings.json`, correcto: no se commitea el valor real) | Acceso directo a la base = bypass total del filtro de tenancy y de la capa de aplicación. |
| A6 | Código fuente del framework y de los proyectos consumidores | Repositorio Git, historial, `.github/workflows/ci.yml` | Manipulación del código o del pipeline compromete todo despliegue derivado. |
| A7 | Pipeline CI (secretos de GitHub Actions, artefactos TRX) | `.github/workflows/ci.yml` | Vector para inyectar código malicioso o exfiltrar secretos de build si se compromete un PR o una dependencia. |
| A8 | Auditoría de acciones sensibles (`IAuditedEntity`) | `Shared.Domain` (interfaz), aplicada por reflexión en persistencia | Evidencia forense; su ausencia o alterabilidad rompe no-repudio. |
| A9 | Disponibilidad del servicio (API, SQL Server, Redis) | Todo el runtime | Interrupción afecta a todos los tenants del despliegue compartido (no hay aislamiento de recursos por tenant hoy). |

---

## 2. Actores

| Actor | Descripción | Confianza asumida hoy |
|---|---|---|
| Usuario final autenticado | Consume la API vía JWT emitido por el proyecto consumidor | Media — depende de validación de token, sin MFA ni OIDC todavía (ADR 0004, `Proposed`). |
| Administrador de tenant | Usuario con permisos elevados dentro de su propio tenant (roles/claims) | Media — mismo mecanismo de permisos que un usuario normal, sin separación de plano de administración. |
| Operador de plataforma | Acceso a infraestructura (SQL Server, Redis, pipeline, secretos de configuración) | Alta — fuera del alcance del código del framework; depende de controles de infraestructura no cubiertos aquí. |
| Servicio interno (futuro) | Comunicación entre bounded contexts extraídos (Fase 9) o publicador/consumidor de eventos (Fase 3, Kafka) | No existe todavía en runtime — ADR 0005 en `Proposed`; se modela como amenaza futura. |
| Atacante externo no autenticado | Cualquier tráfico HTTP entrante sin credenciales válidas | Ninguna — debe ser rechazado por defecto. |
| Atacante con cuenta válida de un tenant (insider / cuenta comprometida) | Usuario autenticado que intenta acceder a datos de otro tenant o escalar privilegios | Ninguna fuera de su propio alcance de permisos y tenant. |
| Colaborador/PR externo o dependencia comprometida | Contribuye código o una dependencia NuGet que entra al build | Ninguna hasta pasar revisión — hoy sin SAST/SCA automatizado (brecha #7 de `docs/inventario-tecnico.md`). |

---

## 3. Fronteras de confianza

```
[Atacante / Internet]
        │  (1) Cliente ↔ API HTTP
        ▼
[Sample.Api / API consumidora] ── usa ──> [Shared.Infrastructure.Web: GlobalExceptionHandler, ToOkOrProblem]
        │  (2) API ↔ Identity/JWT (Shared.Infrastructure.Security)
        │  (3) API ↔ resolución de tenant (ITenantProvider — implementación real NO existe en el framework hoy)
        ▼
[Shared.Application: MediatR pipeline, TransactionBehavior] ── (4) Query/Command ↔ persistencia
        ▼
[Shared.Infrastructure.Persistence: MultiTenantDbContext] ── (5) API/App ↔ SQL Server
        ▼
[SQL Server — datos de todos los tenants, base compartida con discriminador TenantId]

[Shared.Infrastructure.Caching: HybridCache] ── (6) App ↔ Redis (L2) — fuera de la ruta de autorización, nunca fuente de verdad

[Repositorio Git / PR] ── (7) Colaborador ↔ código fuente
[GitHub Actions] ── (8) CI ↔ pipeline (build, test-unit, test-integration) ── (9) CI ↔ Testcontainers (SQL Server/Redis efímeros)

[Futuro, Proposed] [Servicio] ── (10) Servicio ↔ Kafka (ADR 0005) — no existe en runtime hoy
[Futuro, Proposed] [Cliente] ── (11) Cliente ↔ Gateway/BFF (ADR 0007) — no existe en runtime hoy
[Futuro, Proposed] [API] ── (12) API ↔ IdP externo OIDC (ADR 0004) — no existe en runtime hoy, hoy la frontera (2) es JWT propio
```

Cada número de frontera se referencia en la tabla de amenazas de la sección 4.

---

## 4. Amenazas (STRIDE) y mitigaciones

Convención de la columna "Mitigación": **Existente** (ya implementada y verificable en código/CI hoy), **Planeada** (ligada a una fase/tarea concreta del Plan Maestro), **Hueco sin plan** (riesgo real detectado en esta tarea sin tarea asignada todavía — candidato a `docs/risk-register.md`, F0-12).

### 4.1 Spoofing (suplantación de identidad)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| S1 | Forjar un JWT válido si `JwtOptions.SecretKey` se filtra (config en texto plano, log, repo) | (2) | `JwtTokenGenerator.cs:37-39` firma con `SymmetricSecurityKey` derivada directamente de `SecretKey`; no hay rotación de clave ni JWKS | **Hueco sin plan.** Existente: el repo no commitea el valor real (`appsettings.json` con placeholders vacíos). Planeada: ADR 0004 (OIDC/OAuth2 con IdP externo, Fase 2) reemplaza la firma HMAC simétrica propia por firma/validación estándar del IdP con rotación de claves. Mientras tanto, la gestión del secreto depende enteramente del proyecto consumidor (no hay guía de secret manager en el framework) — el propio Plan Maestro (sección 13) exige aprobación humana para elegir el proveedor de secretos/KMS, decisión aún no tomada. |
| S2 | Suplantar a otro tenant si la resolución de `TenantId` confía en un dato controlable por el cliente (p. ej. un header HTTP sin validar) | (3) | El framework **no provee** una implementación productiva de `ITenantProvider` que resuelva el tenant desde el JWT validado — solo existe `NullTenantProvider` (deshabilita multi-tenancy, `src/Shared.Infrastructure.Persistence/MultiTenancy/NullTenantProvider.cs`) registrado por `TryAddScoped` en `PersistenceServiceCollectionExtensions.cs:28`, y `FakeTenantProvider` en tests. La responsabilidad de implementar `ITenantProvider` de forma segura recae enteramente en cada proyecto consumidor | **Hueco sin plan.** No hay guía documentada en `docs/convenciones.md` sobre cómo implementar `ITenantProvider` de forma segura (p. ej. "el TenantId debe salir de un claim firmado del JWT, nunca de un header o query string sin validar"). Riesgo real de que un proyecto consumidor implemente esto de forma insegura por falta de guía. Se recomienda como hallazgo de esta tarea documentar esta guía en `docs/convenciones.md` y/o convertirla en tarea de Fase 1/2. |
| S3 | Reutilizar un refresh token robado tras rotación (replay) | (2) | `RefreshToken.cs` modela `RevokedAtUtc`/`ExpiresAtUtc`/`IsActive`, pero el modelo por sí solo no garantiza que el flujo de emisión revoque el token anterior en cada uso (rotación con detección de reuso) — no se encontró un `RefreshTokenService`/handler que implemente ese flujo en `src/Shared.Infrastructure.Security` | **Hueco sin plan.** La entidad soporta revocación, pero no se confirmó lógica de rotación con detección de reuso en el código relevado. Riesgo si un consumidor implementa el intercambio de refresh token sin revocar el anterior. |
| S4 | Suplantación de identidad entre servicios cuando existan servicios extraídos (Fase 9) sin mTLS/workload identity | (10, futuro) | ADR 0004 fija "Client Credentials/workload identity y mTLS cuando existan servicios separados" como dirección, no como hecho | **Planeada** — Fase 2 (F2-01 a F2-06) y Fase 9 (extracción de microservicios). No aplica hoy porque no hay servicios separados en runtime. |

### 4.2 Tampering (alteración de datos)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| T1 | Modificar `TenantId` de una entidad ya creada por otro tenant en una operación de escritura mal construida | (4, 5) | `TenantSaveChangesInterceptor.cs:32-38` solo asigna `TenantId` en estado `Added` cuando está `Guid.Empty`; no impide que un `Update` explícito cambie el `TenantId` de una fila existente si el handler lo permite | **Existente parcial.** El filtro global de lectura (`MultiTenancyModelConfigurator.ApplyGlobalFilters`) evita que una query vea filas de otro tenant, pero no hay una guarda explícita contra reasignar `TenantId` en un `Update`. **Hueco sin plan**: no se encontró un test de "un handler no puede reasignar TenantId de una entidad existente" en `tests/Shared.Infrastructure.Persistence.Tests`. Candidato a test de aislamiento de Fase 1 (el propio ADR 0003 exige "pruebas de aislamiento obligatorias" antes de cerrar el rediseño de tenancy). |
| T2 | Bypass del filtro global de tenancy por una consulta que use `IQueryable` expuesto o SQL crudo | (4, 5) | Regla dura 3 de `docs/convenciones.md` prohíbe exponer `IQueryable`; no hay todavía un architecture test (NetArchTest o similar) que lo verifique automáticamente — confirmado como brecha en `docs/inventario-tecnico.md` sección 5 ("Architecture tests... no existen") | **Existente (convención) / Hueco de enforcement automático.** Depende hoy de revisión de código manual, no de un gate de CI. Planeada: architecture tests es gate exigido por la sección 7.3 del Plan Maestro, sin tarea de Fase 0 asignada explícitamente — candidato a Fase 1/7.3. |
| T3 | Alterar el pipeline CI (workflow YAML) o inyectar una dependencia maliciosa en un PR | (7, 8) | `.github/workflows/ci.yml` no tiene protección de branch documentada en el repo (fuera del alcance de este documento verificar configuración de GitHub), ni SAST/SCA/secret scanning (brecha #7 de inventario) | **Hueco sin plan** para SAST/SCA/secret scanning — ya identificado como brecha en `docs/inventario-tecnico.md`, sin tarea de Fase 0 que lo cierre (candidato a Fase 7-8 según el propio inventario). Protección de branch/CODEOWNERS es configuración de plataforma GitHub, fuera del código del repo. |
| T4 | Modificar datos vía cache envejecido/inconsistente usado como si fuera fuente de verdad | (6) | Regla dura del Plan Maestro sección 3.2 y ADR 0006: "cache nunca es fuente de verdad para saldos, ledger, auditoría o transacciones" | **Existente (regla documentada) / Hueco de enforcement automático.** ADR 0006 ya señala este riesgo y propone "architecture test que detecte lecturas de cache en rutas de saldo/ledger/auditoría" como mitigación pendiente, no implementada todavía. |

### 4.3 Repudiation (repudio / falta de trazabilidad)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| R1 | Un administrador de tenant niega haber realizado una acción sensible (borrado, cambio de permisos) sin evidencia inmutable | (1, 4) | `IAuditedEntity` existe como interfaz de dominio (`docs/convenciones.md` regla 5) pero el propio `docs/architecture-principles.md` (sección 4, "Brecha actual") confirma: "no hay todavía auditoría inmutable (F2-15 a F2-20)" | **Planeada** — Fase 2 (F2-15 a F2-20). Hoy la auditoría de campos (quién/cuándo) existe por reflexión, pero no hay garantía de inmutabilidad (append-only, protección contra alteración/borrado del registro de auditoría). |
| R2 | Acciones de autorización denegadas/concedidas sin registro correlacionable (quién pidió qué permiso y con qué resultado) | (1, 4) | `PermissionAuthorizationHandler.cs` no emite ningún log/evento cuando deniega o concede una autorización — solo llama `context.Succeed`/no hace nada si falla | **Hueco sin plan.** No hay traza explícita de decisiones de autorización denegadas, lo que dificulta detectar intentos de escalamiento de privilegios. Observabilidad general existe (`Shared.Infrastructure.Observability`, OpenTelemetry/Serilog) pero no está conectada específicamente a este punto según el código relevado. |

### 4.4 Information Disclosure (fuga de información)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| I1 | Fuga de datos entre tenants si una entidad nueva olvida implementar `ITenantEntity` | (4, 5) | El filtro global se aplica "por reflexión" sobre entidades que implementan la interfaz (`docs/architecture-principles.md`, sección 3); una entidad que la omite queda sin filtro, sin error explícito en tiempo de diseño | **Existente (mecanismo) / Hueco de enforcement.** No hay un architecture test que falle el build si una entidad con datos de negocio "olvida" `ITenantEntity` (es una decisión de diseño manual). Este es el riesgo de aislamiento entre tenants más citado explícitamente por el propio ADR 0003 y por `docs/architecture-principles.md` sección 3. |
| I2 | Filtración de detalles internos (stack trace, tipo de excepción) al cliente en un error 500 | (1) | `GlobalExceptionHandler.cs:26-34` devuelve un `ProblemDetails` genérico ("Ha ocurrido un error inesperado") con `traceId`, sin exponer el mensaje de la excepción ni el stack trace | **Existente.** Correctamente mitigado hoy: el detalle completo va solo al log (`logger.LogError`), no a la respuesta HTTP. |
| I3 | Fuga de secretos (cadena de conexión SQL Server, `JwtOptions.SecretKey`) por quedar commiteados en `appsettings.json` | (5, A3, A5) | `samples/Sample.Api/appsettings.json` tiene `ConnectionStrings:Default` vacío; no se encontró un valor real de `SecretKey` commiteado en los archivos relevados | **Existente (buena práctica actual, sin garantía automatizada).** No hay secret scanning en CI (brecha ya documentada en `docs/inventario-tecnico.md` sección 5) que impida que un futuro commit introduzca un secreto real por error humano. **Hueco sin plan** de enforcement automático (gitleaks/trufflehog u equivalente en CI). |
| I4 | Enumeración de usuarios/tenants mediante mensajes de error diferenciados en login/permisos | (2) | `PermissionAuthorizationHandler.cs:15-18` retorna silenciosamente si el claim de usuario no es válido (no revela información), pero no se relevó el endpoint de login/autenticación en sí (no existe en `Shared.Infrastructure.Security` un controlador de login concreto, solo `JwtTokenGenerator`/`IJwtTokenGenerator`) | **No verificable en esta tarea** — el flujo de login concreto (con sus mensajes de error) se implementa en cada proyecto consumidor, no en el framework. Recomendación: documentar en `docs/convenciones.md` que los mensajes de error de autenticación no deben diferenciar "usuario no existe" de "contraseña incorrecta". |

### 4.5 Denial of Service (denegación de servicio)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| D1 | Un tenant consume recursos desproporcionados (CPU, conexiones SQL) y degrada el servicio para el resto, dado que hoy es infraestructura compartida sin aislamiento por tenant | (5, 9) | ADR 0003 confirma que el modelo actual es "base de datos compartida con discriminador TenantId"; no hay throttling ni cuotas por tenant relevadas en el código | **Hueco sin plan.** El propio ADR 0003 ya señala esto como parte del rediseño pendiente de Fase 1 (benchmark de multi-tenancy bajo carga), pero no como control de DoS explícito. Sin rate limiting/cuota por tenant documentada. |
| D2 | Ausencia de rate limiting en los endpoints HTTP expuestos por `Shared.Infrastructure.Web`/`Sample.Api` | (1) | No se encontró middleware de rate limiting (`Microsoft.AspNetCore.RateLimiting` u otro) en `Shared.Infrastructure.Web` ni en `Sample.Api` | **Hueco sin plan.** No hay tarea explícita del Plan Maestro relevada para esto en Fase 0-1; candidato a Fase 2 (junto con el gateway YARP, ADR 0007, que es el punto natural para centralizar rate limiting). |
| D3 | Token de acceso JWT con expiración larga aumenta la ventana de abuso si se compromete | (2) | `JwtOptions.AccessTokenExpirationMinutes` default 15 minutos (`JwtOptions.cs:13`) | **Existente.** Valor por defecto razonable (15 min); depende de que el consumidor no lo sobreescriba con un valor excesivo. |

### 4.6 Elevation of Privilege (escalamiento de privilegios)

| # | Amenaza | Frontera | Evidencia | Mitigación |
|---|---|---|---|---|
| E1 | Un usuario obtiene un permiso no asignado explícitamente por un bug en la resolución de permisos vía roles | (2, 4) | `PermissionService.cs` calcula permisos únicamente a partir de `roleManager.GetClaimsAsync(role)` con `PermissionClaimTypes.Permission` — modelo simple y auditable, sin jerarquía de roles implícita ni permisos heredados que puedan generar ambigüedad | **Existente.** El modelo es explícito (permiso = claim en rol asignado al usuario), sin lógica implícita de herencia que pueda causar sobre-otorgamiento. |
| E2 | Un endpoint nuevo olvida `[RequirePermission]`/`.RequireAuthorization(...)` y queda accesible sin autorización (falla a "permitir" en vez de "denegar") | (1) | Regla dura de `docs/convenciones.md` ("proteger un endpoint por permiso") depende de que el desarrollador la aplique explícitamente por endpoint; no se relevó un mecanismo de *default deny* global (p. ej. `RequireAuthorization()` global con excepciones explícitas para endpoints públicos) en `Shared.Infrastructure.Web` | **Hueco sin plan.** El principio de "default deny" está declarado en `docs/architecture-principles.md` sección 3, pero no se confirmó un fallback global en código que lo haga estructural en vez de depender de disciplina por endpoint. Candidato a architecture test o a un filtro/convención global en Fase 1-2. |
| E3 | Elevación de privilegios entre bounded contexts si un futuro servicio extraído confía en headers/claims propagados sin volver a validar el token | (10/11, futuro) | No aplica hoy (no hay servicios extraídos ni gateway); ADR 0007 en `Proposed` | **Planeada** — a resolver en el diseño del gateway/BFF (F2-03) y en la extracción de microservicios (Fase 9), ambas con aprobación humana requerida (sección 13 del Plan Maestro) antes de habilitar tráfico productivo. |

---

## 5. Resumen de huecos sin plan asignado (candidatos a `docs/risk-register.md`, F0-12)

Estos son los hallazgos de esta tarea que **no** tienen todavía una tarea de fase que los cierre explícitamente y que se recomienda incorporar al registro de riesgos cuando se ejecute F0-12:

1. **S2** — Falta de guía documentada sobre cómo implementar `ITenantProvider` de forma segura (resolución de `TenantId` desde un claim JWT validado, nunca desde un header/parámetro controlado por el cliente). Impacto: fuga de datos entre tenants por implementación insegura en un proyecto consumidor.
2. **S3** — No se confirmó lógica de rotación de refresh token con detección de reuso en el código relevado.
3. **T1** — No hay guarda explícita ni test que impida reasignar `TenantId` de una entidad existente en un `Update`.
4. **T2 / I1** — No hay architecture test que haga cumplir automáticamente "toda entidad de negocio implementa `ITenantEntity`" ni "ningún repositorio expone `IQueryable`" (brecha ya señalada en `docs/inventario-tecnico.md` sección 5, sin tarea de Fase 0 asignada).
5. **T3 / I3** — Pipeline sin SAST, SCA, secret scanning, license scan ni SBOM (brecha ya documentada en `docs/inventario-tecnico.md` sección 5; sin tarea concreta de Fase 0 que lo cierre, solo referencias a F8-14/F10-04).
6. **R2** — Decisiones de autorización denegadas no se registran de forma correlacionable para detección de abuso.
7. **D1** — Sin control de cuota/aislamiento de recursos por tenant en la infraestructura compartida actual.
8. **D2** — Sin rate limiting en los endpoints HTTP del framework.
9. **E2** — Sin mecanismo estructural de *default deny* global (depende de disciplina manual por endpoint).
10. **I4** — Sin guía documentada sobre no diferenciar mensajes de error en autenticación (enumeración de usuarios).

Estos huecos **no bloquean** el resto del Plan Maestro (no son criterio de aceptación de F0-07), pero deben alimentar el registro de riesgos de F0-12 con probabilidad/impacto/owner, y varios (S2, T2/I1, E2) son candidatos naturales a resolverse junto con el rediseño de multi-tenancy y los architecture tests de Fase 1.

---

## 6. Amenazas explícitamente fuera de alcance de este documento

- **Seguridad de infraestructura física/red** (hardening de SQL Server/Redis, segmentación de red, WAF) — depende del entorno de despliegue del consumidor, no del código del framework.
- **Seguridad de la elección de IdP concreta, proveedor de secretos/KMS, o del proveedor de Kafka/gateway** — son decisiones de la sección 13 del Plan Maestro, requieren aprobación humana antes de evaluarse en detalle.
- **Amenazas a un gateway YARP o a Kafka en producción** — no existen en runtime hoy (ADR 0005/0007 en `Proposed`); se modelan aquí solo como amenazas futuras a alto nivel (S4, E3), y deberán re-modelarse en detalle cuando se construyan (Fase 2/3/4).

---

## Revisión

| Campo | Valor |
|---|---|
| Estado | Completado — pendiente de revisión de seguridad |
| Revisado por | Pendiente |
| Fecha de revisión | Pendiente |

Este documento pasa a considerarse aprobado únicamente cuando un responsable de seguridad identificado (Plan Maestro, sección "Precondiciones" de Fase 0) registre su revisión explícita, actualizando esta tabla. Hasta entonces, ningún hueco identificado en la sección 5 debe tratarse como cerrado ni como priorizado sin esa revisión.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — secciones 2, 3.2, 4, 7.3, 9 y 13.
- [`inventario-tecnico.md`](inventario-tecnico.md) — estado real del repositorio (F0-01), en particular sección 5 (brechas de pipeline).
- [`architecture-principles.md`](architecture-principles.md) — principio de seguridad (sección 3) y sus brechas declaradas.
- [`convenciones.md`](convenciones.md) — reglas duras vigentes (auditoría, tenancy, autorización).
- [`adr/0003-tenancy-multi-tenant-por-filtro-global.md`](adr/0003-tenancy-multi-tenant-por-filtro-global.md), [`adr/0004-identidad-idp-oidc-oauth2.md`](adr/0004-identidad-idp-oidc-oauth2.md), [`adr/0005-mensajeria-kafka.md`](adr/0005-mensajeria-kafka.md), [`adr/0006-cache-hybridcache-valkey-redis.md`](adr/0006-cache-hybridcache-valkey-redis.md), [`adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md).
