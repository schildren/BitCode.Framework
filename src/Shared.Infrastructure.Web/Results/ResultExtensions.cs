using BitCode.Framework.Shared.Kernel;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Shared.Infrastructure.Web.Results;

/// <summary>
/// Cierra el ciclo del patrón Result&lt;T&gt; (Fase 2) en el borde HTTP: un handler de MediatR nunca
/// lanza excepciones para errores de negocio esperados, y el Endpoint solo necesita traducir el
/// Result final a una respuesta. Un Result exitoso deja la respuesta al llamador (Ok/Created/etc.);
/// uno fallido siempre se traduce aquí a un ProblemDetails con el status HTTP correspondiente al
/// ErrorType.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("No se puede convertir un Result exitoso a un problema HTTP.");
        }

        var statusCode = MapStatusCode(result.Error.Type);

        if (result.Error is ValidationError validationError)
        {
            var errorsDictionary = validationError.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Microsoft.AspNetCore.Http.TypedResults.ValidationProblem(errorsDictionary);
        }

        return Microsoft.AspNetCore.Http.TypedResults.Problem(
            title: result.Error.Code,
            detail: result.Error.Description,
            statusCode: statusCode);
    }

    public static IResult ToOkOrProblem<TValue>(this Result<TValue> result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.TypedResults.Ok(result.Value) : result.ToProblemDetails();

    private static int MapStatusCode(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError,
    };
}
