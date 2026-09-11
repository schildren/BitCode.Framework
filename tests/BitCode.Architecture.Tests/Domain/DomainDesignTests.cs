using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Domain;

public class DomainDesignTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(ITenantContext).Assembly;
    private static readonly System.Reflection.Assembly KernelAssembly = typeof(Entity<>).Assembly;

    [Fact]
    public void Domain_Entities_Should_Inherit_From_Entity_Or_AggregateRoot()
    {
        var result = Types.InAssemblies([DomainAssembly, KernelAssembly])
            .That()
            .ResideInNamespace("BitCode.Framework.Shared.Domain")
            .And()
            .HaveNameEndingWith("Entity")
            .Should()
            .Inherit(typeof(Entity<>))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Entidades del dominio deben heredar de Entity<T>. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_Events_Should_Inherit_From_DomainEvent()
    {
        var result = Types.InAssemblies([DomainAssembly, KernelAssembly])
            .That()
            .HaveNameEndingWith("DomainEvent")
            .And()
            .AreNotInterfaces()
            .And()
            .AreNotAbstract()
            .Should()
            .Inherit(typeof(DomainEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Eventos de dominio deben heredar de DomainEvent. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void MultiTenant_Entities_Should_Implement_ITenantEntity()
    {
        var tenantEntities = Types.InAssemblies([DomainAssembly, KernelAssembly])
            .That()
            .HaveNameEndingWith("Entity")
            .And()
            .ImplementInterface(typeof(ITenantEntity))
            .Should()
            .Inherit(typeof(Entity<>))
            .GetResult();

        tenantEntities.IsSuccessful.Should().BeTrue(
            $"Entidades multi-tenant que implementan ITenantEntity deben heredar de Entity<T>. Violaciones: {string.Join(", ", tenantEntities.FailingTypeNames ?? [])}");
    }
}
