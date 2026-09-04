using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BitCode.Framework.Shared.Application.Behaviors;

/// <summary>
/// Envuelve la ejecución en una transacción explícita del Unit of Work. Se aplica únicamente a
/// TRequest : IBaseCommand (ICommand/ICommand&lt;T&gt;): las queries son de solo lectura por
/// convención y no necesitan overhead transaccional.
/// </summary>
public class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand, IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
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
