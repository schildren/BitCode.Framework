# Golden Paths — Rutas Canónicas de Desarrollo (F8-10)

## 1. Propósito y Visión General

Los **Golden Paths** (caminos dorados) representan las rutas de implementación recomendadas, estandarizadas y verificadas para construir software sobre **BitCode.Framework**. Seguir un Golden Path garantiza:
- Cumplimiento automático de las **reglas duras** de arquitectura (ver [`docs/convenciones.md`](convenciones.md)).
- Coherencia en nomenclatura, patrones CQRS, contratos de retorno y límites de módulos.
- Reutilización de capacidades transversales probadas (auditoría inmutable, tenancy, idempotencia, resiliencia, seguridad y observabilidad).
- Mínima fricción para equipos que inician nuevos bounded contexts o capacidades de negocio.

---

## 2. Golden Path 1: CRUD & Vertical Slice CQRS

Este es el patrón canónico para operaciones de dominio estándar sobre entidades de negocio.

### Estructura Recomendada
```
Features/
└── {Entidad}/
    ├── {Entidad}.cs                      # Agregado / Entidad de dominio
    ├── Crear{Entidad}Command.cs          # Comando + Validator + Handler
    ├── Obtener{Entidad}Query.cs          # Query de lectura + Handler (IReadRepository)
    ├── Listar{Entidad}sQuery.cs          # Query paginada con PagedResult<T>
    └── {Entidad}Endpoints.cs             # Minimal API mapeada a IEndpointRouteBuilder
```

### Código Canónico

#### 1. Entidad de Dominio
```csharp
using BitCode.Framework.Shared.Kernel;

namespace MiModulo.Productos;

public class Producto : AggregateRoot<Guid>, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public decimal Precio { get; private set; }

    private Producto() { } // Constructor privado para EF Core

    public Producto(Guid id, string nombre, decimal precio) : base(id)
    {
        Nombre = Guard.AgainstNullOrWhiteSpace(nombre);
        Precio = Guard.AgainstNegativeOrZero(precio);
    }

    public void ActualizarPrecio(decimal nuevoPrecio)
    {
        Precio = Guard.AgainstNegativeOrZero(nuevoPrecio);
    }
}
```

#### 2. Comando de Escritura (con Idempotencia)
```csharp
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace MiModulo.Productos;

public record CrearProductoCommand(string Nombre, decimal Precio) 
    : ICommand<Guid>, IIdempotentCommand;

public class CrearProductoCommandValidator : AbstractValidator<CrearProductoCommand>
{
    public CrearProductoCommandValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Precio).GreaterThan(0);
    }
}

public class CrearProductoCommandHandler(IRepository<Producto, Guid> repository)
    : IRequestHandler<CrearProductoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearProductoCommand request, CancellationToken ct)
    {
        var producto = new Producto(Guid.NewGuid(), request.Nombre, request.Precio);
        await repository.AddAsync(producto, ct);
        // NOTA: TransactionBehavior ejecuta SaveChangesAsync automáticamente al terminar con éxito.
        return producto.Id;
    }
}
```

#### 3. Consulta de Lectura Eficiente
```csharp
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace MiModulo.Productos;

public record ProductoResponse(Guid Id, string Nombre, decimal Precio);
public record ObtenerProductoQuery(Guid Id) : IQuery<ProductoResponse>;

public class ObtenerProductoQueryHandler(IReadRepository<Producto, Guid> repository)
    : IRequestHandler<ObtenerProductoQuery, Result<ProductoResponse>>
{
    public async Task<Result<ProductoResponse>> Handle(ObtenerProductoQuery request, CancellationToken ct)
    {
        // IReadRepository aplica AsNoTracking() de forma automática
        var producto = await repository.GetByIdAsync(request.Id, ct);
        if (producto is null)
        {
            return Result<ProductoResponse>.Failure("Producto.NoEncontrado", "El producto solicitado no existe.");
        }

        return new ProductoResponse(producto.Id, producto.Nombre, producto.Precio);
    }
}
```

