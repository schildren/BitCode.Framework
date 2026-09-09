using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Organization.Empresas;

/// <summary>
/// Raíz de la jerarquía organizacional (Fase 6, módulo Organization): Empresa -&gt; Sucursal -&gt; Área
/// -&gt; Cargo (4 niveles fijos, ver <c>docs/guia-organization.md</c>, sección "Modelo de jerarquía" --
/// deliberadamente no es un grafo genérico, el Plan Maestro solo exige esos 4 niveles). Es un
/// <see cref="AggregateRoot{TId}"/> propio del módulo (a diferencia de Identity Administration, que
/// reutilizaba tipos ajenos de Security 2.0 sin poder levantar eventos de dominio reales): las
/// operaciones de negocio de esta clase levantan los eventos que <c>OutboxSaveChangesInterceptor</c>
/// (F1-23) persiste atómicamente junto con el cambio.
/// </summary>
public sealed class Empresa : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public string RazonSocial { get; private set; } = string.Empty;

    /// <summary>Identificador fiscal (CUIT/RUC/NIF, según el país) -- único a nivel de negocio dentro
    /// del tenant, no validado con formato específico de país en este primer corte (ver pendiente en
    /// la guía).</summary>
    public string Identificador { get; private set; } = string.Empty;

    public bool Activa { get; private set; } = true;

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public Empresa(Guid id, string razonSocial, string identificador) : base(id)
    {
        RazonSocial = razonSocial;
        Identificador = identificador;

        RaiseDomainEvent(new EmpresaCreadaIntegrationEvent(Id, RazonSocial, Identificador));
    }

    private Empresa()
    {
    }

    /// <summary>
    /// Operación sensible de referencia del módulo (checklist Fase 6, requisito común 7): desactivar
    /// una empresa completa es una operación de mayor alcance que crear una sucursal, requiere un
    /// permiso RBAC distinto y más restrictivo
    /// (<see cref="OrganizationPermissions.EmpresasDesactivar"/>) y, además, una regla ABAC explícita
    /// evaluada por el handler -- ver <c>DesactivarEmpresaCommandHandler</c>. Es idempotente a nivel de
    /// dominio: desactivar una empresa ya inactiva no levanta un segundo evento.
    /// </summary>
    public void Desactivar()
    {
        if (!Activa)
        {
            return;
        }

        Activa = false;
        RaiseDomainEvent(new EmpresaDesactivadaIntegrationEvent(Id));
    }
}
