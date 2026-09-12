using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BitCode.Framework.Shared.Application.Behaviors;

/// <summary>
/// Persiste los cambios de un comando (TRequest : IBaseCommand) al finalizar el pipeline. Se aplica
/// únicamente a comandos: las queries son de solo lectura por convención y no necesitan overhead de
/// persistencia. Solo los comandos que implementan explícitamente <see cref="ITransactionalCommand"/>
/// abren una transacción real de base de datos con rollback coordinado ante fallo; un
/// <see cref="IBaseCommand"/> simple confía en que <see cref="IUnitOfWork.SaveChangesAsync"/> por sí
/// solo ya es atómico para el conjunto de cambios rastreados en ese único <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// F1-07 (endurecimiento del pipeline transaccional): la transacción real de un
/// <see cref="ITransactionalCommand"/> se abre lo más tarde posible (inmediatamente antes de invocar
/// el handler, nunca antes en el pipeline — <c>LoggingBehavior</c> y <c>ValidationBehavior</c> corren
/// antes de este behavior y no ven la transacción abierta) y se cierra lo más pronto posible: ante
/// éxito, <c>CommitAsync</c> se llama de inmediato; ante fallo o excepción, <c>RollbackAsync</c> se
/// llama antes de cualquier logging, para no demorar la liberación de locks con trabajo no esencial.
/// Ningún handler ni este behavior deben realizar llamadas HTTP salientes, a cache distribuido o a un
/// broker de mensajería mientras la transacción sigue abierta (ver regla dura 3 en
/// <c>docs/convenciones.md</c>): esas llamadas deben diferirse hasta después del commit.
/// </remarks>
public class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand, IRequest<TResponse>
    where TResponse : Result
{
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken) =>
        request is ITransactionalCommand
            ? HandleTransactionalAsync(next, cancellationToken)
            : HandleSimpleAsync(next, cancellationToken);

    private async Task<TResponse> HandleSimpleAsync(
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (!response.IsSuccess)
        {
            return response;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            // F1-08: un conflicto de concurrencia optimista es un error de negocio esperado (alguien
            // más ya modificó el registro), no una excepción inesperada — se traduce aquí mismo a un
            // Result.Failure uniforme en vez de dejarlo propagar hasta GlobalExceptionHandler.
            logger.LogInformation(
                exception,
                "{RequestName} generó un conflicto de concurrencia al guardar cambios",
                typeof(TRequest).Name);
            return CreateConcurrencyConflictResult<TResponse>();
        }

        return response;
    }

    private async Task<TResponse> HandleTransactionalAsync(
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();

            if (response.IsSuccess)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            else
            {
                // Revertir primero y recién después loguear: mientras la transacción sigue abierta,
                // cualquier trabajo no esencial (incluido escribir a un sink de logging remoto)
                // extiende innecesariamente la ventana de bloqueo en el motor de base de datos.
                await unitOfWork.RollbackAsync(cancellationToken);
                logger.LogInformation(
                    "{RequestName} devolvió un Result fallido ({ErrorCode}); se revirtió la transacción",
                    requestName,
                    response.Error.Code);
            }

            return response;
        }
        catch (ConcurrencyConflictException exception)
        {
            // F1-08: mismo criterio de "manejo uniforme" que HandleSimpleAsync — un conflicto de
            // concurrencia optimista no debe propagarse como excepción sin controlar hasta
            // GlobalExceptionHandler (que lo trataría como 500); se revierte la transacción y se
            // traduce a un Result.Failure con Error.Type = Conflict (409).
            await unitOfWork.RollbackAsync(cancellationToken);
            logger.LogInformation(
                exception,
                "{RequestName} generó un conflicto de concurrencia; se revirtió la transacción",
                requestName);
            return CreateConcurrencyConflictResult<TResponse>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // F1-10 (timeouts y cancelación): la cancelación de un request (cliente que cierra la
            // conexión, o un timeout del lado del servidor) NO es un error de negocio ni una excepción
            // inesperada — es un camino normal del ciclo de vida del request. Se revierte la
            // transacción igual que ante cualquier otro fallo (para no dejar locks/transacciones
            // huérfanas en SQL Server), pero se relanza tal cual (nunca se traduce a un
            // Result.Failure/ProblemDetails) para que ASP.NET Core la trate como cancelación del
            // request, no como un error 500 genérico; y se loguea en un nivel bajo (no LogError) para
            // no ensuciar los dashboards de errores con cancelaciones esperadas.
            await unitOfWork.RollbackAsync(CancellationToken.None);
            logger.LogInformation(
                "{RequestName} fue cancelado; se revirtió la transacción",
                requestName);
            throw;
        }
        catch (Exception exception)
        {
            // Mismo criterio: cerrar la transacción (liberar locks) antes de cualquier trabajo
            // adicional. El logging de la excepción no debe demorar el rollback.
            await unitOfWork.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Excepción en {RequestName}; se revirtió la transacción", requestName);
            throw;
        }
    }

    /// <summary>
    /// Construye un <c>Result.Failure</c>/<c>Result&lt;TValue&gt;.Failure</c> con
    /// <see cref="ConcurrencyError.Conflict"/> del tipo concreto <typeparamref name="TResult"/>
    /// (mismo patrón de reflexión que <c>ValidationBehavior.CreateValidationFailureResult</c>, ya que
    /// <typeparamref name="TResult"/> puede ser <c>Result</c> o <c>Result&lt;TValue&gt;</c> según el
    /// comando).
    /// </summary>
    private static TResult CreateConcurrencyConflictResult<TResult>()
        where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (TResult)(object)Result.Failure(ConcurrencyError.Conflict);
        }

        var valueType = typeof(TResult).GetGenericArguments()[0];

        var failureMethod = typeof(Result)
            .GetMethods()
            .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethod)
            .MakeGenericMethod(valueType);

        return (TResult)failureMethod.Invoke(null, [ConcurrencyError.Conflict])!;
    }
}
