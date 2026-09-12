using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

/// <summary>
/// Un interruptor de una capacidad de negocio administrado por API, con persistencia propia y ciclo de
/// vida propio (Fase 6, módulo Feature Management) -- deliberadamente distinto del mecanismo de
/// <c>IFeatureFlagProvider</c> de F4-12 (<c>Shared.Infrastructure.Security.FeatureFlags</c>), que lee un
/// on/off simple desde <c>IConfiguration</c> sin targeting ni persistencia. Ver
/// <c>docs/guia-feature-management.md</c>, sección "Relación con F4-12" para la frontera explícita entre
/// ambos mecanismos, que CONVIVEN.
///
/// Nace con <see cref="Activo"/> en <see langword="false"/> (un flag se crea apagado -- activarlo es una
/// decisión explícita y auditada, ver <see cref="Activar"/>). Es un <see cref="AggregateRoot{TId}"/>
/// propio del módulo (mismo criterio que <c>Catalogo</c> en Catalogs and Parameters, Fase 6 módulo 3): no
/// reutiliza tipos ajenos de otro bounded context.
/// </summary>
public sealed class FeatureFlag : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    /// <summary>Nombre lógico estable del flag (por ejemplo, "nuevo-checkout") -- único dentro del
    /// tenant, ver el índice compuesto <c>(TenantId, Nombre)</c> en <see cref="FeatureManagementDbContext"/>.</summary>
    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    /// <summary>Estado global del flag: si es <see langword="false"/>, está apagado para TODOS los
    /// contextos de evaluación sin importar los <see cref="Rollouts.Rollout"/> asociados (short-circuit,
    /// ver <c>EvaluarFeatureFlagQueryHandler</c>) -- el mismo criterio que un breaker de emergencia.</summary>
    public bool Activo { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public FeatureFlag(Guid id, string nombre, string? descripcion) : base(id)
    {
        Nombre = nombre;
        Descripcion = descripcion;
        Activo = false;
    }

    private FeatureFlag()
    {
    }

    /// <summary>
    /// Operación sensible de referencia del módulo (checklist Fase 6, requisito común 7 "RBAC y ABAC"):
    /// encender un flag en producción es de mayor alcance que crearlo -- ver
    /// <see cref="FeatureManagementPermissions.FlagsActivar"/> y la evaluación ABAC explícita en
    /// <c>ActivarFeatureFlagCommandHandler</c>.
    /// </summary>
    public Result Activar()
    {
        if (Activo)
        {
            return Result.Failure(Error.Conflict(
                "FeatureManagement.Flags.YaActivo", $"El flag '{Nombre}' ya está activo."));
        }

        Activo = true;
        RaiseDomainEvent(new FeatureFlagActivadoIntegrationEvent(Id, Nombre));

        return Result.Success();
    }

    /// <summary>Simétrico de <see cref="Activar"/> -- misma sensibilidad, mismo permiso distinto
    /// (<see cref="FeatureManagementPermissions.FlagsDesactivar"/>) y misma regla ABAC de alcance.</summary>
    public Result Desactivar()
    {
        if (!Activo)
        {
            return Result.Failure(Error.Conflict(
                "FeatureManagement.Flags.YaInactivo", $"El flag '{Nombre}' ya está inactivo."));
        }

        Activo = false;
        RaiseDomainEvent(new FeatureFlagDesactivadoIntegrationEvent(Id, Nombre));

        return Result.Success();
    }
}
