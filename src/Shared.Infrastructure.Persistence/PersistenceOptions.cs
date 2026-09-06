namespace BitCode.Framework.Shared.Infrastructure.Persistence;

/// <summary>
/// Opciones de configuración de <see cref="PersistenceServiceCollectionExtensions.AddSharedPersistence{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, System.Action{PersistenceOptions})"/>.
/// </summary>
/// <remarks>
/// F1-10 (timeouts y cancelación): el <see cref="System.Threading.CancellationToken"/> propagado por
/// el cliente (o por un futuro middleware de timeout) ya limita cuánto puede tardar un request de
/// punta a punta, pero no protege contra un único comando SQL que se cuelga del lado del servidor sin
/// que el cliente cancele nada (por ejemplo, un bloqueo inesperado en SQL Server). <see cref="CommandTimeoutSeconds"/>
/// es el límite de tiempo, independiente de la cancelación del cliente, que ADO.NET aplica a cada
/// comando individual antes de abortarlo con un <c>SqlException</c> de timeout. Si no se configura,
/// se usa el default del proveedor de EF Core para SQL Server (equivalente al de <c>SqlCommand</c>,
/// 30 segundos).
/// </remarks>
public class PersistenceOptions
{
    /// <summary>
    /// Límite de tiempo, en segundos, para cada comando SQL individual (independiente del
    /// <see cref="System.Threading.CancellationToken"/> del request). <c>null</c> (default) deja el
    /// valor por defecto del proveedor de EF Core para SQL Server sin modificar.
    /// </summary>
    public int? CommandTimeoutSeconds { get; set; }
}
