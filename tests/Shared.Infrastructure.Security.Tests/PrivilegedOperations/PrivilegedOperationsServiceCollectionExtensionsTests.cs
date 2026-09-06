using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.PrivilegedOperations;

public class PrivilegedOperationsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedPrivilegedOperationsPolicies_WithoutPriorAbacRegistration_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedPrivilegedOperationsPolicies();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSharedAbacAuthorization*");
    }

    [Fact]
    public void AddSharedPrivilegedOperationsPolicies_AfterAbacRegistration_RegistersBothRules()
    {
        var services = new ServiceCollection();
        services.AddSharedAbacAuthorization();

        services.AddSharedPrivilegedOperationsPolicies();

        using var provider = services.BuildServiceProvider();
        var rules = provider.GetServices<IAbacRule>().ToArray();
        rules.Should().Contain(r => r is StepUpAbacRule);
        rules.Should().Contain(r => r is SegregationOfDutiesAbacRule);
        // Las reglas de F2-08 siguen registradas -- F2-10 se suma, no reemplaza.
        rules.Should().Contain(r => r is AttributeScopeAbacRule);
        rules.Should().Contain(r => r is AmountLimitAbacRule);
    }

    [Fact]
    public void AddSharedPrivilegedOperationsPolicies_AppliesConfigureOptionsDelegate()
    {
        var services = new ServiceCollection();
        services.AddSharedAbacAuthorization();

        services.AddSharedPrivilegedOperationsPolicies(options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            });
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PrivilegedOperationsOptions>>().Value;
        options.StepUpRequirements.Should().ContainSingle(r => r.ResourceType == "pagos");
    }

    [Fact]
    public void AddSharedPrivilegedOperationsPolicies_CalledTwice_DoesNotDuplicateRules()
    {
        var services = new ServiceCollection();
        services.AddSharedAbacAuthorization();

        services.AddSharedPrivilegedOperationsPolicies();
        services.AddSharedPrivilegedOperationsPolicies();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IAbacRule>().Count(r => r is StepUpAbacRule).Should().Be(1);
        provider.GetServices<IAbacRule>().Count(r => r is SegregationOfDutiesAbacRule).Should().Be(1);
    }

    [Fact]
    public void AddSharedPrivilegedOperationsPolicies_StepUpRequirementWithoutAnyCriterion_FailsAtStartup()
    {
        // El error de configuración (StepUpRequirement sin AcceptableAuthenticationMethods ni
        // MaxAuthenticationAge) debe ser visible en el arranque -- ya sea al construir el
        // ServiceProvider (ValidateOnStart via host) o, como mínimo, en la primera resolución de
        // IOptions<PrivilegedOperationsOptions> -- nunca recién en la primera evaluación real de un
        // usuario contra la operación protegida (lo que produciría un 500 genérico indistinguible de un
        // fallo de infraestructura).
        var services = new ServiceCollection();
        services.AddSharedAbacAuthorization();
        services.AddSharedPrivilegedOperationsPolicies(options =>
        {
            options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
            });
        });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<PrivilegedOperationsOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*pagos.aprobar*AcceptableAuthenticationMethods*");
    }
}
