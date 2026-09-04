using BitCode.Framework.Shared.Infrastructure.Web.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registra GlobalExceptionHandler + ProblemDetails (RFC 7807). El proyecto consumidor todavía
    /// debe llamar app.UseExceptionHandler() en el pipeline (Program.cs) para activarlo.
    /// </summary>
    public static IServiceCollection AddSharedExceptionHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }
}
