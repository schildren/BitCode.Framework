using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

/// <summary>Los dos tipos de criterio de pertenencia soportados en este primer corte (Fase 6, módulo
/// Feature Management) -- deliberadamente acotado a 2, no un DSL de reglas arbitrario (ver el pedido
/// explícito del Plan Maestro de "mantenerlo simple: 2-3 tipos de criterio"). Un tercer tipo (por
/// ejemplo, "por claim arbitrario") queda documentado como pendiente explícito en
/// <c>docs/guia-feature-management.md</c>.</summary>
public enum SegmentoTipo
{
    /// <summary>Pertenece el contexto cuyo <c>TenantId</c> coincide exactamente con
    /// <see cref="Segmento.TenantIdCriterio"/>.</summary>
    PorTenant = 0,

    /// <summary>Pertenece un porcentaje determinístico y estable de contextos (F1-17 no aplica acá, es
    /// un hash, no una proyección de columnas) -- ver <c>PorcentajeRolloutHasher</c> para el algoritmo
    /// exacto de bucketing.</summary>
    PorPorcentaje = 1,
}

/// <summary>
/// Define un criterio de pertenencia de audiencia (Fase 6, módulo Feature Management): un
/// <see cref="Rollouts.Rollout"/> asocia un <see cref="Flags.FeatureFlag"/> con uno o más segmentos, y
/// <c>EvaluarFeatureFlagQueryHandler</c> decide si un contexto de evaluación concreto pertenece a alguno
/// de ellos. Es un <see cref="AggregateRoot{TId}"/> propio del módulo -- no levanta eventos de dominio
/// propios en este corte (a diferencia de <see cref="Flags.FeatureFlag"/>/<see cref="Rollouts.Rollout"/>,
/// que sí notifican hechos de negocio que otros módulos pueden necesitar reaccionar; la sola existencia
/// de un segmento sin asociarlo a un rollout no tiene efecto de negocio observable todavía).
/// </summary>
public sealed class Segmento : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public string Nombre { get; private set; } = string.Empty;

    public SegmentoTipo Tipo { get; private set; }

    /// <summary>Solo tiene valor cuando <see cref="Tipo"/> es <see cref="SegmentoTipo.PorTenant"/>.</summary>
    public Guid? TenantIdCriterio { get; private set; }

    /// <summary>Solo tiene valor (0-100 inclusive) cuando <see cref="Tipo"/> es
    /// <see cref="SegmentoTipo.PorPorcentaje"/>.</summary>
    public int? Porcentaje { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    private Segmento()
    {
    }

    private Segmento(Guid id, string nombre, SegmentoTipo tipo, Guid? tenantIdCriterio, int? porcentaje) : base(id)
    {
        Nombre = nombre;
        Tipo = tipo;
        TenantIdCriterio = tenantIdCriterio;
        Porcentaje = porcentaje;
    }

    public static Result<Segmento> CrearPorTenant(Guid id, string nombre, Guid tenantIdCriterio)
    {
        if (tenantIdCriterio == Guid.Empty)
        {
            return Result.Failure<Segmento>(Error.Validation(
                "FeatureManagement.Segmentos.TenantCriterioInvalido", "El tenant de criterio no puede ser vacío."));
        }

        return new Segmento(id, nombre, SegmentoTipo.PorTenant, tenantIdCriterio, null);
    }

    public static Result<Segmento> CrearPorPorcentaje(Guid id, string nombre, int porcentaje)
    {
        if (porcentaje is < 0 or > 100)
        {
            return Result.Failure<Segmento>(Error.Validation(
                "FeatureManagement.Segmentos.PorcentajeInvalido", "El porcentaje debe estar entre 0 y 100."));
        }

        return new Segmento(id, nombre, SegmentoTipo.PorPorcentaje, null, porcentaje);
    }
}