#### 4. Endpoints Minimal API
```csharp
using BitCode.Framework.Shared.Infrastructure.Web.Endpoints;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MiModulo.Productos;

public static class ProductoEndpoints
{
    public static void MapProductoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/productos")
                       .WithTags("Productos");

        group.MapPost("/", async (CrearProductoCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(cmd, ct);
            return result.ToCreatedOrProblem(id => $"/api/v1/productos/{id}");
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoQuery(id), ct);
            return result.ToOkOrProblem();
        });
    }
}
```
*Referencia viva:* [`samples/Sample.Api`](../samples/Sample.Api) y plantilla `dotnet new bitcode-feature`.

---

## 3. Golden Path 2: Orquestación de Workflow

Para procesos de negocio basados en estados, aprobaciones humanas, SLA y reglas de transición.

### Flujo Canónico
1. **Definir el Workflow:** Crear `WorkflowDefinition` con código único.
2. **Versionar y Publicar:** Agregar `WorkflowVersion` en borrador con estados iniciales/finales, reglas de transición y publicar (operación irreversible).
3. **Iniciar Instancia:** Ejecutar `IniciarInstanciaCommand` asociando la entidad de negocio a traves de su identificador y metadata contextual.
4. **Resolver Tareas:** Asignar y completar tareas (`CompletarTareaCommand`) aplicando guardrails de ownership (solo el usuario asignado o delegado puede resolver).

### Código Canónico
```csharp
// 1. Iniciar una instancia de workflow asociada a una entidad de negocio
var iniciarCmd = new IniciarInstanciaCommand(
    WorkflowDefinitionCodigo: "APROBACION_ORDEN_COMPRA",
    EntidadTipo: "OrdenCompra",
    EntidadId: ordenCompraId.ToString(),
    VariablesIniciales: new Dictionary<string, object> { ["Monto"] = 15000 }
);
var instanciaResult = await sender.Send(iniciarCmd, ct);

// 2. Resolver una tarea humana pendiente
var resolverTareaCmd = new ResolverTareaCommand(
    InstanciaId: instanciaResult.Value,
    Accion: "Aprobar",
    Comentarios: "Aprobado conforme al presupuesto trimestral"
);
var tareaResult = await sender.Send(resolverTareaCmd, ct);
```
*Referencia viva:* [`samples/Sample.Workflow.Api`](../samples/Sample.Workflow.Api) y [`docs/guia-workflow.md`](guia-workflow.md).

---

## 4. Golden Path 3: Event-Driven Architecture (EDA) con Outbox y Kafka

Para comunicación asíncrona y consistente entre bounded contexts desacoplados.

### Flujo Canónico
1. **Generar Evento en Dominio:** El agregado de negocio levanta un `DomainEvent` interno (`RaiseDomainEvent`) durante una operación transaccional.
2. **Outbox Automático:** `TransactionBehavior` persiste el agregado y la fila correspondiente en `OutboxMessages` dentro de la misma transacción física SQL Server.
3. **Outbox Relay:** `OutboxPublisherBackgroundService` toma los lotes pendientes con bloqueo pesimista y los publica en el broker Kafka.
4. **Consumo e Inbox Idempotente:** El módulo consumidor recibe el evento con `KafkaEventConsumer<T>`, valida que no haya sido procesado previamente en `InboxMessages` y ejecuta la lógica de negocio.

### Código Canónico

#### 1. Evento de Integración
```csharp
using BitCode.Framework.Shared.Application.Eventing;

namespace MiModulo.Contratos;

public record OrdenCreadaIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid OrdenId,
    Guid ClienteId,
    decimal Total
) : IntegrationEvent(EventId, OccurredOnUtc, "Ventas.OrdenCreada", 1);
```

#### 2. Consumidor con De-duplicación Inbox
```csharp
using BitCode.Framework.Shared.Application.Eventing;
using BitCode.Framework.Shared.Domain.Inbox;

namespace ModuloFacturacion.Eventos;

public class OrdenCreadaEventConsumer(IFacturacionService facturacionService)
    : IEventConsumer<OrdenCreadaIntegrationEvent>
{
    public async Task ConsumeAsync(OrdenCreadaIntegrationEvent evento, CancellationToken ct)
    {
        // Se ejecuta coordinado automáticamente con IInboxMessageProcessor
        await facturacionService.GenerarFacturaPendienteAsync(evento.OrdenId, evento.Total, ct);
    }
}
```
*Referencia viva:* [`samples/Sample.Eventing`](../samples/Sample.Eventing) y [`docs/guia-eventing-contratos.md`](guia-eventing-contratos.md).

