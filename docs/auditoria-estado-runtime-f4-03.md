# Auditoría de estado local y sticky sessions — F4-03 (Stateless)

**Tarea:** F4-03 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07 (actualizado el mismo día tras cerrar el hallazgo de la sección 3).
**Objetivo:** identificar y, cuando el cambio sea mínimo y acotado, corregir cualquier estado que impida que un pod de `samples/Sample.Api` (o de un futuro consumidor real construido sobre el framework) sea reemplazable sin pérdida de funcionalidad ni de datos.

**Estado del criterio "Pod reemplazable":** cumplido y **verificado con dos instancias reales** para el único host ejecutable actual (`samples/Sample.Api`). La auditoría inicial encontró un hallazgo real de estado local latente en un módulo ya enviado (`Shared.Infrastructure.Security`, Data Protection) que no afectaba a `Sample.Api` (no usa OIDC/BFF), pero rompía la reemplazabilidad del pod para cualquier consumidor real que sí lo use con más de una réplica — ver sección 3. Ese hallazgo se **corrigió** en esta misma tarea (sección 3.5): el key ring de Data Protection ahora se persiste en Redis cuando `Caching:RedisConnectionString` está configurado, mismo backend ya aprobado que usa `AddSharedCaching`, verificado con un test de integración contra Redis real. Con eso, esta tarea se cierra como **Completada**: el runtime auditable hoy es stateless y quedó demostrado, y el único hallazgo de diseño encontrado quedó corregido (no solo documentado) dentro del alcance de esta tarea.

---

## 1. Alcance de la auditoría

Se revisó cada categoría del enunciado de la tarea contra el código real de `src/` y `samples/Sample.Api` (no se asumió estructura de memoria):

| Categoría | Resultado |
|---|---|
| `IMemoryCache`/estado en memoria de proceso como fuente de verdad | Sin hallazgos — no hay ningún uso de `IMemoryCache` en el repositorio. El único cache en memoria (`HybridCache` L1) está documentado explícitamente como no-fuente-de-verdad (convenciones regla dura 14) y siempre respaldado por SQL Server/Redis para lo que sí importa (saldos/ledger/auditoría nunca pasan por cache). |
| Sesión in-process / `ISession`/`UseSession` | Sin hallazgos — no se usa `Microsoft.AspNetCore.Session` en ningún proyecto. La única "sesión" del framework es la del BFF (`IBffSessionStore`), y está implementada sobre `IDistributedCache` desde su diseño original (F2-03), no en memoria de proceso — ver hallazgo de la sección 3 sobre el cifrado de esa sesión. |
| Archivos locales (uploads, temp, logs persistentes) | Sin hallazgos — ningún proyecto usa `IFormFile`, `wwwroot`, ni escribe archivos de negocio a disco. Serilog (`UseSharedSerilog`) solo registra un sink `Console` por defecto; cualquier sink de archivo queda a discreción del `appsettings.json` del consumidor, no es un default del framework. |
| Sticky sessions explícitas/implícitas (cookies de afinidad, IDs atados a instancia) | Sin hallazgos directos en el mecanismo de sesión declarado — pero ver hallazgo de la sección 3: el cifrado de esa sesión con Data Protection sin key ring compartido produce el mismo efecto práctico que una sticky session (una sesión creada en un pod puede volverse ilegible en otro). |
| Background workers/schedulers no recuperables (Quartz sin `JobStore` persistente) | **Hallazgo confirmado, diagnóstico únicamente (alcance reservado a F4-11)** — ver sección 4. |
| Estructura Kubernetes (volúmenes de estado, afinidad de nodo) | Sin hallazgos — `k8s/sample-api/base/deployment.yaml` ya declara explícitamente "sin volúmenes de estado local requeridos, sin affinity a un nodo específico" (comentario dejado por F4-02) y no monta ningún `PersistentVolumeClaim`. |

---

## 2. Verificación práctica: dos instancias reales contra el mismo SQL Server

Se ejecutó el escenario de prueba pedido por la tarea usando el binario real de `samples/Sample.Api` (build `Release`, mismo artefacto que empaqueta `docker/sample-api/Dockerfile`), **no un mock**:

