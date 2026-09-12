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

        // F1-10 (timeouts y cancelación): si el cliente cerró la conexión o canceló el request antes
        // de que terminara, no hay forma de escribir una respuesta a esa conexión (y no tiene sentido
        // hacerlo). Tratar esto como un error 500 lo confunde con una falla real del servidor y
        // ensucia los dashboards de errores con cancelaciones esperadas del cliente — se loguea en un
        // nivel bajo y se evita escribir sobre una conexión ya abortada.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request cancelado por el cliente antes de finalizar. TraceId: {TraceId}",
                traceId);
            return true;
        }

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
