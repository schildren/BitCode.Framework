using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Identidad estable de un documento (Épica de Documents, Plan Maestro: "Metadata y clasificación"): el
/// contenido real vive en una o más <see cref="DocumentoVersion"/> inmutables (Épica de Documents:
/// "Versionado") -- este agregado nunca contiene bytes, solo metadata y el puntero a la versión vigente.
/// Es un <see cref="AggregateRoot{TId}"/> propio del módulo (mismo criterio que <c>Catalogo</c> en
/// Catalogs and Parameters/<c>FeatureFlag</c> en Feature Management, Fase 6): no reutiliza tipos ajenos de
/// otro bounded context.
/// <para>
/// <b>Autorización por recurso (Épica de Documents: "Autorización por recurso"):</b> este módulo NO
/// modela una lista de ACL embebida en el agregado -- reutiliza el mismo mecanismo ABAC de alcance
/// (<c>AttributeScopeAbacRule</c>, F2-08) ya usado por Feature Management/Catalogs/Organization: el
/// endpoint de descarga/disposición exige el permiso RBAC correspondiente y, además, evalúa
/// explícitamente <c>IAuthorizationPolicyEvaluator</c> con el atributo <c>documentoId</c> de ESTE
/// documento contra el claim de alcance del actor (configurado por el consumidor real vía
/// <c>AbacOptions.ScopeRules</c>, ver <c>docs/guia-documents.md</c>). Se decidió reutilizar el mecanismo
/// ABAC genérico del framework en vez de introducir un campo <c>PropietarioId</c>/lista de actores
/// permitidos propia de este módulo, porque (a) es exactamente el mismo problema ("¿este actor puede
/// operar ESTA instancia concreta del recurso?") que Feature Management ya resolvió con el mismo
/// mecanismo, y (b) evita duplicar en cada módulo de plataforma una tabla de permisos por recurso que el
/// framework ya provee de forma transversal.
/// </para>
/// <para>
/// <b>Retención y disposición (Épica de Documents: "Retención y disposición"):</b> <see cref="RetencionDias"/>
/// se fija al crear el documento y determina <see cref="DisponibleParaDisposicionDesde"/> -- una fecha
/// calculada, NO un job de limpieza automática (ver <c>docs/guia-documents.md</c>, sección "Pendientes",
/// mismo criterio de simplificación deliberada que <c>Retention-Cleanup.sql</c> en F5-07, que declara la
/// política de retención sin ejecutar el borrado por su cuenta).
/// </para>
/// </summary>
public sealed class Documento : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity, ISoftDelete
{
    public string Titulo { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    /// <summary>Clasificación libre del documento (por ejemplo, "Contrato", "Factura", "Identificación")
    /// -- Épica de Documents: "Metadata y clasificación". No se modela como un catálogo cerrado en este
    /// primer corte (ver <c>docs/guia-documents.md</c>, sección "Pendientes": una integración real con
    /// Catalogs and Parameters, Fase 6 módulo 3, es candidata natural para un corte futuro).</summary>
    public string Clasificacion { get; private set; } = string.Empty;

    /// <summary>Días desde la carga (<see cref="IAuditedEntity.CreatedAtUtc"/>) tras los cuales el
    /// documento puede disponerse -- ver <see cref="DisponibleParaDisposicionDesde"/>.</summary>
    public int RetencionDias { get; private set; }

    public Guid VersionActualId { get; private set; }

    public int VersionActualNumero { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    /// <summary>Fecha desde la cual este documento puede disponerse (eliminarse) según su política de
    /// retención -- calculada, no persistida (se recalcula siempre a partir de
    /// <see cref="IAuditedEntity.CreatedAtUtc"/>, que <c>AuditingSaveChangesInterceptor</c> fija en el
    /// momento de la carga inicial). <see langword="default"/> antes de persistir por primera vez (cuando
    /// <see cref="IAuditedEntity.CreatedAtUtc"/> todavía no fue asignado).</summary>
    public DateTime DisponibleParaDisposicionDesde => CreatedAtUtc.AddDays(RetencionDias);

    public Documento(Guid id, string titulo, string? descripcion, string clasificacion, int retencionDias)
        : base(id)
    {
        Titulo = titulo;
        Descripcion = descripcion;
        Clasificacion = clasificacion;
        RetencionDias = retencionDias;
    }

    private Documento()
    {
    }

    /// <summary>
    /// Registra la primera versión del documento (subida inicial) -- levanta
    /// <see cref="DocumentoSubidoIntegrationEvent"/>. Debe llamarse una única vez, inmediatamente después
    /// de construir el documento, dentro del mismo handler que crea la <see cref="DocumentoVersion"/>
    /// número 1 (<c>CrearDocumentoCommandHandler</c>).
    /// </summary>
    public Result RegistrarVersionInicial(DocumentoVersion version)
    {
        if (VersionActualId != Guid.Empty)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Documentos.YaTieneVersionInicial", $"El documento {Id} ya tiene una versión inicial."));
        }

        if (version.Numero != 1)
        {
            return Result.Failure(Error.Validation(
                "Documents.Documentos.NumeroVersionInicialInvalido", "La versión inicial debe ser la número 1."));
        }

        VersionActualId = version.Id;
        VersionActualNumero = version.Numero;

        RaiseDomainEvent(new DocumentoSubidoIntegrationEvent(Id, version.Id, version.NombreArchivo, version.HashSha256));

        return Result.Success();
    }

    /// <summary>
    /// Registra una versión posterior a la inicial (Épica de Documents: "Versionado" -- subir una versión
    /// nueva NUNCA borra ni reemplaza el contenido de la anterior, solo avanza el puntero de versión
    /// vigente). Levanta <see cref="DocumentoVersionCreadaIntegrationEvent"/>.
    /// </summary>
    public Result RegistrarNuevaVersion(DocumentoVersion version)
    {
        if (VersionActualId == Guid.Empty)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Documentos.SinVersionInicial",
                $"El documento {Id} todavía no tiene una versión inicial registrada."));
        }

        if (version.Numero != VersionActualNumero + 1)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Documentos.NumeroVersionInvalido",
                $"Se esperaba la versión {VersionActualNumero + 1}, se recibió la versión {version.Numero}."));
        }

        VersionActualId = version.Id;
        VersionActualNumero = version.Numero;

        RaiseDomainEvent(new DocumentoVersionCreadaIntegrationEvent(Id, version.Id, version.Numero, version.HashSha256));

        return Result.Success();
    }

    /// <summary>
    /// Valida que el documento puede disponerse (Épica de Documents: "Retención y disposición") -- no
    /// muta el estado por sí mismo: la baja lógica real (<see cref="ISoftDelete.IsDeleted"/>/
    /// <see cref="ISoftDelete.DeletedAtUtc"/>/<see cref="ISoftDelete.DeletedBy"/>) la aplica
    /// <c>SoftDeleteInterceptor</c> (Shared.Infrastructure.Persistence) cuando el handler llama
    /// <c>IRepository.Remove(documento)</c> -- mismo mecanismo que cualquier otra baja lógica del
    /// framework, para no duplicar la lógica de auditoría de quién/cuándo dispuso.
    /// </summary>
    public Result PuedeDisponerse(DateTime ahoraUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Documentos.YaDispuesto", $"El documento {Id} ya fue dispuesto."));
        }

        if (ahoraUtc < DisponibleParaDisposicionDesde)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Documentos.RetencionVigente",
                $"El documento {Id} está en período de retención hasta {DisponibleParaDisposicionDesde:O}."));
        }

        return Result.Success();
    }
}
