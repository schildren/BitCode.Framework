namespace BitCode.Framework.Shared.Domain.HotPaths;

/// <summary>
/// Documenta, de forma obligatoria, por qué una consulta implementa <see cref="IHotPathQuery{TResult}"/>
/// en vez de usar el camino genérico (<c>ISpecification&lt;T&gt;</c>/<c>IReadRepository&lt;,&gt;</c>,
/// F1-17). Criterio de aceptación de F1-18: "Benchmark justifica cada bypass" — <c>Justification</c>
/// explica en una frase por qué el patrón genérico no alcanza, y <c>BenchmarkRef</c> apunta a un
/// documento/prueba reproducible con los números reales que lo demuestran (por ejemplo,
/// <c>"docs/guia-hot-paths.md#benchmark"</c> o el nombre de un test de benchmark en el repo). No es
/// solo un comentario: <c>IHotPathQueryExecutor</c> (Shared.Infrastructure.Persistence) exige este
/// atributo con ambos valores no vacíos antes de ejecutar la consulta la primera vez.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HotPathAttribute(string justification, string benchmarkRef) : Attribute
{
    public string Justification { get; } = justification;

    public string BenchmarkRef { get; } = benchmarkRef;
}
