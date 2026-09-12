namespace BitCode.Framework.Tools.Migrations.Core.DataReconciliation;

/// <summary>
/// Descripción de una tabla física derivada del modelo de EF Core (Fase 9, F9-08 "Data migration"):
/// nombre calificado, columnas en orden estable (alfabético) y columnas de clave primaria usadas para
/// ordenar filas de forma determinística antes de comparar contenido.
/// </summary>
/// <remarks>
/// El orden alfabético de <see cref="Columns"/> (en vez del orden de declaración del modelo) es
/// deliberado: hace que el hash de fila no dependa de que ambos lados del proceso de reconciliación
/// hayan cargado exactamente el mismo ensamblado/versión de la entidad, siempre que el nombre físico de
/// columna sea el mismo.
/// </remarks>
public sealed record TableSchema(
    string? SchemaName,
    string TableName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> KeyColumns)
{
    public string QualifiedName => string.IsNullOrWhiteSpace(SchemaName)
        ? Quote(TableName)
        : $"{Quote(SchemaName)}.{Quote(TableName)}";

    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
