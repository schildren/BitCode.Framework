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
/// ante un reintento de red o un doble clic del cliente) no debe producir un efecto duplicado.
/// </summary>
/// <remarks>
/// F1-22: <c>IdempotencyBehavior</c> (registrado por <c>AddSharedApplication</c>) le da
/// comportamiento real a este marcador. Un comando <see cref="IIdempotentCommand"/> EXIGE una
/// Idempotency-Key no vacía —resuelta vía <c>IIdempotencyKeyProvider</c>, típicamente el header HTTP
/// <c>Idempotency-Key</c>—: sin ella, el comando se rechaza (<c>IdempotencyErrors.KeyRequired</c>) en
/// vez de ejecutarse como un comando no idempotente cualquiera. Con una clave ya usada antes por el
/// mismo request (mismo hash del comando serializado), el handler no se vuelve a ejecutar: se
/// devuelve el mismo <c>Result</c> exitoso ya obtenido. Con la misma clave pero un payload distinto,
/// se rechaza (<c>IdempotencyErrors.KeyReused</c>) en vez de tratarse como el mismo reintento. Solo
/// un resultado exitoso queda guardado bajo la clave; un <c>Result.Failure</c> no deja rastro, así
/// que un reintento posterior a un fallo vuelve a ejecutar el handler con normalidad. Ver
/// <c>docs/convenciones.md</c> (regla dura 4) para el contrato completo.
/// </remarks>
public interface IIdempotentCommand : IBaseCommand;
