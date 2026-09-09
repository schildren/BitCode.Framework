# Routing regional del Gateway — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-03 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md)) y F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — mapa de ownership tenant → región, `IRegionalOwnershipResolver`/`ICurrentRegionProvider`).
**Estado:** "Política global" de routing implementada en `BitCode.Gateway` (YARP), verificada con pruebas de integración reales (Gateway real vía Kestrel/TestServer + backend HTTP real). Sin infraestructura multi-región física ni Kubernetes multi-clúster disponible en este entorno (un solo host de desarrollo) — las regiones se simulan vía configuración, ver sección 4.

---

## 1. Por qué el Gateway, y no un middleware de MediatR nuevo

`RegionalOwnershipBehavior` (F5-02, Shared.Application) ya rechaza un comando marcado con `IRegionalCommand` si se ejecuta fuera de su región propietaria — pero actúa **dentro** del proceso backend, después de que el request ya cruzó la red hasta esa instancia. Eso es la última línea de defensa (nunca se completa una escritura en la región equivocada), no el mecanismo de enrutamiento.

`BitCode.Gateway` (YARP, F4-08) es el único punto de entrada real del framework — es el lugar natural para decidir, **antes de proxyar**, si esta instancia del Gateway debería atender el request o no. Esto es "afinidad" (dirigir el tráfico hacia la región propietaria) más "failover controlado" (rechazo explícito, con la región correcta indicada, en vez de un procesamiento silencioso en la región incorrecta).

## 2. Mecanismo de código

### 2.1 Reutilización de F5-02 (no duplicación)

El routing regional del Gateway reutiliza integralmente el mecanismo de ownership de F5-02 — no define un segundo modelo de "¿quién es el propietario de este tenant?":

| Contrato/servicio | Origen | Rol en el Gateway |
|---|---|---|
| `RegionId`, `ITenantRegionMapStore`, `IRegionalOwnershipResolver`, `ICurrentRegionProvider` | `Shared.Domain.MultiTenancy` (F5-02) | Sin cambios — el Gateway consume las mismas interfaces que `RegionalOwnershipBehavior`. |
| `TenantRegionOwnershipResolver`, `AddRegionalOwnership(RegionId)` | `Shared.Infrastructure.Persistence.MultiTenancy.Regions` (F5-02) | Reutilizados tal cual — `AddGatewayRegionalRouting` (ver 2.2) llama a `AddRegionalOwnership` en vez de reimplementar la resolución. |
| `InMemoryTenantRegionMapStore` (F5-02) | `Shared.Infrastructure.Persistence.MultiTenancy.Regions` | **No reutilizado** en el Gateway — ver 2.2, `ConfigurationTenantRegionMapStore`. |

### 2.2 Componentes nuevos (`BitCode.Gateway/Regional`)

| Componente | Responsabilidad |
|---|---|
| [`GatewayRegionalRoutingOptions`](../src/BitCode.Gateway/Regional/GatewayRegionalRoutingOptions.cs) | Sección de configuración `"Regional"`: `CurrentRegion` (región de esta instancia) y `TenantRegionAssignments` (mapa tenant → región, por configuración). |
| [`ConfigurationTenantRegionMapStore`](../src/BitCode.Gateway/Regional/ConfigurationTenantRegionMapStore.cs) | `ITenantRegionMapStore` respaldado por `GatewayRegionalRoutingOptions.TenantRegionAssignments` — reemplaza a `InMemoryTenantRegionMapStore` porque el Gateway no tiene base de datos propia; la fuente natural de la asignación es la misma configuración externa que ya usa para rutas YARP/rate limiting/límites de tamaño. Se registra ANTES de `AddRegionalOwnership` (mismo mecanismo de extensión — `TryAdd` — documentado en F5-02). |
| [`GatewayRegionalRoutingServiceCollectionExtensions.AddGatewayRegionalRouting`](../src/BitCode.Gateway/Regional/GatewayRegionalRoutingServiceCollectionExtensions.cs) | Registra `ConfigurationTenantRegionMapStore` y llama a `AddRegionalOwnership(currentRegion)` (F5-02) con la región leída de `"Regional:CurrentRegion"`. |
| [`RegionalOwnershipRoutingMiddleware`](../src/BitCode.Gateway/Regional/RegionalOwnershipRoutingMiddleware.cs) | La "política global": lee el claim `tenant_id` del usuario ya autenticado (nunca un header/query string que el cliente controle — mismo criterio que `HttpContextTenantProvider`, `docs/threat-model.md` S2), resuelve la región propietaria (`IRegionalOwnershipResolver`) y la compara contra la región de esta instancia (`ICurrentRegionProvider`). Si no coinciden, responde `421 Misdirected Request` con un `ProblemDetails` y el header `X-BitCode-Owner-Region` — **nunca** deja pasar el request hacia YARP/el backend. |

### 2.3 Posición en el pipeline (`Program.cs`)

```
UseForwardedHeaders → UseGatewayMaxRequestBodySize (F4-09) → UseAuthentication → UseAuthorization
  → UseGatewayRegionalOwnershipRouting (F5-03, NUEVO) → UseRateLimiter (F4-08) → MapReverseProxy
```

Después de auth (necesita el claim `tenant_id` del usuario ya autenticado) y antes de rate limiting/proxy (un request rechazado por región no debe consumir cupo de rate limiting ni llegar al backend).

## 3. Decisión: rechazo (421), no redirect (307)

