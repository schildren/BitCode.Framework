using System.Linq.Expressions;
using MyApp.Modules.Elementos;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;

namespace MyApp.Modules.Tests;

/// <summary>
/// Repositorio en memoria para probar handlers del módulo sin infraestructura real (sin SQL Server, sin
/// Testcontainers) -- las pruebas de handler de este módulo son pruebas unitarias, no de integración. Para
/// probar el módulo contra un SQL Server real, ver el patrón de
/// <c>tests/Templates.Tests/AppTemplateVerificationTests.cs</c> (Testcontainers.MsSql) y adaptarlo si el
/// módulo lo necesita.
/// </summary>
internal sealed class FakeElementoRepository : IRepository<Elemento, Guid>
{
    private readonly List<Elemento> _elementos = [];

    public IReadOnlyList<Elemento> Elementos => _elementos;

    public Task AddAsync(Elemento entity, CancellationToken cancellationToken = default)
    {
        _elementos.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(Elemento entity)
    {
    }

    public void Remove(Elemento entity) => _elementos.Remove(entity);

    public Task<Elemento?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_elementos.SingleOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<Elemento>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Elemento>>(_elementos.ToList());

    public Task<IReadOnlyList<Elemento>> ListAsync(
        ISpecification<Elemento> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Elemento>>(_elementos.ToList());

    public Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<Elemento> specification,
        Expression<Func<Elemento, TResult>> selector,
        CancellationToken cancellationToken = default)
    {
        var compiled = selector.Compile();
        return Task.FromResult<IReadOnlyList<TResult>>(_elementos.Select(compiled).ToList());
    }

    public Task<PagedResult<Elemento>> ListPagedAsync(
        ISpecification<Elemento> specification, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<Elemento>(_elementos.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, _elementos.Count));

    public Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<Elemento> specification,
        Expression<Func<Elemento, TResult>> selector,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var compiled = selector.Compile();
        var items = _elementos.Skip((page - 1) * pageSize).Take(pageSize).Select(compiled).ToList();
        return Task.FromResult(new PagedResult<TResult>(items, page, pageSize, _elementos.Count));
    }

    public Task<PagedResult<Elemento>> ListPagedAsync(
        ISpecification<Elemento> specification, PageRequest pageRequest, CancellationToken cancellationToken = default) =>
        ListPagedAsync(specification, pageRequest.Page, pageRequest.PageSize, cancellationToken);

    public Task<PagedResult<TResult>> ListPagedAsync<TResult>(
        ISpecification<Elemento> specification,
        Expression<Func<Elemento, TResult>> selector,
        PageRequest pageRequest,
        CancellationToken cancellationToken = default) =>
        ListPagedAsync(specification, selector, pageRequest.Page, pageRequest.PageSize, cancellationToken);

    public Task<int> CountAsync(ISpecification<Elemento> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(_elementos.Count);

    public Task<bool> AnyAsync(ISpecification<Elemento> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(_elementos.Count > 0);
}
