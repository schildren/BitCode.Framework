using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Layers;

public class LayerDependencyTests
{
    private static readonly string SharedDomainNamespace = "BitCode.Framework.Shared.Domain";
    private static readonly string SharedApplicationNamespace = "BitCode.Framework.Shared.Application";
    private static readonly string SharedPersistenceNamespace = "BitCode.Framework.Shared.Infrastructure.Persistence";
    private static readonly string SharedSecurityNamespace = "BitCode.Framework.Shared.Infrastructure.Security";
    private static readonly string SharedWebNamespace = "BitCode.Framework.Shared.Infrastructure.Web";
    private static readonly string PlatformNamespace = "BitCode.Framework.Platform";

    [Fact]
    public void Shared_Kernel_Should_Not_Have_Dependency_On_Other_Projects()
    {
        var result = Types.InAssembly(typeof(AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                SharedDomainNamespace,
                SharedApplicationNamespace,
                SharedPersistenceNamespace,
                SharedSecurityNamespace,
                SharedWebNamespace,
                PlatformNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Shared.Kernel debe ser independiente de capas superiores. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Shared_Domain_Should_Not_Have_Dependency_On_Application_Or_Infrastructure()
    {
        var domainAssembly = typeof(BitCode.Framework.Shared.Domain.MultiTenancy.ITenantContext).Assembly;

        var result = Types.InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                SharedApplicationNamespace,
                SharedPersistenceNamespace,
                SharedSecurityNamespace,
                SharedWebNamespace,
                PlatformNamespace,
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Shared.Domain no debe acoplarse a Application, Infrastructure, EF Core ni ASP.NET Core. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Shared_Application_Should_Not_Have_Dependency_On_Persistence_Or_Web()
    {
        var applicationAssembly = typeof(BitCode.Framework.Shared.Application.Behaviors.LoggingBehavior<,>).Assembly;

        var result = Types.InAssembly(applicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                SharedPersistenceNamespace,
                SharedWebNamespace,
                PlatformNamespace,
                "Microsoft.AspNetCore.Mvc")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Shared.Application no debe depender de Persistence ni capas de presentación Web. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Shared_Infrastructure_Should_Not_Have_Dependency_On_Platform_Modules()
    {
        var persistenceAssembly = typeof(BitCode.Framework.Shared.Infrastructure.Persistence.UnitOfWork).Assembly;
        var securityAssembly = typeof(BitCode.Framework.Shared.Infrastructure.Security.Permissions.RequirePermissionAttribute).Assembly;
        var cachingAssembly = typeof(BitCode.Framework.Shared.Infrastructure.Caching.ITenantAwareCache).Assembly;

        var result = Types.InAssemblies([persistenceAssembly, securityAssembly, cachingAssembly])
            .ShouldNot()
            .HaveDependencyOn(PlatformNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"La infraestructura transversal no debe depender de módulos de plataforma de negocio. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