---

## 5. Golden Path 4: Gestión Segura de Documentos

Para ingesta multipart, validación de integridad, escaneo antivirus y almacenamiento de blobs.

### Flujo Canónico
1. **Subida Segura:** El endpoint recibe el stream multipart (`IFormFile`), calcula su checksum SHA-256 en memoria o disco y persiste la metadata inicial en `Documentos`.
2. **Escaneo Antivirus:** El pipeline ejecuta `IAntivirusScanner`. Si el archivo contiene una amenaza (ej. firma EICAR), se marca como `Infectado` y queda bloqueado para descarga.
3. **Almacenamiento Desacoplado:** Archivos limpios se persisten en `IDocumentBlobStore` bajo una ruta no predecible basada en hash o GUID.
4. **Descarga con Auditoría:** La descarga verifica permisos RBAC y políticas ABAC, recupera el stream y genera un registro append-only de auditoría en `IAuditWriter`.

### Código Canónico
```csharp
// Endpoint para subir nueva versión de documento
group.MapPost("/{id:guid}/versiones", async (Guid id, IFormFile archivo, ISender sender, CancellationToken ct) =>
{
    using var stream = archivo.OpenReadStream();
    var cmd = new SubirVersionDocumentoCommand(
        DocumentoId: id,
        NombreArchivo: archivo.FileName,
        ContentType: archivo.ContentType,
        Contenido: stream
    );
    var result = await sender.Send(cmd, ct);
    return result.ToOkOrProblem();
}).DisableAntiforgery();
```
*Referencia viva:* [`samples/Sample.Documents.Api`](../samples/Sample.Documents.Api) y [`docs/guia-documents.md`](guia-documents.md).

---

## 6. Golden Path 5: Integration Hub (Sistemas Externos)

Para interacciones HTTP salientes con sistemas legados o terceros, con mapeo configurable, credenciales seguras y colas de reintento.

### Flujo Canónico
1. **Configurar Conector:** Dar de alta un `IntegrationConnector` con URL base, método, tipo de autenticación y referencia de secreto en `ISecretProvider`.
2. **Definir Mapeo:** Asociar `IntegrationFieldMapping` para transformar nombres de campos internos a las rutas esperadas por la API externa.
3. **Encolar Petición:** Guardar `IntegrationRequest` en base de datos sin acoplar la transacción del usuario a la latencia de la red externa.
4. **Procesador en Background:** `IntegrationOutboundProcessorJob` despacha la solicitud, aplica el mapeo dinámico en cada intento, ejecuta con resiliencia de dos capas y registra el resultado en `IntegrationRequestLog`.

### Código Canónico
```csharp
// Encolar una petición saliente hacia un sistema externo
var enviarCmd = new EnviarSolicitudIntegracionCommand(
    ConectorCodigo: "ERP_SAP_VENTAS",
    PayloadInternoJson: JsonSerializer.Serialize(new { ClienteRfc = "XAXX010101000", MontoFactura = 2500.00 })
);
var result = await sender.Send(enviarCmd, ct);
```
*Referencia viva:* [`samples/Sample.IntegrationHub.Api`](../samples/Sample.IntegrationHub.Api) y [`docs/guia-integration-hub.md`](guia-integration-hub.md).

---

## 7. Verificación Continua de los Golden Paths

Todos los Golden Paths descritos están respaldados por:
1. Pruebas de humo estructurales en [`tests/BitCode.Architecture.Tests/GoldenPaths/GoldenPathSmokeTests.cs`](../tests/BitCode.Architecture.Tests/GoldenPaths/GoldenPathSmokeTests.cs).
2. Pruebas de integración reales contra contenedores (SQL Server, Kafka, Kestrel) en los proyectos correspondientes en `samples/`.
3. Plantillas de scaffolding automatizadas (`dotnet new bitcode-app`, `dotnet new bitcode-module`, `dotnet new bitcode-feature`).
