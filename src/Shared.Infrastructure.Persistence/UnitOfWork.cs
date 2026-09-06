using BitCode.Framework.Shared.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BitCode.Framework.Shared.Infrastructure.Persistence;

/// <remarks>
/// F1-09 (Unit of Work: ownership, nesting y límites transaccionales): esta clase es la única
/// autorizada, en todo el framework, a invocar <c>Database.BeginTransactionAsync</c>/
/// <c>IDbContextTransaction.CommitAsync</c>/<c>RollbackAsync</c> sobre el <see cref="DbContext"/>
/// subyacente — ni <c>RepositoryBase</c> ni ningún handler tienen acceso directo al
/// <see cref="DbContext"/> para hacerlo por su cuenta (ownership).
///
/// Nesting: como <see cref="UnitOfWork"/> se registra con el mismo ciclo de vida (scoped) que el
/// <see cref="DbContext"/> (ver <c>PersistenceServiceCollectionExtensions</c>), si un handler de
/// <see cref="BitCode.Framework.Shared.Application.Messaging.ITransactionalCommand"/> despacha un
/// segundo comando a través de <c>ISender</c> dentro del mismo scope (por ejemplo, un comando
/// orquestador que reutiliza un caso de uso existente), ambos comparten la misma instancia de
/// <see cref="UnitOfWork"/> y, por lo tanto, la misma transacción física. <see cref="_transactionDepth"/>
/// cuenta cuántos niveles de <see cref="BeginTransactionAsync"/> están anidados sobre esa única
/// transacción: solo el nivel más externo la abre y solo su <see cref="CommitAsync"/> la confirma
/// físicamente; los niveles internos solo hacen <c>flush</c> (<see cref="SaveChangesAsync"/>) de sus
/// cambios pendientes sin cerrar la transacción, dejando esa decisión al nivel externo. Antes de este
/// cambio, un <see cref="CommitAsync"/> del comando interno confirmaba y liberaba la transacción
/// completa de forma prematura, y el <see cref="CommitAsync"/> del comando externo fallaba después con
/// un <see cref="InvalidOperationException"/> ("No hay una transacción activa") aun cuando el trabajo
/// del comando externo posterior al interno nunca llegó a persistirse — un caso real de "commit
/// implícito inesperado" que este ADR corrige (ver addendum F1-09 en
/// <c>docs/adr/0009-contratos-comando-transaccion-explicita.md</c>).
///
/// Límites transaccionales: un <see cref="RollbackAsync"/>, sea invocado desde el nivel interno o
/// externo, siempre revierte inmediatamente la transacción física completa (SQL Server no ofrece un
/// mecanismo de "rollback parcial" para una transacción compartida por el mismo <see cref="DbContext"/>
/// sin usar savepoints, fuera de alcance de esta tarea). Esto es intencional: la falla de un comando
/// anidado invalida todo el trabajo, ya persistido o no, del comando contenedor, porque ambos comparten
/// el mismo <see cref="DbContext"/> y su <c>ChangeTracker</c>.
/// </remarks>
public class UnitOfWork(DbContext dbContext) : IUnitOfWork, IAsyncDisposable
{
    private IDbContextTransaction? _currentTransaction;
    private int _transactionDepth;

    /// <remarks>
    /// F1-08 (concurrencia optimista): traduce el <see cref="DbUpdateConcurrencyException"/> de EF
    /// Core —lanzado cuando el token de concurrencia (<c>RowVersion</c>) de una entidad ya cambió
    /// desde que se cargó— a un <see cref="ConcurrencyConflictException"/> propio del framework.
    /// <c>Shared.Application</c> no referencia EF Core, así que <c>TransactionBehavior</c> solo puede
    /// capturar esta excepción de dominio, nunca el tipo concreto de EF Core, para convertirla de
    /// forma uniforme en un <c>Result.Failure</c>.
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "Uno o más registros fueron modificados o eliminados por otra operación entre la carga y el guardado.",
                exception);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            // Anidamiento (F1-09): ya hay una transacción física abierta en este mismo scope (un
            // comando transaccional despachó otro comando transaccional). No se abre una segunda
            // transacción; se reutiliza la existente y solo se cuenta el nivel de anidamiento.
            _transactionDepth++;
            return;
        }

        _currentTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _transactionDepth = 1;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No hay una transacción activa para confirmar.");
        }

        if (_transactionDepth > 1)
        {
            // Commit de un nivel anidado (F1-09): no confirma ni cierra la transacción física —eso
            // es responsabilidad exclusiva del nivel más externo—, solo persiste (flush) los cambios
            // pendientes del comando interno para que sigan siendo visibles al comando contenedor.
            _transactionDepth--;
            await SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await DisposeCurrentTransactionAsync();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return;
        }

        // Límite transaccional (F1-09): un rollback siempre revierte la transacción física completa,
        // sea invocado desde el nivel interno o el externo. No existe "rollback parcial" de un nivel
        // anidado: ambos comandos comparten el mismo DbContext/ChangeTracker, así que la falla de uno
        // invalida el trabajo del otro, persistido o no.
        await _currentTransaction.RollbackAsync(cancellationToken);
        await DisposeCurrentTransactionAsync();
    }

    private async Task DisposeCurrentTransactionAsync()
    {
        _transactionDepth = 0;

        if (_currentTransaction is null)
        {
            return;
        }

        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction is not null)
        {
            await RollbackAsync();
        }

        GC.SuppressFinalize(this);
    }
}
