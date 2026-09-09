using System.Reflection;
using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Application.Idempotency;
using BitCode.Framework.Shared.Application.Inbox;
using BitCode.Framework.Shared.Domain.Idempotency;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra MediatR (handlers descubiertos por assembly), los validadores de FluentValidation,
    /// el pipeline de behaviors en el orden Logging -> Validation -> RegionalOwnership -> Transaction
    /// -> Idempotency -> Handler, y Mapster (config global + IMapper). Un DTO declara su mapeo
    /// implementando Mapster.IRegister (o Mapster.IMapFrom&lt;TSource&gt; para el caso simple de
    /// propiedades homónimas); ambos son descubiertos automáticamente por TypeAdapterConfig.Scan sobre
    /// los assemblies indicados. TransactionBehavior solo se ejecuta para requests que implementan
    /// IBaseCommand (ICommand); las queries pasan por Logging y Validation únicamente. Dentro de
    /// TransactionBehavior, solo los comandos que implementan ITransactionalCommand abren una
    /// transacción explícita con rollback coordinado; un ICommand simple persiste sus cambios vía
    /// SaveChangesAsync sin transacción explícita. RegionalOwnershipBehavior (F5-02) solo se ejecuta
    /// para comandos IRegionalCommand, y corre ANTES de TransactionBehavior/IdempotencyBehavior para
    /// rechazar una escritura fuera de la región propietaria del tenant sin abrir ninguna transacción
    /// ni tocar ningún store de idempotencia (ver el remarks de RegionalOwnershipBehavior).
    /// IdempotencyBehavior (F1-22) solo se ejecuta para comandos IIdempotentCommand, y corre DENTRO
    /// del alcance de TransactionBehavior para que el registro de idempotencia quede en el mismo
    /// SaveChangesAsync/transacción que el efecto del comando (ver el remarks de IdempotencyBehavior).
    /// </summary>
    public static IServiceCollection AddSharedApplication(
        this IServiceCollection services,
        params Assembly[] assemblies) =>
        services.AddSharedApplication(static _ => { }, assemblies);

    /// <summary>
    /// Sobrecarga que además acepta <see cref="IdempotencyOptions"/> (F1-22: por ejemplo,
    /// <see cref="IdempotencyOptions.RetentionPeriod"/> para ajustar cuánto tiempo se conserva el
    /// resultado guardado bajo una Idempotency-Key antes de vencer).
    /// </summary>
    public static IServiceCollection AddSharedApplication(
        this IServiceCollection services,
        Action<IdempotencyOptions> configureIdempotency,
        params Assembly[] assemblies)
    {
        var idempotencyOptions = new IdempotencyOptions();
        configureIdempotency(idempotencyOptions);
        services.AddSingleton(idempotencyOptions);

        // F1-22: NullIdempotencyKeyProvider (Shared.Application) es el default análogo a
        // NullTenantProvider/NullCurrentUserProvider — un proyecto con pipeline HTTP lo reemplaza
        // llamando AddHttpContextIdempotencyKeyProvider() (Shared.Infrastructure.Web) ANTES de este
        // método, mismo patrón que AddHttpContextTenantProvider().
        services.TryAddScoped<IIdempotencyKeyProvider, NullIdempotencyKeyProvider>();

        // F1-24 (Inbox base): mecanismo genérico de deduplicación para un futuro consumidor de
        // mensajería (Fase 3) — no depende de MediatR, se resuelve como un servicio de scope normal,
        // invocado directamente por el consumidor por cada mensaje recibido (no es un pipeline
        // behavior, a diferencia de IdempotencyBehavior, porque un mensaje de Inbox no llega como un
        // IRequest de MediatR).
        services.AddScoped<IInboxMessageProcessor, InboxMessageProcessor>();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(assemblies);
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(RegionalOwnershipBehavior<,>));
            config.AddOpenBehavior(typeof(TransactionBehavior<,>));
            config.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(assemblies);

        var mapsterConfig = TypeAdapterConfig.GlobalSettings;
        mapsterConfig.Scan(assemblies);
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
