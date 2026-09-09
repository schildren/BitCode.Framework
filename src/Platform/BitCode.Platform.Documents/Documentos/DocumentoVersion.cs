using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.Documents.Documentos;

/// <summary>
/// Estado de escaneo antivirus de una <see cref="DocumentoVersion"/> (Épica de Documents, Plan Maestro:
/// "Escaneo antivirus"). Una versión nace SIEMPRE en <see cref="PendienteEscaneo"/> -- ningún consumidor
/// puede asumir que un archivo recién cargado ya fue analizado -- y transiciona una única vez a
/// <see cref="Limpio"/> o <see cref="Infectado"/> vía <see cref="DocumentoVersion.MarcarEscaneada"/>. NO
/// hay integración real con un motor antivirus (ClamAV, Windows Defender, etc.) en este primer corte --
/// eso es infraestructura externa fuera de alcance (ver <see cref="IAntivirusScanner"/> y
/// <c>docs/guia-documents.md</c>, sección "Pendientes"); lo que SÍ está completo y probado es el flujo de
/// negocio que depende de este estado: una versión <see cref="Infectado"/> nunca puede descargarse
/// (<c>DescargarDocumentoVersionQueryHandler</c>).
/// </summary>
public enum EstadoEscaneo
{
    PendienteEscaneo = 0,
    Limpio = 1,
    Infectado = 2,
}

/// <summary>
/// Una versión concreta e INMUTABLE del contenido de un <see cref="Documento"/> (Épica de Documents):
/// subir una versión nueva nunca borra ni modifica una anterior, solo agrega una fila nueva -- mismo
/// criterio que <c>CatalogoVersion</c> (Catalogs and Parameters, Fase 6 módulo 3). Es un
/// <see cref="AggregateRoot{TId}"/> propio (no anidado dentro de <see cref="Documento"/>) porque levanta
/// eventos de integración propios (<see cref="DocumentoEscaneadoIntegrationEvent"/>) y porque el resultado
/// del escaneo antivirus es un hecho de negocio con ciclo de vida propio, independiente del ciclo de vida
/// del <see cref="Documento"/> que la contiene.
/// <para>
/// <see cref="HashSha256"/> cumple una doble función (Épica de Documents, "Hash de contenido"): verificar
/// integridad en la descarga (recalcular el hash del contenido leído del almacenamiento y compararlo
/// contra este valor detectaría corrupción/tampering) y detectar duplicados exactos entre versiones. No
/// se recalcula automáticamente en cada descarga en este primer corte (ver
/// <c>docs/guia-documents.md</c>, sección "Pendientes") -- el campo existe y se persiste, la
/// verificación activa en el camino de lectura queda pendiente.
/// </para>
/// </summary>
public sealed class DocumentoVersion : AggregateRoot<Guid>, ITenantEntity, IAuditedEntity
{
    public Guid DocumentoId { get; private set; }

    /// <summary>Número incremental dentro del documento (1, 2, 3, ...) -- único por
    /// <c>(DocumentoId, Numero)</c>, ver el índice de <see cref="DocumentsDbContext"/> (guardrail de
    /// datos contra la condición de carrera de <c>SubirVersionDocumentoCommandHandler</c>, mismo criterio
    /// documentado en Feature Management/Catalogs para el mismo tipo de check-then-act).</summary>
    public int Numero { get; private set; }

    public string NombreArchivo { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long TamanioBytes { get; private set; }

    /// <summary>Hash SHA-256 del contenido, en hexadecimal mayúsculas (<c>Convert.ToHexString</c>).</summary>
    public string HashSha256 { get; private set; } = string.Empty;

    /// <summary>Clave opaca que identifica el contenido dentro de <see cref="IDocumentBlobStore"/> --
    /// nunca una ruta de filesystem cruda expuesta fuera del módulo (Épica de Documents: "Integración con
    /// almacenamiento desacoplado").</summary>
    public string BlobKey { get; private set; } = string.Empty;

    public EstadoEscaneo EstadoEscaneo { get; private set; } = EstadoEscaneo.PendienteEscaneo;

    public DateTime? EscaneadaAtUtc { get; private set; }

    public Guid TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    public DocumentoVersion(
        Guid id,
        Guid documentoId,
        int numero,
        string nombreArchivo,
        string contentType,
        long tamanioBytes,
        string hashSha256,
        string blobKey)
        : base(id)
    {
        DocumentoId = documentoId;
        Numero = numero;
        NombreArchivo = nombreArchivo;
        ContentType = contentType;
        TamanioBytes = tamanioBytes;
        HashSha256 = hashSha256;
        BlobKey = blobKey;
    }

    private DocumentoVersion()
    {
    }

    /// <summary>
    /// Registra el resultado del escaneo antivirus (operación de sistema, invocada por el propio handler
    /// de subida en este primer corte -- ver <see cref="IAntivirusScanner"/> -- nunca por un actor HTTP
    /// directamente). Transición única e irreversible desde <see cref="EstadoEscaneo.PendienteEscaneo"/>:
    /// re-escanear una versión ya escaneada exigiría una nueva versión, no una regla de negocio de este
    /// primer corte.
    /// </summary>
    public Result MarcarEscaneada(EstadoEscaneo resultado)
    {
        if (resultado == EstadoEscaneo.PendienteEscaneo)
        {
            return Result.Failure(Error.Validation(
                "Documents.Versiones.ResultadoEscaneoInvalido",
                "El resultado del escaneo debe ser Limpio o Infectado."));
        }

        if (EstadoEscaneo != EstadoEscaneo.PendienteEscaneo)
        {
            return Result.Failure(Error.Conflict(
                "Documents.Versiones.YaEscaneada", $"La versión {Id} ya fue escaneada."));
        }

        EstadoEscaneo = resultado;
        EscaneadaAtUtc = DateTime.UtcNow;

        RaiseDomainEvent(new DocumentoEscaneadoIntegrationEvent(DocumentoId, Id, Numero, resultado));

        return Result.Success();
    }

    /// <summary>
    /// Guardrail de negocio central del requisito "Escaneo antivirus" (Épica de Documents): una versión
    /// <see cref="EstadoEscaneo.Infectado"/>, o todavía no escaneada, nunca se entrega -- ver
    /// <c>DescargarDocumentoVersionQueryHandler</c>.
    /// </summary>
    public bool PuedeDescargarse() => EstadoEscaneo == EstadoEscaneo.Limpio;
}