1. SQL Server 2022 real en un contenedor Docker (`mcr.microsoft.com/mssql/server:2022-latest`), una única base de datos compartida.
2. Dos instancias del proceso `Sample.Api.dll` arrancadas por separado (puertos `15501`/`15502`), ambas apuntando a la misma cadena de conexión — el equivalente a 2 réplicas de un mismo `Deployment` contra el mismo SQL Server, sin usar ningún mecanismo de afinidad.
3. Ambas pasaron `/health/ready` de forma independiente (`EnsureCreatedAsync` concurrente en el arranque de ambas instancias no falló contra una base vacía compartida).

### 2.1 Idempotencia cruzada entre instancias (simula "el cliente reintenta contra un pod distinto")

```
POST http://127.0.0.1:15501/api/v1/productos/  Idempotency-Key: test-key-cross-instance-001  → 201, Guid 6b7217ea-98ff-4fef-9be3-8c71ecc34346
POST http://127.0.0.1:15502/api/v1/productos/  Idempotency-Key: test-key-cross-instance-001  → 201, MISMO Guid 6b7217ea-98ff-4fef-9be3-8c71ecc34346
```

`SELECT COUNT(*) FROM Productos WHERE Nombre='ProductoX'` → **1** fila. El registro de idempotencia (tabla `IdempotencyKey`, F1-22) vive en SQL Server, no en memoria del proceso — el pod que atiende el reintento es indistinto.

### 2.2 Muerte abrupta de un pod a mitad de un lote de operaciones concurrentes

Se lanzaron 60 `POST` concurrentes contra la instancia A (puerto 15501, PID real verificado con `netstat`/`tasklist`, no el PID reportado por el subshell de Git Bash) y, a los ~400 ms, se mató el proceso con `taskkill /F` (equivalente a `SIGKILL`/eliminación forzada de un pod) mientras las 60 requests seguían en vuelo.

Resultado:

- 29 respuestas HTTP 201 recibidas por el cliente antes del corte; 31 conexiones cortadas (código `000`, TCP reset) — comportamiento esperado, ningún mecanismo de sesión intentó "recuperar" la conexión rota.
- `SELECT COUNT(*) FROM Productos WHERE Nombre LIKE 'KillTest%'` → **36** filas (7 más que las 29 respuestas recibidas: esas 7 alcanzaron a hacer `COMMIT` en SQL Server antes de que el proceso muriera, pero la respuesta HTTP nunca llegó al cliente — comportamiento esperado de "ejecutado al menos una vez, confirmación perdida", no un problema de esta tarea).
- `SELECT Nombre, COUNT(*) FROM Productos GROUP BY Nombre HAVING COUNT(*) > 1` → **0 filas**: ninguna operación se duplicó.
- Reconsultado inmediatamente después de la caída: `sys.dm_tran_session_transactions`/`sys.dm_exec_requests` → **0 transacciones activas/bloqueadas** atribuibles a la conexión muerta. SQL Server revirtió automáticamente cualquier transacción incompleta de la conexión cortada; no quedó ningún lock huérfano.
- La instancia B, que no participó en el lote, siguió sirviendo `/health/ready` (200) y aceptando nuevas escrituras sin ninguna acción manual.
- Se identificó una de las 31 requests que el cliente vio fallar y que **no** llegó a comprometerse en SQL Server (`KillTest40`, verificado con `SELECT` — 0 filas antes del reintento). Se reintentó la misma `Idempotency-Key` contra la instancia B superviviente: primera llamada crea el recurso (201), segunda llamada con la misma clave devuelve el mismo `Guid` sin duplicar la fila (`SELECT COUNT(*)` → 1).

**Conclusión de la prueba:** matar un pod a mitad de una operación no deja el sistema en un estado inconsistente ni pierde datos — cualquier operación que alcanzó a comprometerse en SQL Server queda íntegra y sin duplicados; cualquier operación que no alcanzó a comprometerse puede reintentarse de forma segura e idempotente contra cualquier otra instancia superviviente, sin necesidad de que el reintento vuelva al mismo pod.

Contenedor y procesos de la prueba se destruyeron al finalizar (`docker rm -f`, `taskkill /F` de ambas instancias) — no queda infraestructura de prueba residual.

---

