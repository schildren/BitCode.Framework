using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Application.Regions;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BitCode.Framework.Shared.Application.Tests;

/// <summary>
/// F5-02 (ownership regional): pruebas de <see cref="RegionalOwnershipBehavior{TRequest,TResponse}"/>
/// — el mecanismo que rechaza, de forma determinística, un comando ejecutado fuera de la región
/// propietaria de escritura del tenant actual.
/// </summary>
public class RegionalOwnershipBehaviorTests
{
    private static (
        RegionalOwnershipBehavior<TestRegionalCommand, Result<string>> Behavior,
        ITenantContext TenantContext,
        IRegionalOwnershipResolver OwnershipResolver,
        ICurrentRegionProvider CurrentRegionProvider) CreateBehavior(
        bool multiTenancyEnabled,
        Guid? tenantId,
        RegionId ownerRegion,
        RegionId currentRegion)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsMultiTenancyEnabled.Returns(multiTenancyEnabled);
        tenantContext.TenantId.Returns(tenantId);

        var ownershipResolver = Substitute.For<IRegionalOwnershipResolver>();
        ownershipResolver.ResolveOwnerRegionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ownerRegion));

        var currentRegionProvider = Substitute.For<ICurrentRegionProvider>();
        currentRegionProvider.CurrentRegion.Returns(currentRegion);

        var behavior = new RegionalOwnershipBehavior<TestRegionalCommand, Result<string>>(
            tenantContext,
            ownershipResolver,
            currentRegionProvider,
            NullLogger<RegionalOwnershipBehavior<TestRegionalCommand, Result<string>>>.Instance);

        return (behavior, tenantContext, ownershipResolver, currentRegionProvider);
    }

    [Fact]
    public async Task Handle_CurrentRegionMatchesOwnerRegion_CallsNext()
    {
        var tenantId = Guid.NewGuid();
        var (behavior, _, _, _) = CreateBehavior(
            multiTenancyEnabled: true,
            tenantId: tenantId,
            ownerRegion: new RegionId("eu-west"),
            currentRegion: new RegionId("eu-west"));

        var result = await behavior.Handle(
            new TestRegionalCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_CurrentRegionDiffersFromOwnerRegion_RejectsWithoutCallingNext()
    {
        var tenantId = Guid.NewGuid();
        var (behavior, _, _, _) = CreateBehavior(
            multiTenancyEnabled: true,
            tenantId: tenantId,
            ownerRegion: new RegionId("eu-west"),
            currentRegion: new RegionId("us-east"));
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestRegionalCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RegionalOwnership.WrongRegion");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        nextCalled.Should().BeFalse(
            "un comando ejecutado fuera de la región propietaria del tenant nunca debe ejecutar el handler");
    }

    [Fact]
    public async Task Handle_MultiTenancyDisabled_NeverRejectsRegardlessOfConfiguredRegions()
    {
        var (behavior, _, ownershipResolver, _) = CreateBehavior(
            multiTenancyEnabled: false,
            tenantId: null,
            ownerRegion: new RegionId("eu-west"),
            currentRegion: new RegionId("us-east"));

        var result = await behavior.Handle(
            new TestRegionalCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await ownershipResolver.DidNotReceive()
            .ResolveOwnerRegionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TenantIdNotResolved_NeverRejectsRegardlessOfConfiguredRegions()
    {
        var (behavior, _, ownershipResolver, _) = CreateBehavior(
            multiTenancyEnabled: true,
            tenantId: null,
            ownerRegion: new RegionId("eu-west"),
            currentRegion: new RegionId("us-east"));

        var result = await behavior.Handle(
            new TestRegionalCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await ownershipResolver.DidNotReceive()
            .ResolveOwnerRegionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SingleRegionDeployment_SameRegionEverywhere_NeverRejects()
    {
        // Caso real hoy en este repositorio (docs/bia-fase5.md): un solo host, sin infraestructura
        // multi-región. ICurrentRegionProvider y el mapa de ownership coinciden siempre en
        // RegionId.Primary, así que el comportamiento observable es idéntico al de no tener este
        // behavior en absoluto.
        var tenantId = Guid.NewGuid();
        var (behavior, _, _, _) = CreateBehavior(
            multiTenancyEnabled: true,
            tenantId: tenantId,
            ownerRegion: RegionId.Primary,
            currentRegion: RegionId.Primary);

        var result = await behavior.Handle(
            new TestRegionalCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
