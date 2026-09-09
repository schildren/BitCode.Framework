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

/// <summary>
/// Marca un comando cuya ejecución debe ocurrir en la región propietaria de escritura (single-writer)
/// del tenant actual (F5-02, Fase 5 — Disaster Recovery y multi-región), siguiendo la decisión
/// arquitectónica rectora del Plan Maestro (sección 2): "Multi-región: cómputo activo/activo y un
/// único propietario de escritura por agregado o bounded context".
/// </summary>
/// <remarks>
/// F5-02: <c>RegionalOwnershipBehavior</c> (registrado por <c>AddSharedApplication</c>) le da
/// comportamiento real a este marcador. En un despliegue de una sola región/instancia sin multi-
/// tenancy habilitada, o sin <c>TenantId</c> resuelto para el request actual, el behavior no rechaza
/// nada — cero cambio de comportamiento para el caso común. En un despliegue multi-región (varias
/// instancias, cada una configurada con su propio <c>ICurrentRegionProvider</c>,
/// Shared.Infrastructure.Persistence), un comando <see cref="IRegionalCommand"/> ejecutado en una
/// instancia cuya región no coincide con la región propietaria del tenant (resuelta vía
/// <c>IRegionalOwnershipResolver</c>) se rechaza (<c>RegionalOwnershipErrors.WrongRegion</c>) sin
/// ejecutar el handler ni abrir ninguna transacción. Reservado para comandos que mutan un agregado o
/// bounded context cuyo perfil DR exige un único escritor válido (p. ej. perfiles Gold/Platinum del
/// BIA de Fase 5, ver <c>docs/bia-fase5.md</c>); un comando de solo lectura nunca debe implementar
/// esta interfaz — usar <c>IQuery</c> en su lugar (regla dura de <c>docs/convenciones.md</c>).
/// </remarks>
public interface IRegionalCommand : IBaseCommand;