## 3. Hallazgo real: Data Protection sin key ring compartido entre pods (`Shared.Infrastructure.Security`)

**Severidad:** Alta si un consumidor real despliega el BFF (F2-03) o el flujo Authorization Code + PKCE (F2-02) con más de una réplica. **No afecta a `samples/Sample.Api` hoy** (no usa `AddSharedOidcAuthorizationCodeFlow` ni `AddSharedBffSessionStore` — confirmado revisando `InfrastructureModule.cs`, único módulo de infraestructura del piloto).

### 3.1 Qué se encontró

`AddDataProtection()` se invoca en dos lugares sin persistir el key ring en un backend compartido entre instancias ni fijar `SetApplicationName`:

- `src/Shared.Infrastructure.Security/Oidc/AuthorizationCode/OidcAuthorizationCodeServiceCollectionExtensions.cs:64` — protege la cookie de correlación del Authorization Code Flow (`state`/`code_verifier`/`nonce`/`returnUrl`, vía `OidcAuthorizationCodeStateProtector`).
- `src/Shared.Infrastructure.Security/Oidc/Bff/BffSessionServiceCollectionExtensions.cs:40` — protege el valor de la sesión BFF antes de guardarlo en `IDistributedCache` (`DistributedCacheBffSessionStore`).

Sin `.PersistKeysToStackExchangeRedis(...)`/`.PersistKeysToDbContext(...)`/`.PersistKeysToAzureBlobStorage(...)` (o equivalente) configurado explícitamente, ASP.NET Core persiste el key ring de Data Protection en el almacenamiento por defecto del proceso (perfil de usuario/registro en Windows; `%HOME%/.aspnet/DataProtection-Keys` en Linux/contenedores sin volumen persistente) — **no compartido entre réplicas** y **no sobrevive al reemplazo de un pod**.

### 3.2 Por qué rompe "pod reemplazable"

- **Flujo Authorization Code + PKCE:** un usuario inicia login contra el pod A, que cifra la cookie de correlación con la clave del pod A. Si el callback del IdP (redirect posterior) llega a un pod B distinto — comportamiento normal de un balanceador sin afinidad, exactamente lo que este Deployment declara no requerir — el pod B no puede descifrar la cookie con su propia clave y el login falla. Esto obliga de facto a una sticky session no declarada como tal, contradiciendo el criterio de aceptación de esta tarea.
- **Sesión BFF:** `DistributedCacheBffSessionStore` ya usa `IDistributedCache` correctamente (Redis en producción, memoria de proceso solo como fallback de una sola instancia documentado en el propio código) — el **contenido** de la sesión sí está compartido entre pods. Pero ese contenido se cifra con `IDataProtector` antes de guardarse: si el pod A y el pod B tienen key rings distintos, el pod B lee el valor correcto de Redis pero no puede descifrarlo. `DistributedCacheBffSessionStore.GetAsync` atrapa `CryptographicException` y lo trata como "sesión ausente" (silencioso, ver comentario en el propio código) — el usuario queda deslogueado según a qué pod caiga cada request, sin ningún error visible que lo explique.

### 3.3 Por qué el hallazgo inicial no se corrigió en el primer paso de esta auditoría

Siguiendo la sección 3.2 del Plan Maestro ("cambios pequeños y cohesionados", "no mezclar refactorizaciones ajenas a la tarea"), la corrección no era un parche de una línea sin analizar primero: requería decidir el backend de persistencia del key ring, evaluar la dependencia nueva (licencia/mantenimiento/compatibilidad) antes de agregarla, y no tocar dos módulos ya enviados (F2-02/F2-03) sin actualizar su documentación ni agregar cobertura de test. Por eso el primer paso de esta auditoría (ver historial de este documento) dejó el hallazgo solo documentado, con una recomendación explícita para una tarea de seguimiento. Esa recomendación se ejecutó a continuación, en el mismo día y como parte de esta misma tarea F4-03 (instrucción explícita: aplicar la opción técnica recomendada y cerrar, en vez de dejarla pendiente) — ver sección 3.5.

### 3.4 Recomendación original (ya ejecutada — ver 3.5)

La recomendación original era una tarea dedicada que:

1. Extendiera `AddSharedBffSessionStore`/`AddSharedOidcAuthorizationCodeFlow` para persistir el key ring de Data Protection en Redis cuando `Caching:RedisConnectionString` esté configurado (mismo patrón condicional que ya usa `AddSharedCaching`), manteniendo el fallback actual en memoria solo para una única instancia/desarrollo.
2. Documentara la nueva dependencia (licencia, mantenimiento) en `docs/politica-dependencias.md`.
3. Agregara un test de integración que confirme que dos instancias con el mismo backend de persistencia de claves comparten el mismo key ring (una cookie de correlación protegida por una es legible por la otra).
4. Actualizara `docs/politica-criptografica.md` con la guía resultante.

Los cuatro puntos se implementaron — ver sección 3.5.

### 3.5 Corrección implementada

- **Código:** `src/Shared.Infrastructure.Security/Oidc/SharedDataProtectionServiceCollectionExtensions.cs` (nuevo) — método interno `AddSharedDataProtectionWithSharedKeyRing`, llamado desde `OidcAuthorizationCodeServiceCollectionExtensions.AddSharedOidcAuthorizationCodeFlow` y `BffSessionServiceCollectionExtensions.AddSharedBffSessionStore` en vez de invocar `services.AddDataProtection()` directamente. Fija un `ApplicationName` estable (`OpenTelemetry:ServiceName` si está configurado, o un valor constante por defecto — nunca varía entre réplicas de un mismo despliegue) y, cuando `Caching:RedisConnectionString` está configurado, persiste el key ring con `PersistKeysToStackExchangeRedis` sobre una conexión Redis propia (lazy, no bloquea el arranque). Sin Redis configurado, se mantiene el comportamiento anterior (almacenamiento por defecto del proceso) como fallback documentado de una sola instancia/desarrollo — el riesgo residual queda explícito en R-TEC-08.
- **Dependencia:** `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` 10.0.11 agregada a `src/Shared.Infrastructure.Security/Shared.Infrastructure.Security.csproj` — paquete first-party de ASP.NET Core (MIT, mismo ciclo de versionado que las demás dependencias `Microsoft.AspNetCore.*` ya usadas por este proyecto), evaluado y registrado en `docs/politica-dependencias.md` sección 5.2. No dispara el proceso de excepción de dependencias de terceros de la sección 3.2 del Plan Maestro.
- **Test:** `tests/Shared.Infrastructure.Security.Tests/Integration/SharedDataProtectionRedisIntegrationTests.cs` (nuevo, Testcontainers Redis real, mismo patrón que `CachedPermissionServiceRedisIntegrationTests`/`HybridCacheRedisIntegrationTests`) — construye dos `ServiceProvider` independientes con `AddSharedOidcAuthorizationCodeFlow` apuntando al mismo Redis, protege un `OidcAuthorizationCodeState` con la instancia A y confirma que la instancia B lo descifra correctamente (mismo key ring compartido vía Redis).
- **Documentación:** `docs/politica-criptografica.md` (sección "Decisiones de diseño por caso de uso") y `docs/risk-register.md` (R-TEC-08 marcado como mitigado) actualizados.
- **Verificación:** `dotnet build BitCode.Framework.slnx` sin errores; `dotnet test tests/Shared.Infrastructure.Security.Tests` (433/433) y `dotnet test tests/Shared.Infrastructure.Web.Tests` (49/49) sin regresiones.

Este hallazgo se refleja también en `docs/risk-register.md` (R-TEC-08, ahora "Mitigado (F4-03)").

---

## 4. Hallazgo diagnosticado, no implementado (alcance ya reservado a F4-11): Quartz sin `JobStore` persistente

`src/Shared.Infrastructure.BackgroundJobs/BackgroundJobsServiceCollectionExtensions.cs` registra Quartz.NET (`services.AddQuartz(configureJobs)`) sin llamar a `UsePersistentStore(...)` — Quartz usa por tanto el `RAMJobStore` por defecto: triggers, estado de ejecución y misfires viven únicamente en la memoria del proceso.

Efecto sobre "pod reemplazable": si un pod que ejecuta un `IJob` muere a mitad de la ejecución, el estado de ese job (y de cualquier otro programado) se pierde sin ningún mecanismo de recuperación; con más de una réplica, cada instancia programaría y dispararía el mismo job de forma independiente (duplicación de efectos), porque `RAMJobStore` no coordina entre procesos.

