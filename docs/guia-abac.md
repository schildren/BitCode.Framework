# Guía — ABAC: autorización combinada RBAC + atributos (F2-08, F2-10)

**Tarea:** F2-08 (Fase 2, Épica F2-B — Autorización) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Criterio de aceptación:** "Reglas por monto, empresa y sucursal".

Este documento también cubre F2-10 ("Operaciones privilegiadas": step-up, reevaluación y segregación de
funciones, criterio de aceptación "Operaciones críticas protegidas") — ver la sección
[Operaciones privilegiadas (F2-10)](#operaciones-privilegiadas-f2-10-step-up-y-segregación-de-funciones)
más abajo. F2-10 se implementa como dos `IAbacRule` adicionales sobre el MISMO `IAuthorizationPolicyEvaluator`
de F2-08, no como un pipeline de autorización paralelo — todo lo dicho arriba sobre el algoritmo combinado
(deny-overrides, invocación explícita, sin resolución automática desde HTTP) aplica también a estas dos
reglas.

## Qué resuelve esta tarea

RBAC 2.0 (F2-07, `IPermissionEvaluator`, ver [`docs/guia-rbac-2.md`](guia-rbac-2.md)) responde una pregunta
estática: "¿el sujeto tiene el permiso `productos.crear`?". No puede responder una pregunta que depende de
la instancia concreta del recurso: "¿puede este sujeto aprobar ESTE pedido, de ESTA empresa/sucursal, por
ESTE monto?" — la respuesta depende de datos de negocio (monto, empresa, sucursal) que solo se conocen
después de leer la entidad, no del token ni de los roles del sujeto.

F2-08 agrega esa pieza sin reemplazar RBAC: `IAuthorizationPolicyEvaluator`
(`Shared.Infrastructure.Security.Abac`) combina el permiso RBAC ya existente (obligatorio, base) con
reglas de negocio basadas en atributos del recurso (`IAbacRule`, restrictivas, nunca conceden más de lo
que RBAC ya concedió). El framework trae dos reglas genéricas y configurables por opciones que cubren el
criterio de aceptación literal sin que el proyecto consumidor escriba código: una para alcance
(empresa/sucursal) y otra para límite de monto.

## El modelo Subject / Resource / Action / Context

| Concepto | Tipo | Qué representa |
|---|---|---|
| Subject | `AbacSubject` | La identidad autenticada (`ClaimsPrincipal`) junto con sus `EffectivePermissions` ya calculados por `IPermissionEvaluator` (F2-07) — RBAC no se recalcula, se reutiliza. |
| Resource | `AbacResource` | El objeto de negocio evaluado: `Type` (mismo segmento que el permiso RBAC, `"{entidad}.{accion}"`) y `Attributes` (datos concretos de esta instancia: monto, empresaId, sucursalId, u otros). |
| Action | `string` | El verbo de negocio (`"aprobar"`, `"crear"`, ...), combinado con `Resource.Type` para formar el permiso RBAC requerido. |
| Context | `AbacContext` | Atributos ambientales ajenos al sujeto y al recurso (hora, IP, u otro que una regla propia necesite) — ninguna regla incorporada por el framework lo usa hoy. |

## `IAuthorizationPolicyEvaluator` — el evaluador combinado

```csharp
public interface IAuthorizationPolicyEvaluator
{
    Task<AbacDecision> EvaluateAsync(
        ClaimsPrincipal principal,
        AbacResource resource,
        string action,
        AbacContext? context = null,
        CancellationToken cancellationToken = default);
}
```

A diferencia de `[RequirePermission]`/`RequireAuthorization("permiso")` (F2-07, declarativo, evaluado por
el pipeline de ASP.NET Core antes de que el endpoint corra), `IAuthorizationPolicyEvaluator` se invoca
SIEMPRE explícitamente desde código de aplicación — típicamente un handler de comando/query, después de
leer la entidad de negocio. No hay ninguna resolución automática de `AbacResource.Attributes` desde la
ruta/query string de un request HTTP: esa resolución es responsabilidad del proyecto consumidor, porque
depende de qué entidad concreta está evaluando (un `PedidoId` en la ruta no dice nada sobre el monto o la
empresa del pedido hasta que se lee de la base de datos).

### Algoritmo (siempre fail-closed)

1. Si `principal` no está autenticado, deniega de inmediato (`AbacDecisionReasons.NotAuthenticated`) — ni
   siquiera se calculan permisos.
2. Calcula el permiso RBAC requerido, `"{resource.Type}.{action}"`, y lo verifica contra
   `IPermissionEvaluator.EvaluateAsync` (F2-07). Sin ese permiso, deniega
   (`AbacDecisionReasons.PermissionDenied`) sin evaluar ninguna regla ABAC — una regla ABAC nunca puede
   conceder lo que RBAC ya denegó.
3. Con el permiso RBAC concedido, ejecuta cada `IAbacRule` registrada cuyo `AppliesTo(resource.Type,
   action)` acepte esta combinación. Basta que UNA regla aplicable devuelva `Deny` para que el resultado
   final sea denegado (`AbacDecisionReasons.RuleDenied`, deny-overrides) — no hace falta unanimidad.
4. Si RBAC concede y ninguna regla aplicable deniega (incluido el caso de no tener ninguna regla
   aplicable), el resultado es `AbacDecision.Allow` (`AbacDecisionReasons.Granted`).

`AbacDecision.Reason` es trazable — mismo espíritu que `PermissionGrant.Source` de F2-07: ante una
pregunta de auditoría alcanza con inspeccionarlo, sin reconstruir manualmente qué paso decidió.

## Las dos reglas incorporadas

Ninguna trae reglas por defecto — una lista vacía en `AbacOptions` significa que esa regla nunca
deniega. Se configuran en `InfrastructureModule` (o donde el proyecto registre sus servicios):

```csharp
services.AddSharedAbacAuthorization(options =>
{
    // Empresa: el atributo "empresaId" del recurso debe figurar en el claim "empresa_id" del sujeto.
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "pedidos",
        ResourceAttributeKey = "empresaId",
        ClaimType = "empresa_id",
    });

    // Sucursal: misma regla genérica, otro atributo/claim -- dos instancias, no un concepto nuevo.
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "pedidos",
        ResourceAttributeKey = "sucursalId",
        ClaimType = "sucursal_id",
    });

    // Monto: el atributo "monto" del recurso no puede superar el claim "monto_maximo" del sujeto.
    options.AmountLimitRules.Add(new AbacAmountLimitRule
    {
        ResourceType = "pedidos",
        ResourceAttributeKey = "monto",
        ClaimType = "monto_maximo",
    });
});
```

### `AttributeScopeAbacRule` (empresa/sucursal, y cualquier atributo de alcance equivalente)

Por cada `AbacScopeAttributeRule` configurada cuyo `ResourceType` matchee el recurso evaluado (o
`"*"` para todos), exige que el valor de `ResourceAttributeKey` en el recurso figure entre los valores
del claim `ClaimType` del sujeto (`AbacSubject.GetClaimValues`, admite más de un valor — un usuario con
acceso a varias sucursales).

**Política deliberada de "sin dato, no se restringe"** (mismo criterio que la validación de tenancy de
`PermissionEvaluator`, F2-07 — la ausencia de un dato no bloquea, solo un valor presente y en conflicto
lo hace):

- Si el recurso no declara el atributo configurado, la regla no tiene nada que comparar: `NotApplicable`.
- Si el sujeto no tiene NINGÚN valor para el claim configurado (por ejemplo, un rol de servicio o
  administrador sin alcance limitado), la regla no le aplica: `NotApplicable`.
- Solo cuando el sujeto SÍ tiene al menos un valor de claim configurado y el valor del recurso no está
  entre esos valores, la regla deniega — ese es el caso positivo.

### `AmountLimitAbacRule` (monto, y cualquier límite numérico equivalente)

Por cada `AbacAmountLimitRule` configurada cuyo `ResourceType` matchee el recurso evaluado, exige que el
valor de `ResourceAttributeKey` en el recurso no supere el valor del claim `ClaimType` del sujeto. Misma
política de "sin dato, no se restringe": recurso sin el atributo, atributo sin forma numérica (acepta
`decimal`/`int`/`long`/`double`/`string` parseable), o sujeto sin el claim de límite (o sin forma
numérica) → `NotApplicable`, no denegación.

### Una regla de negocio propia

Un proyecto con una regla más compleja (que no se expresa como "alcance" o "límite numérico" genérico)
implementa `IAbacRule` directamente y la registra además de las incorporadas:

```csharp
services.AddScoped<IAbacRule, MiReglaDeNegocio>();
```

`AuthorizationPolicyEvaluator` la ejecuta junto con las incorporadas, mismo algoritmo deny-overrides.

## Operaciones privilegiadas (F2-10): step-up y segregación de funciones

RBAC (F2-07) y las reglas ABAC de F2-08 responden "¿tiene el permiso, y respeta los límites de negocio de
ESTA instancia?". Ninguna de las dos responde una pregunta distinta que sí importa para una operación
crítica (por ejemplo, aprobar un pago de monto alto, o aprobar un pedido): "¿la EVIDENCIA de identidad del
sujeto en ESTE momento es suficientemente fuerte/reciente?" (step-up) y "¿el sujeto está en conflicto de
funciones respecto de esta operación o de su propio conjunto de permisos?" (segregación de funciones, SoD).
F2-10 agrega esas dos piezas como `IAbacRule` adicionales, registradas junto con las de F2-08 y ejecutadas
por el mismo `AuthorizationPolicyEvaluator` — ningún endpoint ni handler necesita saber que existen aparte
de configurarlas.

### `StepUpAbacRule` — step-up authentication

Por cada `StepUpRequirement` configurada (`PrivilegedOperationsOptions.StepUpRequirements`) cuyo
(`ResourceType`, `Action`) matchee la evaluación actual (ambos admiten `"*"`), exige evidencia de una
autenticación reciente o reforzada, según los dos criterios configurables (al menos uno es obligatorio):

- **Método de autenticación** (`AcceptableAuthenticationMethods`): el sujeto debe tener, entre los valores
  del claim `AuthenticationMethodClaimType` (por defecto `"amr"`, el claim estándar OIDC), al menos uno de
  los métodos aceptables configurados (por ejemplo, `["mfa"]`).
- **Antigüedad máxima** (`MaxAuthenticationAge`): el sujeto debe tener el claim `AuthenticationTimeClaimType`
  (por defecto `"auth_time"`, epoch Unix en segundos, el claim estándar OIDC) con una antigüedad, respecto
  del instante de la evaluación, menor o igual al valor configurado.

```csharp
services.AddSharedPrivilegedOperationsPolicies(options =>
{
    options.StepUpRequirements.Add(new StepUpRequirement
    {
        ResourceType = "pagos",
        Action = "aprobar",
        MaxAuthenticationAge = TimeSpan.FromMinutes(5), // reautenticado en los últimos 5 minutos
    });
});
```

**Política deliberadamente fail-closed — DISTINTA de la política "sin dato, no se restringe" de F2-08**:
un requisito de step-up existe porque la operación es crítica, así que la ausencia del claim de evidencia
(el IdP no lo emite, o el sujeto nunca se reautenticó) deniega, no se ignora. Un `StepUpRequirement` sin
ningún criterio configurado (ni `AcceptableAuthenticationMethods` ni `MaxAuthenticationAge`) es un error de
configuración: `PrivilegedOperationsOptionsValidator` (`IValidateOptions<PrivilegedOperationsOptions>`,
registrado junto con `services.AddOptions<PrivilegedOperationsOptions>().ValidateOnStart()` dentro de
`AddSharedPrivilegedOperationsPolicies`) lo rechaza EN EL ARRANQUE — nunca en la primera evaluación real de
un usuario. `StepUpAbacRule.EvaluateAsync` conserva el mismo `throw new InvalidOperationException` como
defensa en profundidad (por ejemplo, si alguien construye la regla a mano fuera de DI, con opciones no
validadas), pero en un pipeline registrado normalmente ese código es inalcanzable: el error de
configuración ya se detectó al construir el `ServiceProvider`/resolver `IOptions<PrivilegedOperationsOptions>`
por primera vez, con el mismo mensaje identificando el `ResourceType`/`Action` mal configurado, evitando que
un typo se manifieste como un 500 genérico para el usuario final.

`AbacRuleOutcome.Reason` producido: `step-up:not-required` (ningún requisito matchea esta evaluación),
`step-up:verified` (matcheó y el sujeto cumplió toda la evidencia exigida),
`step-up:missing-authentication-method:{claim}` / `step-up:missing-authentication-time:{claim}` (evidencia
ausente) o `step-up:authentication-too-old:{claim}` (evidencia presente pero vencida).

### `SegregationOfDutiesAbacRule` — segregación de funciones (SoD)

Dos mecanismos complementarios, ambos configurables sin escribir código:

1. **Maker-checker** (`PrivilegedOperationsOptions.MakerCheckerRules`): para cada `MakerCheckerRule` cuyo
   (`ResourceType`, `Action`) matchee la evaluación actual, exige que el actor identificado en
   `ActorResourceAttributeKey` del recurso (quien ejecutó la acción anterior — por ejemplo, quien creó el
   pedido) sea DISTINTO del sujeto actual (identificado por `SubjectClaimType`, por defecto
   `ClaimTypes.NameIdentifier`) — el patrón clásico "quien aprueba no puede ser quien creó".
2. **Pares mutuamente excluyentes** (`PrivilegedOperationsOptions.MutuallyExclusivePermissions`): un sujeto
   con AMBOS permisos de un par configurado a la vez entre sus `EffectivePermissions` (F2-07) queda
   denegado en TODA evaluación — a diferencia de maker-checker, esta restricción es de identidad, no de una
   instancia de recurso concreta; se verifica primero, antes de cualquier regla maker-checker.

```csharp
services.AddSharedPrivilegedOperationsPolicies(options =>
{
    // Maker-checker: quien creó el pedido no puede aprobarlo.
    options.MakerCheckerRules.Add(new MakerCheckerRule
    {
        ResourceType = "pedidos",
        Action = "aprobar",
        ActorResourceAttributeKey = "creadoPorUserId", // atributo del AbacResource, lo arma el handler
    });

    // Roles/permisos que no pueden coexistir en el mismo sujeto.
    options.MutuallyExclusivePermissions.Add(new MutuallyExclusivePermissionPair
    {
        PermissionA = "pedidos.crear",
        PermissionB = "pedidos.auditar",
    });
});
```

**Política deliberadamente fail-closed** para maker-checker (mismo criterio que step-up, mismo contraste
con F2-08): para una regla configurada y aplicable, la ausencia del atributo de actor en el recurso, o del
claim de sujeto configurado, NO se interpreta como "no hay nada que comparar" — se interpreta como "no se
puede probar que no hay conflicto de funciones", y se deniega. La comparación entre el atributo de actor
del recurso y los claims del sujeto usa `StringComparer.OrdinalIgnoreCase` (no `Ordinal`): ambos valores
suelen representar la MISMA identidad (por ejemplo, un `Guid`) con distinto casing según quién lo haya
generado (el handler de negocio vs. el IdP), y una comparación sensible a mayúsculas/minúsculas fallaría
en detectar al mismo actor — exactamente el escenario que esta regla existe para prevenir.

`AbacRuleOutcome.Reason` producido: `sod:no-restriction` (nada configurado matchea esta evaluación),
`sod:verified` (matcheó y no hubo conflicto), `sod:mutually-exclusive-permissions:{A}+{B}`,
`sod:missing-actor-attribute:{clave}`, `sod:missing-subject-claim:{claim}` o `sod:same-actor:{clave}={valor}`.

### Registro

`AddSharedPrivilegedOperationsPolicies(Action<PrivilegedOperationsOptions>? configureOptions = null)`
(`Shared.Infrastructure.Security.PrivilegedOperations`) debe llamarse DESPUÉS de
`AddSharedAbacAuthorization` — lanza `InvalidOperationException` en el arranque si no encuentra
`IAuthorizationPolicyEvaluator` ya registrado, mismo patrón de guarda que
`PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache` (F2-09):

```csharp
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MiDbContext>(configuration); // RBAC, F2-07
services.AddSharedAbacAuthorization(options => { /* reglas de empresa/sucursal/monto */ }); // ABAC, F2-08
services.AddSharedPrivilegedOperationsPolicies(options => { /* step-up/SoD */ }); // F2-10
```

### "Reevaluación" (criterio de la fila de backlog)

No hay ninguna decisión de autorización cacheada entre invocaciones: `IAuthorizationPolicyEvaluator.EvaluateAsync`
se invoca explícitamente en cada operación (no hay middleware que "recuerde" un `Allow` previo), y el cache
de F2-09 solo memoiza las consultas SQL detrás de `IPermissionEvaluator` — el permiso base, no la decisión
combinada ni la evidencia de step-up. En la práctica, esto significa que la evidencia de step-up (claims del
`ClaimsPrincipal` vigente) se revisa fresca en cada llamada: si el sujeto necesita reautenticarse para volver
a cumplir `MaxAuthenticationAge`, la siguiente operación crítica lo exige de nuevo — no hace falta ningún
mecanismo adicional de invalidación.

### Qué NO resuelve F2-10

- **Un mecanismo de reautenticación en sí** (challenge/redirect al IdP para que el usuario efectivamente
  eleve su nivel de autenticación): F2-10 solo VERIFICA la evidencia de step-up ya presente en el
  `ClaimsPrincipal` (claims `amr`/`auth_time`, u otros configurados). Provocar esa reautenticación (por
  ejemplo, un `prompt=login`/ACR request al flujo OIDC de F2-02, o un segundo factor específico) es
  responsabilidad del proyecto consumidor y del IdP configurado — el framework no implementa un flujo de
  step-up interactivo.
- **Detección automática de pares mutuamente excluyentes** a partir de un catálogo de roles: el proyecto
  consumidor declara explícitamente qué pares son incompatibles; no hay ninguna heurística ni análisis de
  jerarquía de roles.
- **F2-11 (pruebas de autorización):** la matriz allow/deny y las pruebas de bypass de la épica completa
  siguen siendo una tarea separada.

## Uso desde un handler de aplicación

```csharp
public class AprobarPedidoCommandHandler(
    IRepository<Pedido, Guid> pedidos,
    IAuthorizationPolicyEvaluator authorization,
    ICurrentUserAccessor currentUser) // o el mecanismo de acceso al ClaimsPrincipal del proyecto
    : ICommandHandler<AprobarPedidoCommand>
{
    public async Task<Result> Handle(AprobarPedidoCommand command, CancellationToken ct)
    {
        var pedido = await pedidos.GetByIdAsync(command.PedidoId, ct);
        if (pedido is null)
        {
            return Result.Failure(PedidoErrors.NoEncontrado);
        }

        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["empresaId"] = pedido.EmpresaId.ToString(),
            ["sucursalId"] = pedido.SucursalId.ToString(),
            ["monto"] = pedido.Monto,
        });

        var decision = await authorization.EvaluateAsync(currentUser.Principal, resource, "aprobar", cancellationToken: ct);
        if (!decision.Allowed)
        {
            return Result.Failure(new Error("Autorizacion.Denegada", decision.Reason, ErrorType.Forbidden));
        }

        pedido.Aprobar();
        return Result.Success();
    }
}
```

## Registro

`AddSharedAbacAuthorization(Action<AbacOptions>? configureOptions = null)`
(`Shared.Infrastructure.Security.Abac`):

- Llama a `AddSharedPermissionEvaluation()` (F2-07) para garantizar `IPermissionEvaluator` disponible
  aunque el proyecto solo haya llamado a este método. Es seguro llamarlo junto con
  `AddSharedSecurity`/`AddSharedOidcAuthentication` (que también la llaman): desde el bugfix de esta
  misma tarea, `AddSharedPermissionEvaluation` es idempotente (ver más abajo, "Cambio puntual a F2-07").
- Registra `AbacOptions` (`services.AddOptions<AbacOptions>()` + `Configure(configureOptions)` si se
  pasó el delegado).
- Registra `AttributeScopeAbacRule`/`AmountLimitAbacRule` como `IAbacRule` (`TryAddEnumerable` — no se
  duplican si el método se llama más de una vez).
- Registra `IAuthorizationPolicyEvaluator` → `AuthorizationPolicyEvaluator`.

```csharp
services.AddSharedSecurity<ApplicationUser, ApplicationRole, MiDbContext>(configuration); // RBAC, F2-07
services.AddSharedAbacAuthorization(options => { /* reglas de empresa/sucursal/monto */ }); // ABAC, F2-08
```

## Cambio puntual a F2-07 (bugfix de idempotencia)

`PermissionEvaluationServiceCollectionExtensions.AddSharedPermissionEvaluation` registraba
`IPermissionEvaluator` y `IAuthorizationHandler` con `AddScoped` (no `TryAdd`). Antes de F2-08 esto nunca
era un problema porque solo un método lo llamaba por proyecto (`AddSharedSecurity` O
`AddSharedOidcAuthentication`, mutuamente excluyentes). F2-08 necesita llamarlo también desde
`AddSharedAbacAuthorization` — y un proyecto que combina RBAC + ABAC (el caso normal) llama a ambos
métodos en el mismo `IServiceCollection`. Con el `AddScoped` original, esa combinación duplicaba el
registro de `IAuthorizationHandler`: `PermissionAuthorizationHandler.HandleRequirementAsync` se ejecutaba
dos veces por cada verificación de `[RequirePermission]`, sin cambiar el resultado pero desperdiciando una
consulta redundante a `IPermissionEvaluator` (y, transitivamente, a SQL Server) por request. Cambiado a
`TryAddScoped`/`TryAddEnumerable`: mismo comportamiento para el caso de una sola llamada (sigue siendo el
único registro), correcto para el caso de dos llamadas. Ver
`tests/Shared.Infrastructure.Security.Tests/Abac/AbacServiceCollectionExtensionsTests.cs` (caso
`AddSharedAbacAuthorization_CalledAfterAddSharedPermissionEvaluation_DoesNotDuplicateAuthorizationHandler`).

## Qué NO resuelve F2-08 (alcance de tareas posteriores de la Épica F2-B)

- **Integración declarativa con el pipeline HTTP de ASP.NET Core** (un `[RequireAbacPolicy]` equivalente
  a `[RequirePermission]`): decisión consciente, no un olvido. Los atributos de un `AbacResource` son
  datos de negocio de una instancia concreta (monto, empresa, sucursal de un pedido puntual) que solo se
  conocen después de leer la entidad — no hay forma genérica de resolverlos desde la ruta/query string de
  un endpoint sin acoplar el contrato a un endpoint específico. `IAuthorizationPolicyEvaluator` se invoca
  explícitamente desde el handler, con el mismo patrón que cualquier otra validación de negocio.
- **Cache de la decisión combinada:** F2-09 (`docs/guia-rbac-2.md`) cachea las consultas SQL de
  `IPermissionService` que respaldan `IPermissionEvaluator.EvaluateAsync` — `AuthorizationPolicyEvaluator`
  se beneficia automáticamente de ese cache (sigue llamando a `IPermissionEvaluator`, sin ningún cambio
  en este archivo) sin dejar de recalcular el resto de la cadena (permiso RBAC combinado con `IAbacRule`)
  en cada llamada, porque los atributos del recurso (monto, empresa, sucursal) son datos de la instancia
  concreta evaluada, no del sujeto — cachear la DECISIÓN combinada requeriría una clave que incluya esos
  atributos y todavía no existe.
- **F2-10 (operaciones privilegiadas):** step-up, reevaluación y segregación de funciones — ver la sección
  [Operaciones privilegiadas (F2-10)](#operaciones-privilegiadas-f2-10-step-up-y-segregación-de-funciones)
  más arriba; ya cubiertas por este mismo documento.
- **F2-11 (pruebas de autorización):** la matriz allow/deny y las pruebas de bypass de la épica completa
  son una tarea separada.

## Pruebas

- `tests/Shared.Infrastructure.Security.Tests/Abac/AuthorizationPolicyEvaluatorTests.cs`: el algoritmo
  completo con `IPermissionEvaluator`/`IAbacRule` sustituidos (sujeto no autenticado, permiso RBAC
  ausente sin evaluar ninguna regla, RBAC concedido sin reglas aplicables, RBAC concedido con una regla
  no aplicable, RBAC concedido con una regla que deniega, deny-overrides entre varias reglas, paso
  correcto de recurso/acción/contexto a la regla).
- `tests/Shared.Infrastructure.Security.Tests/Abac/AttributeScopeAbacRuleTests.cs`: `AppliesTo` por tipo
  de recurso exacto y comodín, coincidencia/no coincidencia de valores, los dos casos de "sin dato, no se
  restringe" (recurso sin atributo, sujeto sin claim) y dos reglas configuradas simultáneamente
  (empresa + sucursal).
- `tests/Shared.Infrastructure.Security.Tests/Abac/AmountLimitAbacRuleTests.cs`: dentro/en/por-encima del
  límite, los casos de "sin dato, no se restringe" (recurso sin monto, sujeto sin límite, claim no
  numérico), y conversión de `int`/`long`/`double` además de `decimal`.
- `tests/Shared.Infrastructure.Security.Tests/Abac/AbacServiceCollectionExtensionsTests.cs`: el registro
  DI (evaluador + reglas incorporadas resolubles, `AbacOptions` configurado por el delegado) y la
  regresión de idempotencia del bugfix de F2-07 descrito arriba.
- `tests/Shared.Infrastructure.Security.Tests/Abac/AbacEndToEndTests.cs`: prueba de componente contra la
  pila real completa (contenedor de DI real, `PermissionEvaluator`/`AttributeScopeAbacRule`/
  `AmountLimitAbacRule` reales, solo `IPermissionService` sustituido) — el criterio de aceptación literal
  ("reglas por monto, empresa y sucursal") de punta a punta, incluido el caso default-deny de "sin
  permiso RBAC base, ningún atributo salva la decisión".
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/StepUpAbacRuleTests.cs`: `AppliesTo` por
  recurso/acción exacto y comodín, ambos criterios de evidencia (método, antigüedad) por separado y
  combinados, los casos fail-closed (claim ausente deniega, a diferencia de F2-08) y el
  `InvalidOperationException` de un requisito sin ningún criterio configurado como defensa en profundidad
  (construcción manual de la regla, fuera de DI, con opciones no validadas).
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/SegregationOfDutiesAbacRuleTests.cs`:
  maker-checker (mismo actor deniega, actor distinto pasa, mismo actor con distinto casing sigue
  denegando, atributo/claim ausente deniega fail-closed) y pares mutuamente excluyentes (ambos permisos
  deniega, uno solo pasa), y los casos de "nada configurado para esta evaluación".
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/PrivilegedOperationsServiceCollectionExtensionsTests.cs`:
  el guard de orden de registro (`InvalidOperationException` sin `AddSharedAbacAuthorization` previo), el
  registro de las dos reglas junto con las de F2-08, `PrivilegedOperationsOptions` configurado por el
  delegado, idempotencia de una segunda llamada, y la validación en el arranque
  (`OptionsValidationException` al resolver `IOptions<PrivilegedOperationsOptions>`) de un
  `StepUpRequirement` sin ningún criterio configurado.
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/PrivilegedOperationsEndToEndTests.cs`:
  prueba de componente contra la pila real completa (mismo criterio que `AbacEndToEndTests`) — el criterio
  de aceptación literal de F2-10 ("operaciones críticas protegidas") de punta a punta: step-up
  cumplido/vencido/ausente, maker-checker mismo/distinto actor, pares mutuamente excluyentes, y el caso
  default-deny de "sin permiso RBAC base, ninguna evidencia de step-up salva la decisión".

## Referencias

- `src/Shared.Infrastructure.Security/Abac/` — `IAuthorizationPolicyEvaluator`/`AuthorizationPolicyEvaluator`,
  `AbacSubject`/`AbacResource`/`AbacContext`, `IAbacRule`/`AbacRuleOutcome`,
  `AttributeScopeAbacRule`/`AmountLimitAbacRule`/`AbacOptions`, `AbacServiceCollectionExtensions`.
- `src/Shared.Infrastructure.Security/PrivilegedOperations/` (F2-10) — `StepUpAbacRule`/`StepUpRequirement`,
  `SegregationOfDutiesAbacRule`/`MakerCheckerRule`/`MutuallyExclusivePermissionPair`,
  `PrivilegedOperationsOptions`, `PrivilegedOperationsOptionsValidator` (validación en el arranque, vía
  `ValidateOnStart`), `PrivilegedOperationsServiceCollectionExtensions`.
- `docs/guia-rbac-2.md` — RBAC 2.0 (F2-07), la pieza base sobre la que se apoya este evaluador combinado.
- `docs/convenciones.md` — reglas duras del framework, y la entrada "autorizar una operación por atributos
  de negocio (monto, empresa, sucursal), además del permiso RBAC".
- `docs/plan-maestro-bitcode-ia.md` — backlog completo de la Épica F2-B (F2-07 a F2-11).
