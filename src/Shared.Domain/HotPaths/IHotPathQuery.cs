namespace BitCode.Framework.Shared.Domain.HotPaths;

/// <summary>
/// Contrato de extensión para un hot path (F1-18): una consulta especializada que el patrón genérico
/// <c>ISpecification&lt;T&gt;</c>/<c>IReadRepository&lt;,&gt;</c> (F1-17) no puede expresar de forma
/// eficiente — por ejemplo, un agregado que exige un único <c>SELECT COUNT/SUM</c> en la base de datos
/// en vez de materializar todas las filas filtradas para agregarlas en memoria.
/// </summary>
/// <remarks>
/// Una clase que implemente esta interfaz **debe** llevar el atributo
/// <see cref="HotPathAttribute"/> con una justificación y una referencia a un benchmark real (criterio
/// de aceptación de F1-18: "Benchmark justifica cada bypass") — <c>IHotPathQueryExecutor</c>
/// (Shared.Infrastructure.Persistence) lo valida en tiempo de ejecución la primera vez que se ejecuta
/// cada tipo, y lanza <see cref="InvalidOperationException"/> si falta o está incompleto.
/// <para/>
/// <see cref="ToSql"/> devuelve un <see cref="FormattableString"/> (no una concatenación de strings):
/// el ejecutor lo pasa a <c>Database.SqlQuery&lt;TResult&gt;</c> de EF Core, que parametriza cada
/// interpolación igual que <c>FromSqlInterpolated</c> — nunca inyección SQL, incluso si el valor
/// interpolado viene de un usuario final.
/// <para/>
/// El resultado (<typeparamref name="TResult"/>) es siempre un DTO materializado, nunca una entidad de
/// dominio ni un <c>IQueryable</c> abierto — coherente con la regla dura #5 de
/// <c>docs/convenciones.md</c> ("nunca exponer <c>IQueryable</c> desde un repositorio").
/// </remarks>
public interface IHotPathQuery<TResult>
    where TResult : class
{
    FormattableString ToSql();
}
