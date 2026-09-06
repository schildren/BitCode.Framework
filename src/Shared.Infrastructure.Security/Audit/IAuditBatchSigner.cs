using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Firma y verificación independiente de lotes de auditoría (F2-17, Épica F2-D, entregable "Firma y
/// timestamp", criterio de aceptación "Verificación independiente"). Pieza ADICIONAL y componible sobre
/// F2-15/F2-16 -- no reemplaza ni modifica <see cref="IAuditWriter"/>, <see cref="AuditEntry"/> ni <see
/// cref="IAuditIntegrityVerifier"/>:
/// <list type="bullet">
/// <item><see cref="IAuditIntegrityVerifier"/> (F2-16) detecta que un registro ya escrito fue alterado o que
/// la secuencia fue reordenada/recortada, RECALCULANDO los mismos hashes SHA-256 con el mismo algoritmo
/// público (<see cref="AuditHashCalculator"/>) que cualquiera puede ejecutar -- por diseño, no protege
/// contra un atacante con acceso de escritura al almacenamiento subyacente que reconstruya una cadena
/// alternativa completa desde cero, recalculando todos los hashes de la misma forma (ver "Qué NO resuelve
/// F2-16" en <c>docs/guia-auditoria-inmutable.md</c>).</item>
/// <item><see cref="IAuditBatchSigner"/> cierra ese hueco: la firma de un lote requiere una clave que la
/// verificación de integridad de F2-16 nunca necesita y que un atacante con acceso de solo escritura al
/// almacenamiento de auditoría (pero no al proveedor de secretos donde vive la clave de firma) no puede
/// reproducir -- cualquier alteración a cualquier campo de cualquier registro del lote, incluida una
/// reconstrucción completa y consistente de la cadena de hashes, invalida la firma porque el firmante nunca
/// participó en producir el contenido alterado.</item>
/// </list>
/// </summary>
public interface IAuditBatchSigner
{
    /// <summary>
    /// Firma <paramref name="batch"/> con la clave de firma activa vigente (<see
    /// cref="AuditBatchSigningOptions.ActiveKeyVersionSecretKey"/>). Un lote vacío es un error de uso (nada
    /// que firmar tiene valor probatorio nulo) y devuelve <c>Result.Failure</c> (<c>AuditBatchSigning.EmptyBatch</c>),
    /// no una firma "vacía". Este método NO define cuándo se firma un lote en producción (cada N registros,
    /// cada X minutos, al cierre de un período) -- esa política es responsabilidad del proyecto consumidor;
    /// tampoco persiste la firma resultante -- es responsabilidad del <see cref="IAuditWriter"/> real que un
    /// proyecto conecte, o del destino WORM de F2-18.
    /// </summary>
    Task<Result<AuditBatchSignature>> SignAsync(
        IReadOnlyList<AuditEntry> batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica de forma independiente si <paramref name="signature"/> es válida para <paramref
    /// name="batch"/> -- "independiente" en el sentido de que puede ejecutarse desde un proceso o servicio
    /// distinto del que firmó (ver <see cref="HmacAuditBatchSigner"/> para el detalle de por qué un esquema
    /// simétrico sigue satisfaciendo esa independencia dentro del mismo perímetro de confianza del proyecto
    /// consumidor). Devuelve <c>Result.Success(false)</c> (no una excepción, no un <c>Result.Failure</c>)
    /// cuando la firma es sintácticamente válida pero no corresponde al lote/clave -- una firma inválida es
    /// un resultado de negocio esperado a evaluar, igual que cualquier otro resultado de verificación del
    /// framework (<see cref="AuditIntegrityVerificationResult"/>). <c>Result.Failure</c> queda reservado
    /// para condiciones que impiden intentar la verificación (lote vacío, versión de clave de la firma ya
    /// no disponible en el proveedor de secretos).
    /// </summary>
    Task<Result<bool>> VerifyAsync(
        IReadOnlyList<AuditEntry> batch, AuditBatchSignature signature, CancellationToken cancellationToken = default);
}
