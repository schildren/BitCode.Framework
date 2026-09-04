using FluentAssertions;

namespace BitCode.Framework.Shared.Modularity.Tests;

public class ModuleDependencyResolverTests
{
    [Fact]
    public void OrderByDependencies_WithCircularDependency_Throws()
    {
        var act = () => ModuleDependencyResolver.OrderByDependencies([typeof(ModuleA), typeof(ModuleB)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*circular*");
    }

    [Fact]
    public void OrderByDependencies_WithDependencyOutsideProvidedSet_Throws()
    {
        var act = () => ModuleDependencyResolver.OrderByDependencies([typeof(ModuleWithMissingDependency)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no fue*encontrado*");
    }

    [Fact]
    public void OrderByDependencies_WithNoDependencies_PreservesInputTypes()
    {
        var ordered = ModuleDependencyResolver.OrderByDependencies([typeof(ModuleNotInSet)]);

        ordered.Should().ContainSingle().Which.Should().Be(typeof(ModuleNotInSet));
    }
}
