using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.DeadLetter;

public static class DeadLetterReprocessorServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="OutboxDeadLetterReprocessor"/> como <see cref="IDeadLetterReprocessor"/>
    /// (F3-08, <c>Scoped</c> -- reutiliza el mismo <c>DbContext</c> de scope que
    /// <c>AddSharedPersistence&lt;TContext&gt;</c> ya registra, igual que <c>OutboxBatchProcessor</c>).
    /// Requiere que ya exista un <c>IAuditWriter</c> registrado (<c>AddSharedAuditing</c>, F2-15) --
    /// llamar este método DESPUÉS de <c>AddSharedPersistence&lt;TContext&gt;</c> y de
    /// <c>AddSharedAuditing</c>.
    /// </summary>
    public static IServiceCollection AddSharedDeadLetterReprocessing(this IServiceCollection services)
    {
        services.TryAddScoped<IDeadLetterReprocessor, OutboxDeadLetterReprocessor>();
        return services;
    }
}