Un redirect HTTP real hacia la región propietaria requeriría conocer la URL pública de esa región (p. ej. un Gateway regional detrás de DNS/anycast por región) — infraestructura que no existe en este repositorio (un solo host de desarrollo, sin Kubernetes multi-clúster ni regiones físicas). Se aplica la opción recomendada por la tarea: rechazar con `421 Misdirected Request` (semántica HTTP estándar para "este servidor no puede producir una respuesta válida para la combinación solicitada") y un `ProblemDetails` que indica la región correcta, tanto en el cuerpo como en el header `X-BitCode-Owner-Region` — un cliente/orquestador que conozca el mapa de URLs por región (fuera del alcance de este framework) puede reintentar contra el Gateway correcto. Esto es el "failover controlado" que pide la tarea: la redirección es explícita y basada en el ownership vigente, nunca un procesamiento silencioso en la región incorrecta.

## 4. Cómo se verificó sin infraestructura multi-región real

Se simulan dos "regiones" lógicas vía configuración (sección `"Regional"`, appsettings/variables de entorno), sin levantar ningún clúster ni host adicional:

- Una instancia del Gateway (`WebApplicationFactory<Program>`) se configura con `Regional:CurrentRegion = "us-east"`.
- Un tenant tiene, en `Regional:TenantRegionAssignments`, una asignación explícita a `"eu-west"` (región distinta de la local).
- Un segundo tenant tiene una asignación explícita a `"us-east"` (misma región que la local).
- Un tercer tenant no tiene ninguna asignación — resuelve de forma determinística a `RegionId.Primary` (`"primary"`), que tampoco coincide con `"us-east"`.

`GatewayRegionalRoutingIntegrationTests` (`tests/BitCode.Gateway.Tests/Integration/GatewayRegionalRoutingIntegrationTests.cs`) verifica, contra el Gateway real (Kestrel/TestServer) proxyando a un backend HTTP real (`GatewayTestBackend`, mismo patrón que `GatewayIntegrationTests`):

1. Un request de un tenant cuya región propietaria (`eu-west`) es distinta de la región local (`us-east`) se rechaza con `421` y el header `X-BitCode-Owner-Region: eu-west` — **nunca llega al backend** (criterio de aceptación "Requests llegan al owner").
2. Un request de un tenant cuya región propietaria coincide con la región local se proxya normalmente (`200`).
3. Un tenant sin asignación explícita resuelve a `primary` y se rechaza (porque la región local de esta instancia simulada es `us-east`, no `primary`) — confirma que "sin asignación" nunca significa "cualquier región vale".
4. Un request sin token se sigue rechazando con `401` (el auth boundary de F4-08 corre antes; el routing regional nunca se evalúa para un request no autenticado).

## 5. Cero cambio de comportamiento para el caso hoy real

Con los defaults de `appsettings.json` (`Regional:CurrentRegion = "primary"`, `Regional:TenantRegionAssignments` vacío), todo tenant resuelve a `RegionId.Primary` y la región de la instancia también es `RegionId.Primary` — la comparación siempre coincide y `RegionalOwnershipRoutingMiddleware` nunca rechaza nada. Un consumidor de un solo host/región (el único escenario real hoy en este repositorio) no ve ningún cambio de comportamiento del Gateway.

## 6. Qué NO resuelve F5-03 (explícitamente fuera de alcance)

- **Redirección real hacia la URL pública de otra región** — requiere una topología de URLs por región (DNS/anycast) que no existe hoy; el mecanismo entregado deja la información necesaria (header `X-BitCode-Owner-Region`) para que un orquestador externo la implemente cuando exista esa topología.
- **Replicación real del mapa de ownership entre regiones** — sigue siendo F5-04, igual que en F5-02; `ConfigurationTenantRegionMapStore` depende de que la configuración de cada instancia regional del Gateway esté correctamente sincronizada por quien la despliega, no por un mecanismo de replicación del propio framework.
- **Failover/failback regional real** (mover la propiedad de escritura de un tenant de una región a otra) — requiere aprobación humana explícita (sección 13 del Plan Maestro), igual que documenta F5-02.
- **Multi-tenancy en Sample.Api u otro consumidor real usando este mecanismo en producción** — F5-03 entrega la capacidad en el Gateway del framework; adoptarla en un despliegue productivo concreto (con URLs de región reales, Kubernetes multi-clúster, etc.) es trabajo de infraestructura posterior, fuera de alcance de esta tarea.

## 7. Verificación

- `dotnet test tests/BitCode.Gateway.Tests/BitCode.Gateway.Tests.csproj --filter "FullyQualifiedName!~Distributed"` — 13/13 exitosas, incluyendo las 4 de `GatewayRegionalRoutingIntegrationTests`.
- `dotnet build BitCode.Framework.slnx` — compilación correcta de toda la solución, sin errores.
- `dotnet test tests/Shared.Application.Tests` (filtro `RegionalOwnership`) y `tests/Shared.Infrastructure.Persistence.Tests` (filtro `Region`) — sin regresión sobre el mecanismo de F5-02 que este routing reutiliza.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 5 (backlog F5-01 a F5-09), sección 13 (aprobaciones humanas).
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — F5-02, mecanismo de ownership que este routing reutiliza.
- [`threat-model.md`](threat-model.md) — hallazgo S2 (el `TenantId` nunca se resuelve desde un header/query string controlado por el cliente).
