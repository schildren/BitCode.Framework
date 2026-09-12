using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Decorador de <see cref="IAuditWriter"/> (F2-19, Épica F2-D): aplica <see cref="IAuditRedactionPolicy"/>
/// sobre el <see cref="AuditEntryRequest"/> recibido ANTES de delegar en el <see cref="IAuditWriter"/>
/// decorado -- mismo patrón de decoración que <see cref="Permissions.CachedPermissionService"/> (F2-09).
/// <para>
/// <b>Orden de operaciones, crítico para no romper F2-16/F2-17</b>: la redacción ocurre acá, ANTES de que
/// el <see cref="AuditEntryRequest"/> llegue al <see cref="IAuditWriter"/> real -- el punto donde
/// <see cref="AuditHashCalculator.Compute"/> calcula <see cref="AuditEntry.AuditHash"/> (F2-15/F2-16) y
/// donde, más tarde, un lote se firma (F2-17) o se exporta a WORM (F2-18). Redactar DESPUÉS de calcular el
/// hash dejaría dos problemas simultáneos: (a) el hash/firma quedaría calculado sobre el dato SIN redactar
/// (el propio valor sensible sigue siendo recuperable a partir de lo que efectivamente se firmó, aunque el
/// registro persistido lo muestre redactado), o (b) si en cambio se redactara el <see cref="AuditEntry"/>
/// ya construido sin recalcular, el hash almacenado dejaría de corresponder al contenido persistido y
/// <see cref="IAuditIntegrityVerifier.Verify"/> reportaría un falso <c>HashMismatch</c> sobre un registro
/// que en realidad nunca fue manipulado por un tercero. Decorando <see cref="IAuditWriter"/> en esta capa
/// (la más externa: envuelve el escritor real, cualquiera sea) se garantiza que NINGÚN <see
/// cref="IAuditWriter"/> -- ni <see cref="InMemoryAuditWriter"/> ni una implementación productiva futura --
/// recibe jamás el valor sin redactar, y que el hash/firma calculados corresponden siempre a los datos YA
/// redactados.
/// </para>
/// </summary>
public sealed class RedactingAuditWriter(IAuditWriter inner, IAuditRedactionPolicy redactionPolicy) : IAuditWriter
{
    public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var redactedRequest = redactionPolicy.Redact(request);
        return inner.WriteAsync(redactedRequest, cancellationToken);
    }
}
