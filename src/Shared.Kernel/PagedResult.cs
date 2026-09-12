namespace BitCode.Framework.Shared.Kernel;

/// <summary>
/// Resultado de una consulta paginada: la página de elementos solicitada más los metadatos
/// necesarios para construir la siguiente petición (número de página, tamaño y total de filas
/// que cumplen el filtro, sin necesidad de traer todas las filas a memoria). Primitiva genérica
/// de paginación (F1-17) — no impone todavía ningún límite máximo de tamaño de página ni
/// paginación por cursor: eso es responsabilidad de cada endpoint hasta que F1-21 incorpore
/// límites máximos y cursores como política transversal.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }
}
