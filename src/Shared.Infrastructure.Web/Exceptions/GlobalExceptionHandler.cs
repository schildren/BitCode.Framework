using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Infrastructure.Web.Exceptions;

/// <summary>
/// Captura cualquier excepción no manejada y la traduce a un ProblemDetails RFC 7807 estándar,
/// con TraceId para correlacionar el error con los logs/trazas. Registrar vía
/// AddExceptionHandler&lt;GlobalExceptionHandler&gt;() + AddProblemDetails() y app.UseExceptionHandler().
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(exception, "Excepción no manejada. TraceId: {TraceId}", traceId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Ha ocurrido un error inesperado",
                status = StatusCodes.Status500InternalServerError,
                traceId,
            },
            cancellationToken);

        return true;
    }
}
