using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Enforcement;

/// <summary>
/// Prueba negativa para verificar que NetArchTest detecta y falla efectivamente
/// cuando se introducen violaciones arquitectónicas intencionales.
/// Demuestra que las reglas no son trivialmente 'true' (falsos positivos de éxito)
/// y que violaciones reales rompen la compilación/CI.
/// </summary>
public class ArchitectureEnforcementVerificationTests
{
    // Clase ficticia que viola explícitamente una regla arquitectónica
    private class FakeDirectPersistenceDependencyClass
    {
        public BitCode.Framework.Shared.Infrastructure.Persistence.UnitOfWork? LeakedUnitOfWork { get; set; }
    }

    [Fact]
    public void Architecture_Rule_Must_Fail_When_Violation_Is_Detected()
    {
        // Regla: los tipos anidados de esta prueba NO deben tener dependencia de Persistence
        var result = Types.InAssembly(typeof(ArchitectureEnforcementVerificationTests).Assembly)
            .That()
            .HaveName(nameof(FakeDirectPersistenceDependencyClass))
            .ShouldNot()
            .HaveDependencyOn("BitCode.Framework.Shared.Infrastructure.Persistence")
            .GetResult();

        // Verificamos explícitamente que la regla detectó el fallo (IsSuccessful == false)
        result.IsSuccessful.Should().BeFalse(
            "El motor de reglas arquitectónicas DEBE fallar cuando una clase viola los límites de capas.");

        result.FailingTypeNames.Should().NotBeNullOrEmpty();
        result.FailingTypeNames.Should().Contain(name => name.Contains(nameof(FakeDirectPersistenceDependencyClass)));
    }
}
