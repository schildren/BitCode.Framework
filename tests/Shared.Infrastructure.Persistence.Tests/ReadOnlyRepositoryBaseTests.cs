using BitCode.Framework.Shared.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// F1-17: una lectura resuelta a través de <c>IReadRepository&lt;,&gt;</c> (registrada como
/// <see cref="ReadOnlyRepositoryBase{TEntity, TId}"/>) nunca deja entradas trackeadas en el
/// <c>ChangeTracker</c> del <see cref="DbContext"/>, y una proyección explícita nunca trae la
/// entidad completa. <see cref="RepositoryBase{TEntity, TId}"/> (lado de escritura, detrás de
/// <c>IRepository&lt;,&gt;</c>) sigue trackeando por defecto, porque un handler de comando necesita
/// poder llamar <c>Update</c> sobre la misma instancia que obtuvo con <c>GetByIdAsync</c>.
/// </summary>
public class ReadOnlyRepositoryBaseTests
{
    private static TestDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new TestDbContext(options);
        context.TestEntities.AddRange(
            new TestEntity(Guid.NewGuid(), "Alpha", 10),
            new TestEntity(Guid.NewGuid(), "Beta", 30),
            new TestEntity(Guid.NewGuid(), "Gamma", 50),
            new TestEntity(Guid.NewGuid(), "Delta", 70));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return context;
    }

    [Fact]
    public async Task GetByIdAsync_ViaReadOnlyRepository_DoesNotTrackEntity()
    {
        using var context = CreateContext(nameof(GetByIdAsync_ViaReadOnlyRepository_DoesNotTrackEntity));
        var existingId = context.TestEntities.AsNoTracking().First().Id;
        context.ChangeTracker.Clear();

        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);
        var found = await readRepository.GetByIdAsync(existingId);

        found.Should().NotBeNull();
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ViaReadOnlyRepository_DoesNotTrackEntities()
    {
        using var context = CreateContext(nameof(ListAsync_ViaReadOnlyRepository_DoesNotTrackEntities));
        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);

        var all = await readRepository.ListAsync();

        all.Should().HaveCount(4);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WithSpecification_ViaReadOnlyRepository_DoesNotTrackEntities_EvenWithoutApplyAsNoTracking()
    {
        // La especificación no llama ApplyAsNoTracking(): ReadOnlyRepositoryBase igual fuerza
        // AsNoTracking() porque solo se resuelve detrás de IReadRepository<,> (F1-17) — no depende
        // de que cada Specification recuerde declararlo.
        using var context = CreateContext(nameof(ListAsync_WithSpecification_ViaReadOnlyRepository_DoesNotTrackEntities_EvenWithoutApplyAsNoTracking));
        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);
        var spec = new ByAmountAboveSpecification(threshold: 20);

        var result = await readRepository.ListAsync(spec);

        result.Should().HaveCount(3);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ViaWriteRepository_StillTracksEntity()
    {
        // Contraste explícito: el repositorio de escritura (RepositoryBase, detrás de
        // IRepository<,>) necesita seguir trackeando GetByIdAsync para que un handler de comando
        // pueda mutar la entidad y llamar Update sobre la misma instancia.
        using var context = CreateContext(nameof(GetByIdAsync_ViaWriteRepository_StillTracksEntity));
        var existingId = context.TestEntities.AsNoTracking().First().Id;
        context.ChangeTracker.Clear();

        var writeRepository = new RepositoryBase<TestEntity, Guid>(context);
        var found = await writeRepository.GetByIdAsync(existingId);

        found.Should().NotBeNull();
        context.ChangeTracker.Entries().Should().ContainSingle();
    }

    [Fact]
    public async Task ListAsync_WithSelector_ProjectsWithoutMaterializingFullEntity()
    {
        using var context = CreateContext(nameof(ListAsync_WithSelector_ProjectsWithoutMaterializingFullEntity));
        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);
        var spec = new ByAmountAboveSpecification(threshold: 25);

        var names = await readRepository.ListAsync(spec, e => e.Name);

        names.Should().BeEquivalentTo(["Beta", "Gamma", "Delta"]);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ListPagedAsync_ReturnsPageAndTotalCountAcrossTheFullFilteredSet()
    {
        using var context = CreateContext(nameof(ListPagedAsync_ReturnsPageAndTotalCountAcrossTheFullFilteredSet));
        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);
        var spec = new ByAmountAboveSpecification(threshold: 0);

        var firstPage = await readRepository.ListPagedAsync(spec, page: 1, pageSize: 2);
        var secondPage = await readRepository.ListPagedAsync(spec, page: 2, pageSize: 2);

        firstPage.TotalCount.Should().Be(4);
        firstPage.TotalPages.Should().Be(2);
        firstPage.Items.Should().HaveCount(2);
        firstPage.HasNextPage.Should().BeTrue();
        firstPage.HasPreviousPage.Should().BeFalse();

        secondPage.Items.Should().HaveCount(2);
        secondPage.HasNextPage.Should().BeFalse();
        secondPage.HasPreviousPage.Should().BeTrue();

        firstPage.Items.Select(e => e.Name)
            .Should().NotIntersectWith(secondPage.Items.Select(e => e.Name));
    }

    [Fact]
    public async Task ListPagedAsync_WithSelector_ProjectsPageWithoutMaterializingFullEntity()
    {
        using var context = CreateContext(nameof(ListPagedAsync_WithSelector_ProjectsPageWithoutMaterializingFullEntity));
        var readRepository = new ReadOnlyRepositoryBase<TestEntity, Guid>(context);
        var spec = new ByAmountAboveSpecification(threshold: 0);

        var page = await readRepository.ListPagedAsync(spec, e => e.Name, page: 1, pageSize: 2);

        page.TotalCount.Should().Be(4);
        page.Items.Should().HaveCount(2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