Esto es exactamente el trabajo ya reservado explícitamente a **F4-11 (Quartz HA)** del Plan Maestro ("Persistent JobStore, cluster, misfire e idempotencia" → "Un job lógico no duplica efectos") — la propia tarea F4-03 instruye no implementarlo acá si es trabajo grande, solo diagnosticarlo. No se implementa ningún cambio en `AddSharedBackgroundJobs` en esta tarea.

Impacto real hoy: **ninguno sobre `samples/Sample.Api`**, que no llama a `AddSharedBackgroundJobs` ni registra ningún `IJob` (confirmado revisando `InfrastructureModule.cs`).

---

## 5. Observación menor (sin acción): arranque concurrente de `EnsureCreatedAsync`

`samples/Sample.Api/Program.cs` usa `Database.EnsureCreatedAsync()` en el arranque en vez de migraciones EF Core, con el comentario explícito "proyecto piloto/demo, no un consumidor real". En la prueba de la sección 2, dos instancias arrancaron contra la misma base vacía sin fallar, pero `EnsureCreatedAsync` no usa ningún lock distribuido — dos pods arrancando en el mismo instante contra un esquema todavía inexistente podrían, en el peor caso, competir por crear el mismo objeto. No es un problema de "estado local del pod" (no depende de disco/memoria de la instancia), es un riesgo del mecanismo de bootstrap de esquema, ya señalado como atajo de demo en el propio código y fuera del alcance textual de F4-03. Un consumidor real de producción usa migraciones EF Core aplicadas fuera del ciclo de vida del pod (pipeline de despliegue), no `EnsureCreated` en el arranque — no se propone ningún cambio en `samples/Sample.Api` porque alteraría el propósito declarado del proyecto piloto.

---

## 6. Checklist contra el criterio de aceptación ("Pod reemplazable")

| Ítem | Estado |
|---|---|
| Sin `IMemoryCache`/sesión in-process como fuente de verdad | Cumplido |
| Sin archivos locales requeridos para funcionalidad de negocio | Cumplido |
| Sin sticky sessions explícitas en el runtime auditado (`Sample.Api`) | Cumplido |
| Verificación práctica con 2 instancias reales contra el mismo SQL Server, incluida muerte de un pod a mitad de una operación | Cumplido — sección 2 |
| Ningún estado in-memory de background workers sin plan de resolución | Diagnosticado, resolución diferida a F4-11 (ya reservada explícitamente en el Plan Maestro) |
| Sin mecanismo latente que reintroduzca afinidad de sesión en un consumidor real que use módulos ya enviados | **Cumplido** — hallazgo de Data Protection (sección 3) corregido (sección 3.5): key ring compartido vía Redis cuando `Caching:RedisConnectionString` está configurado, verificado con test de integración contra Redis real |

---

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 4, fila F4-03 y F4-11.
- [`convenciones.md`](convenciones.md) — reglas duras 14 (cache no es fuente de verdad) y 4/18 (idempotencia/outbox/inbox sobre SQL Server, base de la prueba de la sección 2).
- [`politica-criptografica.md`](politica-criptografica.md) — uso actual de `IDataProtectionProvider` para el estado de correlación OIDC y la sesión BFF, actualizado con la persistencia del key ring en Redis (sección 3.5).
- [`politica-dependencias.md`](politica-dependencias.md) sección 5.2 — evaluación de `Microsoft.AspNetCore.DataProtection.StackExchangeRedis`.
- [`risk-register.md`](risk-register.md) — R-TEC-08, marcado como mitigado tras la corrección de la sección 3.5.
- `src/Shared.Infrastructure.Security/Oidc/SharedDataProtectionServiceCollectionExtensions.cs` — implementación de la corrección.
- `tests/Shared.Infrastructure.Security.Tests/Integration/SharedDataProtectionRedisIntegrationTests.cs` — verificación con Redis real.
- `k8s/sample-api/base/deployment.yaml` — comentario F4-03 ya presente sobre ausencia de volúmenes de estado local y de afinidad de nodo (F4-02).
