namespace BitCode.Framework.Tools.Migrations.Core.DataReconciliation;

/// <summary>
/// Detalle de la primera fila donde el contenido de origen y destino difiere (Fase 9, F9-08). Se reporta
/// solo la primera discrepancia por tabla de forma deliberada: una vez que se sabe que una tabla no está
/// conciliada, el operador debe corregir la causa raíz (copia incompleta, transformación incorrecta,
/// escritura concurrente durante la ventana de migración) y volver a ejecutar la reconciliación completa,
/// en vez de recibir un listado potencialmente enorme de filas divergentes en un solo reporte.
/// </summary>
public sealed record RowMismatch(
    long RowIndex,
    string KeyDescription,
    string SourceRowHash,
    string TargetRowHash);

/// <summary>
/// Resultado de reconciliar una única tabla entre origen y destino: conteo de filas y hash agregado de
/// contenido en cada lado, más el detalle de la primera fila divergente si corresponde.
/// </summary>
/// <remarks>
/// <para>
/// <b>Granularidad del hash:</b> <see cref="SourceAggregateHash"/>/<see cref="TargetAggregateHash"/> son
/// un hash SHA-256 de tabla completa, calculado como el hash acumulado (streaming, sin materializar la
/// tabla en memoria) de los hashes SHA-256 de cada fila individual, en el orden determinístico dado por la
/// clave primaria. Esto significa que:
/// </para>
/// <list type="bullet">
/// <item>Un hash de tabla distinto SÍ garantiza que el contenido difiere en algún lugar (dos tablas con
/// el mismo conjunto de filas, en el mismo orden de clave primaria, producen siempre el mismo hash).</item>
/// <item>Localizar EXACTAMENTE qué fila difiere no depende de "adivinar" a partir del hash de tabla: la
/// herramienta ya calculó el hash por fila en el camino, así que <see cref="FirstMismatch"/> reporta la
/// primera fila (por orden de clave primaria) cuyo hash no coincide entre origen y destino, sin necesidad
/// de un segundo paso de búsqueda binaria ni de releer las tablas.</item>
/// </list>
/// </remarks>
public sealed record TableReconciliationResult(
    string TableName,
    long SourceCount,
    long TargetCount,
    string SourceAggregateHash,
    string TargetAggregateHash,
    RowMismatch? FirstMismatch,
    string? Error)
{
    public bool CountsMatch => Error is null && SourceCount == TargetCount;

    public bool HashesMatch => Error is null && FirstMismatch is null
        && string.Equals(SourceAggregateHash, TargetAggregateHash, StringComparison.Ordinal);

    public bool IsReconciled => Error is null && CountsMatch && HashesMatch;
}

/// <summary>
/// Reporte completo de una corrida de reconciliación (Fase 9, F9-08): un resultado por tabla descubierta
/// en el modelo del <c>DbContext</c> reconciliado.
/// </summary>
public sealed record ReconciliationReport(IReadOnlyList<TableReconciliationResult> Tables)
{
    public bool IsFullyReconciled => Tables.Count > 0 && Tables.All(t => t.IsReconciled);
}
