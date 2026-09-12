namespace BitCode.Framework.Shared.Kernel;

/// <summary>
/// Primitiva común de solicitud de paginación (F1-21): valida <c>page</c>/<c>pageSize</c> contra un
/// máximo configurable ANTES de construir la especificación de paginación, para que ningún endpoint
/// pueda devolver "todas las filas" simplemente subiendo <c>pageSize</c> a un valor arbitrario. A
/// diferencia del recorte defensivo de última línea que aplica <c>RepositoryBase.NormalizePaging</c>
/// (que solo protege contra un <c>pageSize</c> fuera de rango si, por descuido, algo llama al
/// repositorio sin pasar por esta primitiva), <see cref="Create"/> es el punto de validación
/// principal: un <c>pageSize</c> fuera de rango nunca se trunca en silencio, siempre devuelve un
/// <see cref="Result{TValue}"/> fallido con un <see cref="ErrorType.Validation"/> claro que el
/// endpoint traduce a <c>400 Bad Request</c> vía <c>ToProblemDetails()</c>.
/// </summary>
/// <remarks>
/// Un handler de <c>IQuery</c> que sirve un listado paginado a un endpoint HTTP debe construir un
/// <see cref="PageRequest"/> a partir de los parámetros de query del cliente (nunca pasar
/// <c>page</c>/<c>pageSize</c> crudos directamente a <c>IReadRepository&lt;,&gt;.ListPagedAsync</c>).
/// Ver <c>docs/guia-queries-eficientes.md</c> (sección "Límites máximos de paginación") y
/// <c>ListarProductosQuery</c> en <c>samples/Sample.Api</c> para el patrón de referencia.
/// </remarks>
public sealed record PageRequest
{
    /// <summary>
    /// Máximo de <see cref="PageSize"/> por defecto cuando el llamador de <see cref="Create"/> no
    /// especifica uno propio. 100 es un valor conservador para un listado servido a una grilla/tabla
    /// de UI; un endpoint con una necesidad de exportación masiva debe pasar su propio máximo
    /// explícito a <see cref="Create"/> (o, mejor, resolver un job en background) en vez de subir este
    /// valor global.
    /// </summary>
    public const int DefaultMaxPageSize = 100;

    private const int MinPage = 1;
    private const int MinPageSize = 1;

    public int Page { get; }

    public int PageSize { get; }

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// Valida <paramref name="page"/>/<paramref name="pageSize"/> y construye el <see cref="PageRequest"/>
    /// correspondiente. Nunca trunca un <paramref name="pageSize"/> fuera de rango: devuelve un
    /// <see cref="Result{TValue}"/> fallido con <see cref="ErrorType.Validation"/> para que el
    /// llamador (típicamente un handler de <c>IQuery</c>) lo propague como el <see cref="Error"/> del
    /// propio <c>Result</c> de la query, sin ejecutar ninguna consulta contra la base de datos.
    /// </summary>
    /// <param name="maxPageSize">
    /// Máximo permitido de <paramref name="pageSize"/> para este listado en particular. Configurable
    /// por llamador (por ejemplo, desde una constante del feature o desde <c>IOptions&lt;T&gt;</c> de
    /// un proyecto consumidor); por defecto <see cref="DefaultMaxPageSize"/>.
    /// </param>
    public static Result<PageRequest> Create(int page, int pageSize, int maxPageSize = DefaultMaxPageSize)
    {
        if (page < MinPage)
        {
            return Result.Failure<PageRequest>(
                Error.Validation("Paginacion.PaginaInvalida", $"La página debe ser mayor o igual a {MinPage}."));
        }

        if (pageSize < MinPageSize)
        {
            return Result.Failure<PageRequest>(
                Error.Validation("Paginacion.TamanioInvalido", $"El tamaño de página debe ser mayor o igual a {MinPageSize}."));
        }

        if (pageSize > maxPageSize)
        {
            return Result.Failure<PageRequest>(
                Error.Validation(
                    "Paginacion.TamanioExcedeLimite",
                    $"El tamaño de página ({pageSize}) no puede superar el máximo permitido ({maxPageSize})."));
        }

        return Result.Success(new PageRequest(page, pageSize));
    }
}
