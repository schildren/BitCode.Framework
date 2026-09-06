using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Shared.Application.Messaging;

public interface ICommand : IRequest<Result>, IBaseCommand;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

public interface IBaseCommand;

/// <summary>
/// Marca un comando cuya ejecución (handler + <see cref="BitCode.Framework.Shared.Domain.Persistence.IUnitOfWork.SaveChangesAsync"/>)
/// debe ocurrir dentro de una transacción explícita de base de datos con rollback automático ante
/// una excepción o un <c>Result</c> fallido. Reservado para comandos que necesitan coordinar más de
/// una operación de escritura (varios agregados, varios <c>SaveChanges</c>, efectos que deben
/// confirmarse o revertirse en conjunto). Un <see cref="IBaseCommand"/> que NO implementa esta
/// interfaz sigue persistiendo sus cambios automáticamente (ver <c>TransactionBehavior</c>), pero
/// sin la transacción de base de datos explícita ni el rollback coordinado.
/// </summary>
public interface ITransactionalCommand : IBaseCommand;

/// <summary>
/// Marca un comando que debe tratarse de forma idempotente: reintentar su ejecución (por ejemplo,
/// ante un reintento de red o un doble clic del cliente) no debe producir un efecto duplicado. Este
/// contrato es solo el marcador; la detección de duplicados y el almacenamiento de claves de
/// idempotencia se implementan en una tarea posterior (F1-22, Idempotencia API).
/// </summary>
public interface IIdempotentCommand : IBaseCommand;
