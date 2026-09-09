using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

/// <summary>
/// Un valor concreto de un <see cref="Parametro"/>, válido durante una ventana de tiempo
/// (<see cref="VigenteDesde"/>/<see cref="VigenteHasta"/>, Fase 6, módulo Catalogs and Parameters). Dos
/// vigencias del MISMO parámetro nunca se solapan en el tiempo -- validado explícitamente por
/// <c>CrearParametroVigenciaCommandHandler</c> ANTES de persistir (error de negocio esperado,
/// <c>Result.Failure</c>, nunca una excepción ni una restricción de base de datos que dependa de una
/// carrera). Es un <see cref="AggregateRoot{TId}"/> propio (no anidado dentro de <see cref="Parametro"/>)
/// porque levanta un evento de integración real al crearse.
/// </summary>
public sealed class ParametroVigencia : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid ParametroId { get; private set; }

    public string Valor { get; private set; } = string.Empty;

    public DateTime VigenteDesde { get; private set; }

    /// <summary><see langword="null"/> significa "vigente indefinidamente hacia adelante" hasta que una
    /// vigencia posterior la reemplace -- este corte no soporta "cerrar" una vigencia después de creada,
    /// solo evita solapamientos al momento del alta (ver pendiente en <c>docs/guia-catalogs.md</c>).</summary>
    public DateTime? VigenteHasta { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public ParametroVigencia(Guid id, Guid parametroId, string valor, DateTime vigenteDesde, DateTime? vigenteHasta) : base(id)
    {
        ParametroId = parametroId;
        Valor = valor;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;

        RaiseDomainEvent(new ParametroVigenciaCreadaIntegrationEvent(Id, ParametroId, Valor, VigenteDesde, VigenteHasta));
    }

    private ParametroVigencia()
    {
    }
}
