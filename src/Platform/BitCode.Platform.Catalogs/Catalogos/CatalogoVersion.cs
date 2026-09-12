using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

/// <summary>Estado de una <see cref="CatalogoVersion"/>: nace en <see cref="Borrador"/> (los ítems
/// todavía se pueden cargar) y pasa a <see cref="Publicada"/> de forma irreversible al llamar
/// <see cref="CatalogoVersion.Publicar"/> -- no existe un tercer estado ni un camino de "despublicar"
/// en este primer corte (ver pendiente en <c>docs/guia-catalogs.md</c>).</summary>
public enum CatalogoVersionEstado
{
    Borrador = 0,
    Publicada = 1,
}

/// <summary>
/// Una versión concreta y numerada de un <see cref="Catalogo"/> (Fase 6, módulo Catalogs and
/// Parameters): agrupa los <see cref="CatalogoItem"/> vigentes para esa versión y declara su propia
/// vigencia (<see cref="VigenteDesde"/>/<see cref="VigenteHasta"/>) -- publicar una versión nueva NO
/// borra ni modifica la anterior, solo cierra su vigencia (<see cref="CerrarVigencia"/>), preservando
/// cualquier referencia externa ya resuelta contra el <c>Id</c> de la versión anterior. Es un
/// <see cref="AggregateRoot{TId}"/> propio (no anidado dentro de <see cref="Catalogo"/>) porque su
/// publicación levanta un evento de integración real (<see cref="Catalogos.CatalogoVersionPublicadaIntegrationEvent"/>)
/// que otros módulos consumidores del catálogo pueden necesitar.
/// </summary>
public sealed class CatalogoVersion : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid CatalogoId { get; private set; }

    /// <summary>Número incremental dentro del catálogo (1, 2, 3, ...) -- deliberadamente NO semver
    /// completo (el Plan Maestro solo exige "número de versión incremental y fechas de vigencia", no un
    /// motor de versionado semántico), ver <c>docs/guia-catalogs.md</c>.</summary>
    public int Numero { get; private set; }

    public CatalogoVersionEstado Estado { get; private set; } = CatalogoVersionEstado.Borrador;

    public DateTime? VigenteDesde { get; private set; }

    public DateTime? VigenteHasta { get; private set; }

    public DateTime? PublicadaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public CatalogoVersion(Guid id, Guid catalogoId, int numero) : base(id)
    {
        CatalogoId = catalogoId;
        Numero = numero;
    }

    private CatalogoVersion()
    {
    }

    /// <summary>
    /// Operación sensible de referencia del módulo (checklist Fase 6, requisito común 7): publicar una
    /// versión que otros módulos pueden empezar a consumir de inmediato es de mayor alcance que crear un
    /// catálogo/versión en borrador, ver <see cref="CatalogsPermissions.VersionesPublicar"/> y la
    /// evaluación ABAC explícita en <c>PublicarCatalogoVersionCommandHandler</c>. Es irreversible: una
    /// vez publicada, esta versión nunca vuelve a <see cref="CatalogoVersionEstado.Borrador"/>.
    /// </summary>
    public Result Publicar(DateTime vigenteDesde, DateTime? vigenteHasta)
    {
        if (Estado == CatalogoVersionEstado.Publicada)
        {
            return Result.Failure(Error.Conflict(
                "Catalogos.Versiones.YaPublicada", "La versión ya fue publicada anteriormente."));
        }

        if (vigenteHasta is not null && vigenteHasta <= vigenteDesde)
        {
            return Result.Failure(Error.Validation(
                "Catalogos.Versiones.VigenciaInvalida", "VigenteHasta debe ser posterior a VigenteDesde."));
        }

        Estado = CatalogoVersionEstado.Publicada;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        PublicadaAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new CatalogoVersionPublicadaIntegrationEvent(Id, CatalogoId, Numero, VigenteDesde.Value, VigenteHasta));

        return Result.Success();
    }

    /// <summary>Cierra la vigencia de una versión ya publicada (llamado sobre la versión vigente
    /// anterior al publicar una versión nueva del mismo catálogo) -- nunca sobre una versión que ya
    /// tenía <see cref="VigenteHasta"/> fijado, para no pisar un cierre explícito previo.</summary>
    public void CerrarVigencia(DateTime hasta)
    {
        if (Estado != CatalogoVersionEstado.Publicada || VigenteHasta is not null)
        {
            return;
        }

        VigenteHasta = hasta;
    }
}
