# Fase 2 — Capa de aplicación

**Estado:** Completa
**Commits:** `9b1fc7e`, `eb60b6d`
**Tests:** 44 en verde (9 `Shared.Kernel.Tests` + 12 `Shared.Application.Tests` + 23 `Shared.Infrastructure.Persistence.Tests` no-integración)

## Objetivo

Construir el patrón CQRS del framework sobre MediatR: contratos `ICommand`/`IQuery`, pipeline de behaviors transversales (validación, logging, transacciones) y el tipo de retorno uniforme (`Result<T>`) que evita usar excepciones para flujo de negocio esperado.

## Decisiones de diseño

| Decisión | Elegido | Motivo |
|---|---|---|
| Librería de mapeo | Mapster | Alto rendimiento (expression trees compiladas), sin riesgo de licenciamiento comercial (a diferencia de AutoMapper desde 2024) |
| Alcance de `TransactionBehavior` | Solo `ICommand`, nunca `IQuery` | Las queries son de solo lectura por convención; abrir transacción para ellas es overhead innecesario |

## Prerequisito no completado en la Fase 0/1: `Result<T>`

El plan original preveía `Result<T>` en la Fase 0 ("Fundamentos"), pero esa fase quedó absorbida en la Fase 1 y solo cubrió `Entity`/`AggregateRoot`/auditoría — `Result<T>` nunca se creó. Se añadió como **Tarea 2.1**, prerequisito real antes de poder escribir `ValidationBehavior`.

## Componentes por tarea

### Tarea 2.1 — `Result`/`Result<TValue>`/`Error`/`ValidationError` (`Shared.Kernel`)

- `Error(Code, Description, Type)` con `ErrorType`: `Failure`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`.
- `Result`/`Result<TValue>` con invariantes verificados en el constructor (`protected internal`, expuesto a tests vía `InternalsVisibleTo`): un resultado exitoso no puede tener `Error`, uno fallido debe tenerlo.
- `ValidationError : Error` agrega un array de `Error[]` para reportar múltiples fallos de validación en un solo `Result.Failure`.
- Conversión implícita `TValue → Result<TValue>` para reducir ceremonia en los handlers.

### Tarea 2.2 — Contratos `ICommand`/`IQuery` (`Shared.Application`, nuevo proyecto)

```csharp
public interface IBaseCommand;
public interface ICommand : IRequest<Result>, IBaseCommand;
public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
```

`IBaseCommand` es el marcador que le permite a `TransactionBehavior` aplicarse solo a comandos vía constraint genérico (`where TRequest : IBaseCommand`), sin necesidad de inspeccionar tipos en tiempo de ejecución.

### Tarea 2.3 — `ValidationBehavior`

Ejecuta todos los `IValidator<TRequest>` de FluentValidation registrados para el request. Si no hay validadores, pasa directo al handler (evita el overhead de crear un `ValidationContext` innecesariamente). Si hay fallos, construye un `Result.Failure(ValidationError)` **sin invocar el handler** — usando reflexión sobre `Result.Failure<T>` para soportar tanto `Result` como `Result<TResponse>` desde un único behavior genérico (mismo patrón ya validado en BC-SFE-MID).

### Tarea 2.4 — `LoggingBehavior`

Mide duración con `Stopwatch`, registra outcome (info si éxito, warning con código/descripción del error si falla), y emite un warning adicional si supera un umbral configurable (`LoggingBehaviorOptions.SlowRequestThresholdMilliseconds`, default 500ms).

### Tarea 2.5 — `TransactionBehavior`

```csharp
where TRequest : IBaseCommand, IRequest<TResponse>
where TResponse : Result
```

Envuelve el handler en `IUnitOfWork.BeginTransactionAsync` (Fase 1). Si el `Result` devuelto es exitoso, hace `CommitAsync`; si es fallo, `RollbackAsync` sin commitear; si el handler lanza una excepción, hace `RollbackAsync` y re-lanza. Al restringirse a `IBaseCommand`, MediatR simplemente no lo aplica a un `IQuery` — verificado con un test de extremo a extremo que confirma que una query nunca llama a `BeginTransactionAsync`.

### Tarea 2.6 — Mapster

Se evaluó construir una interfaz propia `IMapFrom<T>` de conveniencia, pero **Mapster ya define una** (`Mapster.IMapFrom<TSource>`), lo que generó un conflicto de nombres ambiguo al importar ambos namespaces. Se descartó la interfaz propia: el framework usa las convenciones nativas de Mapster (`IRegister` para configuración explícita, `IMapFrom<TSource>` de Mapster para el caso simple de propiedades homónimas), descubiertas automáticamente vía `TypeAdapterConfig.Scan(assemblies)`.

### Tarea 2.7 — `AddSharedApplication()`

Registra en un solo paso:
1. MediatR con `RegisterServicesFromAssemblies` + los tres behaviors como *open behaviors*, en el orden `Logging → Validation → Transaction → Handler`.
2. Los validadores de FluentValidation (`AddValidatorsFromAssemblies`).
3. Mapster: `TypeAdapterConfig.GlobalSettings.Scan(assemblies)` + `IMapper` (`ServiceMapper`) como scoped.

### Tarea 2.8 — Tests

- `ValidationBehaviorTests`: sin validadores (passthrough), validadores que aprueban, validadores que fallan (no llega al handler, `Result.Failure` con `ValidationError`), múltiples validadores donde uno falla.
- `LoggingBehaviorTests`: el behavior no altera el `Result` que retorna el handler (éxito y fallo).
- `TransactionBehaviorTests` (con `IUnitOfWork` simulado vía NSubstitute): éxito → begin+commit, fallo → begin+rollback sin commit, excepción → begin+rollback y re-lanza.
- `ApplicationServiceCollectionExtensionsTests`: prueba de extremo a extremo con MediatR real resuelto por DI — un `ICommand` exitoso dispara begin/commit; un `IQuery` **nunca** dispara `BeginTransactionAsync`; un `ICommand` inválido falla en `ValidationBehavior` sin llegar a abrir transacción.

## Cómo usarlo desde un proyecto consumidor

```csharp
// 1. Definir el Command/Query y su Handler
public record CrearProductoCommand(string Nombre, decimal Precio) : ICommand<Guid>;

