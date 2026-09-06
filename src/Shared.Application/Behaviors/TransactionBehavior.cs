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
                logger.LogInformation(
                    "{RequestName} devolvió un Result fallido ({ErrorCode}); revirtiendo transacción",
                    requestName,
                    response.Error.Code);
                await unitOfWork.RollbackAsync(cancellationToken);
            }

            return response;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Excepción en {RequestName}; revirtiendo transacción", requestName);
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
