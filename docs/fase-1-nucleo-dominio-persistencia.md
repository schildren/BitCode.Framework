# Fase 1 — Núcleo de dominio y persistencia

**Estado:** Completa
**Commits:** `5eeab40` … `a559195` (8 commits)
**Tests:** 28 en verde (23 unitarios + 5 de integración contra SQL Server real)

## Objetivo

Construir la capa de persistencia genérica y transversal del framework — repositorio genérico, Specification pattern, Unit of Work, auditoría automática, soft-delete y multi-tenancy opcional — sin acoplamiento a ningún dominio de negocio concreto, de modo que cualquier proyecto consumidor (empezando por BitCode.Satus) pueda heredar este comportamiento en vez de reimplementarlo.

## Decisiones de diseño

Estas decisiones se fijaron antes de empezar a desarrollar y condicionan toda la fase:

| Decisión | Elegido | Motivo |
|---|---|---|
| Tipo de Id | `TId` genérico con `IEquatable<TId>`, estándar = `Guid` | Facilita multi-tenancy y evita colisiones entre entornos |
| Estilo de repositorio | Genérico + extensible | `IRepository<T,TId>` cubre el CRUD simple; un agregado complejo puede heredar `RepositoryBase` y añadir métodos propios sin romper el contrato genérico |
| Multi-tenancy | Opcional / activable | `ITenantEntity` es un marcador; una entidad que no lo implementa no recibe el filtro — sirve igual para proyectos single-tenant y multi-tenant |
| Motor de BD | SQL Server | Consistente con el stack actual del equipo (BitCode.Satus y otros proyectos .NET) |
| Ubicación del código | Repo nuevo dedicado (`BitCode.Framework`) | Independiente de BitCode.Satus y BC-SFE-MID, publicable luego como paquetes NuGet internos |

## Estructura de proyectos resultante

```
BitCode.Framework/
├── NuGet.Config                          # fuente única nuget.org (evita una fuente privada inaccesible configurada globalmente)
├── src/
│   ├── Shared.Kernel/                    # Sin dependencias externas (solo BCL)
│   │   ├── Entity.cs                     # Entity<TId> con igualdad por Id
│   │   ├── AggregateRoot.cs              # AggregateRoot<TId> con DomainEvents
│   │   ├── DomainEvent.cs
│   │   ├── IAuditedEntity.cs             # CreatedAtUtc/CreatedBy/ModifiedAtUtc/ModifiedBy
│   │   ├── ISoftDelete.cs                # IsDeleted/DeletedAtUtc/DeletedBy
│   │   └── ITenantEntity.cs              # TenantId (marcador opcional)
│   │
│   ├── Shared.Domain/                    # Contratos agnósticos de EF Core
│   │   ├── Persistence/
│   │   │   ├── IReadRepository.cs
│   │   │   ├── IRepository.cs
│   │   │   └── IUnitOfWork.cs
│   │   ├── Specifications/
│   │   │   ├── ISpecification.cs
│   │   │   └── Specification.cs          # clase base abstracta (no depende de EF Core)
│   │   ├── MultiTenancy/
│   │   │   └── ITenantProvider.cs
│   │   └── Security/
│   │       └── ICurrentUserProvider.cs   # desacoplado de HttpContext
│   │
│   └── Shared.Infrastructure.Persistence/  # Implementación EF Core 8 + SQL Server
│       ├── Specifications/SpecificationEvaluator.cs
│       ├── Repositories/RepositoryBase.cs
│       ├── UnitOfWork.cs
│       ├── Interceptors/
│       │   ├── AuditableEntitySaveChangesInterceptor.cs
│       │   ├── SoftDeleteInterceptor.cs
│       │   └── TenantSaveChangesInterceptor.cs
│       ├── MultiTenancy/
│       │   ├── MultiTenantDbContext.cs        # base abstracta que el consumidor hereda
│       │   ├── PerInstanceModelCacheKeyFactory.cs
│       │   └── NullTenantProvider.cs          # default single-tenant
│       ├── Security/NullCurrentUserProvider.cs  # default sin usuario
│       └── PersistenceServiceCollectionExtensions.cs  # AddSharedPersistence<TContext>()
│
└── tests/
    └── Shared.Infrastructure.Persistence.Tests/
        ├── *.cs                           # tests unitarios (InMemory / SQLite)
        └── Integration/                   # tests contra SQL Server real (Testcontainers)
```

## Componentes por tarea

### Tarea 1.1 — Contratos de persistencia

Interfaces agnósticas de EF Core en `Shared.Domain`: `IReadRepository<TEntity,TId>`, `IRepository<TEntity,TId>`, `ISpecification<TEntity>`, `IUnitOfWork`, `ITenantProvider`. El repositorio no expone `IQueryable` en ningún punto — toda consulta pasa por una `Specification` o por los métodos tipados del contrato.

### Tarea 1.2 — Specification pattern

`Specification<T>` (clase base, vive en `Shared.Domain` porque no depende de EF Core) + `SpecificationEvaluator<T>` (traduce la especificación a `IQueryable`, vive en `Shared.Infrastructure.Persistence` porque sí depende de EF Core). Soporta filtro, includes, orden, paginación y `AsNoTracking`.

### Tarea 1.3 — Repositorio genérico

`RepositoryBase<TEntity,TId>` implementa `IRepository<TEntity,TId>` sobre `DbContext`/`DbSet<TEntity>`. No llama `SaveChanges` — esa responsabilidad es del `IUnitOfWork`. Es extensible: un repositorio especializado puede heredar `RepositoryBase` y añadir consultas propias del agregado sin duplicar el CRUD base.

### Tarea 1.4 — Unit of Work

`UnitOfWork` envuelve `DbContext.SaveChangesAsync` + `IDbContextTransaction`, con rollback automático si `CommitAsync` falla.

