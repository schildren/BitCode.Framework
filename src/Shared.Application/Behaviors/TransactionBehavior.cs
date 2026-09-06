using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;

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

        if (response.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
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
        catch (Exception exception)
        {
            // Mismo criterio: cerrar la transacción (liberar locks) antes de cualquier trabajo
            // adicional. El logging de la excepción no debe demorar el rollback.
            await unitOfWork.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Excepción en {RequestName}; se revirtió la transacción", requestName);
            throw;
        }
    }
}
