using BitCode.Framework.Shared.Testing;
using FluentAssertions;

namespace BitCode.Framework.Shared.Application.Tests.Eventing;

/// <summary>
/// Pruebas de referencia de F3-06 ("Schema y versionado") — plantilla que otros bounded contexts
/// pueden replicar para sus propios eventos de negocio: usan
/// <see cref="EventSchemaCompatibilityChecker"/> (Shared.Testing) sobre los tipos concretos de
/// <see cref="IIntegrationEvent"/> de ejemplo definidos en <c>TestIntegrationEvents.cs</c>, sin
/// requerir ningún broker ni infraestructura externa. Cubre el criterio de aceptación literal de la
/// Fase 3 ("Schema compatible e incompatible", sección "Pruebas obligatorias" del Plan Maestro).
/// </summary>
public class EventSchemaCompatibilityTests
{
    [Fact]
    public void Agregar_un_campo_nuevo_opcional_es_compatible_hacia_atras()
    {
        var resultado = EventSchemaCompatibilityChecker.CheckBackwardCompatibility(
            previousVersion: typeof(TestOrderCreatedIntegrationEvent),
            currentVersion: typeof(TestOrderCreatedWithOptionalDiscountIntegrationEvent));

        resultado.IsBackwardCompatible.Should().BeTrue(
            "un campo nuevo nullable (DiscountAmount) es un cambio aditivo: un consumidor de la versión " +
            "anterior lo ignora sin fallar (docs/politica-versionado.md, sección 5)");
        resultado.Violations.Should().BeEmpty();
    }

    [Fact]
    public void Eliminar_un_campo_existente_sin_incrementar_SchemaVersion_es_incompatible()
    {
        var resultado = EventSchemaCompatibilityChecker.CheckBackwardCompatibility(
            previousVersion: typeof(TestOrderCreatedIntegrationEvent),
            currentVersion: typeof(TestOrderCreatedMissingCustomerNameIntegrationEvent));

        resultado.IsBackwardCompatible.Should().BeFalse(
            "eliminar CustomerName es un cambio breaking (docs/politica-versionado.md, sección 5) que el " +
            "checker debe detectar, aun cuando el evento de ejemplo ya declaró SchemaVersion = 2");
        resultado.Violations.Should().ContainMatch("*CustomerName*eliminado*");
    }

    [Fact]
    public void Cambiar_el_tipo_de_un_campo_existente_es_incompatible()
    {
        var resultado = EventSchemaCompatibilityChecker.CheckBackwardCompatibility(
            previousVersion: typeof(TestOrderCreatedIntegrationEvent),
            currentVersion: typeof(TestOrderCreatedWithCustomerNameAsNumberIntegrationEvent));

        resultado.IsBackwardCompatible.Should().BeFalse(
            "cambiar CustomerName de string a int es un cambio breaking de tipo (docs/politica-versionado.md, sección 5)");
        resultado.Violations.Should().ContainMatch("*CustomerName*tipo*");
    }

    [Fact]
    public void Agregar_un_campo_nuevo_requerido_sin_incrementar_forma_es_incompatible_y_justifica_el_SchemaVersion_2()
    {
        // TestOrderCreatedV2IntegrationEvent agrega `Total` (decimal, no nullable) como campo requerido.
        // Este test demuestra POR QUÉ ese evento tuvo que incrementar SchemaVersion a 2 (F3-01): si se lo
        // tratara como forma compatible con la versión 1, el checker lo marca como violación.
        var resultado = EventSchemaCompatibilityChecker.CheckBackwardCompatibility(
            previousVersion: typeof(TestOrderCreatedIntegrationEvent),
            currentVersion: typeof(TestOrderCreatedV2IntegrationEvent));

        resultado.IsBackwardCompatible.Should().BeFalse(
            "Total es un campo nuevo requerido (decimal, no nullable) — un consumidor de la versión 1 no " +
            "puede asumir su presencia; por eso TestOrderCreatedV2IntegrationEvent incrementa SchemaVersion a 2");
        resultado.Violations.Should().ContainMatch("*Total*requerido*");
    }

    [Fact]
    public void Comparar_un_tipo_contra_si_mismo_es_trivialmente_compatible()
    {
        var resultado = EventSchemaCompatibilityChecker.CheckBackwardCompatibility(
            previousVersion: typeof(TestOrderCreatedIntegrationEvent),
            currentVersion: typeof(TestOrderCreatedIntegrationEvent));

        resultado.IsBackwardCompatible.Should().BeTrue();
        resultado.Violations.Should().BeEmpty();
    }
}