### Tarea 1.5 — Auditoría y soft-delete automáticos

Dos `SaveChangesInterceptor`:
- **`AuditableEntitySaveChangesInterceptor`**: puebla `CreatedAtUtc`/`CreatedBy` en altas y `ModifiedAtUtc`/`ModifiedBy` en modificaciones, para toda entidad `IAuditedEntity`.
- **`SoftDeleteInterceptor`**: convierte `EntityState.Deleted` en `Modified` para entidades `ISoftDelete`, evitando el borrado físico.

Ambos usan `ICurrentUserProvider` (no `HttpContext`) para que funcionen igual en una API, un Worker o un job en background.

### Tarea 1.6 — Multi-tenancy con Global Query Filters

`MultiTenantDbContext` es la clase base que un proyecto consumidor hereda (solo define sus `DbSet<T>`). En `OnModelCreating`, aplica automáticamente por reflexión un filtro combinado (`!IsDeleted && (tenant deshabilitado || TenantId == tenant actual)`) a toda entidad que implemente `ISoftDelete`/`ITenantEntity`. `TenantSaveChangesInterceptor` asigna el `TenantId` actual en altas nuevas sin sobreescribir uno ya asignado explícitamente.

**Nota de diseño importante — bug real detectado y corregido:** el filtro global cierra sobre el estado de la instancia (`_tenantId`, `_isMultiTenancyEnabled`) capturado en `OnModelCreating`. El `IModelCacheKeyFactory` por defecto de EF Core cachea el **modelo** (y por tanto ese closure) por tipo de `DbContext`, y lo reutiliza en todas las instancias siguientes — "congelando" el tenant de la primera instancia creada. Un test que aisló datos entre dos tenants con la misma `DbContextOptions` detectó esto empíricamente antes de asumir que funcionaba. Se corrigió con `PerInstanceModelCacheKeyFactory`, que fuerza a reconstruir el modelo (y el filtro) en cada instancia de `DbContext`, a costa de un pequeño overhead de arranque por instancia.

### Tarea 1.7 — Registro DI

`AddSharedPersistence<TContext>(connectionString)` registra en un solo paso: el `MultiTenantDbContext` consumidor sobre SQL Server con los tres interceptores conectados, `IUnitOfWork`, el repositorio genérico abierto (`IRepository<,>` → `RepositoryBase<,>`), y `ITenantProvider`/`ICurrentUserProvider` con implementaciones no-op (`NullTenantProvider`, `NullCurrentUserProvider`) que un consumidor puede sobreescribir registrando las suyas **antes** de llamar al método.

### Tarea 1.8 — Tests de integración contra SQL Server real

Suite con `Testcontainers.MsSql` (base de datos aislada por test, contenedor `mcr.microsoft.com/mssql/server`), que ejercita todo el stack anterior a través de `AddSharedPersistence` tal como lo usaría un consumidor real, no solo con proveedores en memoria.

**Segundo bug real detectado:** `RepositoryBase.GetByIdAsync` usaba `DbSet.FindAsync`, que devuelve una entidad ya trackeada directamente desde el `ChangeTracker` **sin volver a aplicar los filtros globales** — un test que soft-eliminaba un registro y lo volvía a buscar en la misma unidad de trabajo seguía encontrándolo. Se corrigió cambiando la implementación a `Where`/`FirstOrDefaultAsync`, que sí respeta el filtro, a costa de perder el atajo de caché local que ofrecía `FindAsync`.

## Cómo usarlo desde un proyecto consumidor

```csharp
// 1. Definir el DbContext del proyecto heredando de MultiTenantDbContext
public class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Producto> Productos => Set<Producto>();
}

// 2. Registrar el stack completo
services.AddSharedPersistence<MyAppDbContext>(connectionString);

// 3. (Opcional) registrar implementaciones propias ANTES del paso 2 si se necesita
//    multi-tenancy real o auditoría de usuario autenticado
services.AddScoped<ITenantProvider, HttpContextTenantProvider>();
services.AddScoped<ICurrentUserProvider, HttpContextCurrentUserProvider>();

// 4. Consumir en los handlers de aplicación
public class CrearProductoHandler(IRepository<Producto, Guid> repository, IUnitOfWork unitOfWork)
{
    public async Task Handle(...)
    {
        await repository.AddAsync(nuevoProducto);
        await unitOfWork.SaveChangesAsync();
    }
}
```

Una entidad que quiera auditoría, soft-delete y/o multi-tenancy solo necesita implementar las interfaces correspondientes (`IAuditedEntity`, `ISoftDelete`, `ITenantEntity`) — el resto (repositorio, filtros, interceptores) funciona sin código adicional.

## Cobertura de tests

| Área | Unitarios (InMemory/SQLite) | Integración (SQL Server real) |
|---|---|---|
| Specification / evaluator | 3 | — |
| Repositorio genérico | 7 | 1 (CRUD) |
| Unit of Work (commit/rollback) | 4 | 1 (rollback) |
| Auditoría | 2 | 1 |
| Soft-delete | 2 | 1 |
| Multi-tenancy | 3 | 1 |
| Registro DI (`AddSharedPersistence`) | 2 | — |
| **Total** | **23** | **5** |

## Pendiente / fuera de alcance de esta fase

- Publicación de los proyectos como paquetes NuGet internos (mencionado como objetivo futuro en la decisión de repo dedicado, no ejecutado en esta fase).
- Migraciones EF Core del `DbContext` consumidor — cada proyecto gestiona las suyas; el framework no impone una estrategia de migraciones.
- Fase 2 del plan general (capa de aplicación: MediatR, pipeline behaviors, `Result<T>`, mapeo) — siguiente fase a desarrollar.