public class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    public CrearProductoCommandValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty();
        RuleFor(c => c.Precio).GreaterThan(0);
    }
}

public class CrearProductoCommandHandler(IRepository<Producto, Guid> repository)
    : IRequestHandler<CrearProductoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearProductoCommand request, CancellationToken ct)
    {
        var producto = new Producto(Guid.NewGuid(), request.Nombre, request.Precio);
        await repository.AddAsync(producto, ct);
        return producto.Id; // conversión implícita a Result<Guid>
    }
}

// 2. Registrar (una sola vez, junto con AddSharedPersistence de la Fase 1)
services.AddSharedApplication(typeof(CrearProductoCommand).Assembly);

// 3. Enviar desde el Endpoint/Controller
var result = await mediator.Send(new CrearProductoCommand("Teclado", 49.90m));
return result.IsSuccess ? Results.Created($"/productos/{result.Value}", result.Value)
                         : Results.BadRequest(result.Error);
```

Un `ICommand` obtiene automáticamente validación + logging + transacción sin código adicional en el handler. Un `IQuery<T>` obtiene validación + logging, sin el overhead transaccional.

## Cobertura de tests

| Área | Tests |
|---|---|
| `Result`/`Error`/`ValidationError` | 9 |
| `ValidationBehavior` | 4 |
| `LoggingBehavior` | 2 |
| `TransactionBehavior` | 3 |
| `AddSharedApplication` (extremo a extremo con MediatR real) | 3 |
| **Total Fase 2** | **21** (de los 44 totales del repo en este punto) |

## Pendiente / fuera de alcance de esta fase

- Behavior de idempotencia (patrón visto en BC-SFE-MID vía Redis) — no forma parte del plan de Fase 2 original; candidato para una fase de infraestructura transversal (Fase 4) si se decide incorporarlo.
- Fase 3 del plan general (seguridad: Identity/JWT/OpenIddict, permisos, policy-based authorization) — siguiente fase a desarrollar.

## Actualización (F1-06 — Épica F1-B, Plan Maestro)

El diseño de `TransactionBehavior` descrito arriba (todo `ICommand` abre transacción implícita) fue
**reemplazado**: a partir de F1-06, `TransactionBehavior` solo abre una transacción real de base de
datos (`BeginTransactionAsync`/`CommitAsync`/`RollbackAsync`) para comandos que implementan
explícitamente el nuevo marcador `ITransactionalCommand`. Un `ICommand`/`ICommand<T>` simple sigue
persistiendo sus cambios automáticamente vía `SaveChangesAsync` (el handler nunca lo llama a mano),
pero sin transacción explícita. También se agregó `IIdempotentCommand` como contrato marcador para
comandos que deben tolerar reintentos (implementación completa en F1-22).

Ver `docs/adr/0009-contratos-comando-transaccion-explicita.md` para el detalle de la decisión y
`docs/convenciones.md` (reglas 1, 3 y 4) para la convención vigente.
