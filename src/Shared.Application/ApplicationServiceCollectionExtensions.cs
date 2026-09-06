using System.Reflection;
using BitCode.Framework.Shared.Application.Behaviors;
using FluentValidation;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra MediatR (handlers descubiertos por assembly), los validadores de FluentValidation,
    /// el pipeline de behaviors en el orden Logging -> Validation -> Transaction -> Handler, y
    /// Mapster (config global + IMapper). Un DTO declara su mapeo implementando Mapster.IRegister
    /// (o Mapster.IMapFrom&lt;TSource&gt; para el caso simple de propiedades homónimas); ambos son
    /// descubiertos automáticamente por TypeAdapterConfig.Scan sobre los assemblies indicados.
    /// TransactionBehavior solo se ejecuta para requests que implementan IBaseCommand (ICommand);
    /// las queries pasan por Logging y Validation únicamente. Dentro de TransactionBehavior, solo
    /// los comandos que implementan ITransactionalCommand abren una transacción explícita con
    /// rollback coordinado; un ICommand simple persiste sus cambios vía SaveChangesAsync sin
    /// transacción explícita.
    /// </summary>
    public static IServiceCollection AddSharedApplication(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(assemblies);
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(assemblies);

        var mapsterConfig = TypeAdapterConfig.GlobalSettings;
        mapsterConfig.Scan(assemblies);
        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
