using BitCode.Framework.Shared.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BitCode.Framework.Shared.Infrastructure.Persistence;

public class UnitOfWork(DbContext dbContext) : IUnitOfWork, IAsyncDisposable
{
    private IDbContextTransaction? _currentTransaction;

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
            return;
        }

        _currentTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No hay una transacción activa para confirmar.");
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

        await _currentTransaction.RollbackAsync(cancellationToken);
        await DisposeCurrentTransactionAsync();
    }

    private async Task DisposeCurrentTransactionAsync()
    {
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
