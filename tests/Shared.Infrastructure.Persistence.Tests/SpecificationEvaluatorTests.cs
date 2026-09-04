using BitCode.Framework.Shared.Infrastructure.Persistence.Specifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

public class SpecificationEvaluatorTests
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

        return context;
    }

    [Fact]
    public void GetQuery_WithSimpleFilter_ReturnsMatchingEntities()
    {
        using var context = CreateContext(nameof(GetQuery_WithSimpleFilter_ReturnsMatchingEntities));
        var spec = new ByAmountAboveSpecification(threshold: 25);

        var result = SpecificationEvaluator<TestEntity>.GetQuery(context.TestEntities, spec).ToList();

        result.Should().HaveCount(3);
        result.Select(e => e.Name).Should().BeEquivalentTo(["Beta", "Gamma", "Delta"]);
    }

    [Fact]
    public void GetQuery_WithFilterOrderAndPaging_ReturnsExpectedPage()
    {
        using var context = CreateContext(nameof(GetQuery_WithFilterOrderAndPaging_ReturnsExpectedPage));
        var spec = new ByAmountAboveSpecification(threshold: 0, skip: 1, take: 2);

        var result = SpecificationEvaluator<TestEntity>.GetQuery(context.TestEntities, spec).ToList();

        result.Should().HaveCount(2);
        result.Select(e => e.Name).Should().ContainInOrder("Beta", "Delta");
    }

    [Fact]
    public void GetQuery_WithNoCriteria_ReturnsAllEntities()
    {
        using var context = CreateContext(nameof(GetQuery_WithNoCriteria_ReturnsAllEntities));
        var spec = new ByAmountAboveSpecification(threshold: -1);

        var result = SpecificationEvaluator<TestEntity>.GetQuery(context.TestEntities, spec).ToList();

        result.Should().HaveCount(4);
    }
}
