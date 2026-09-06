using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// F1-15: <see cref="TenantContext"/> envuelve <see cref="ITenantProvider"/> y memoiza el
/// <c>TenantId</c> resuelto la primera vez que se accede — estas pruebas demuestran la garantía de
/// inmutabilidad "una vez fijado, no puede sobrescribirse" a nivel de contrato: ni siquiera un
/// <see cref="ITenantProvider"/> subyacente que cambiara de valor a mitad de un scope (un escenario
/// que no debería darse en producción, pero sirve como prueba de robustez) logra alterar el
/// <c>TenantId</c> ya expuesto por <see cref="ITenantContext"/>.
/// </summary>
public class TenantContextTests
{
    /// <summary>
    /// Doble de prueba mutable: a diferencia de <see cref="FakeTenantProvider"/> (inmutable por
    /// diseño), este permite simular un <see cref="ITenantProvider"/> cuyo valor cambia entre
    /// accesos, para poder demostrar que <see cref="TenantContext"/> ignora esos cambios posteriores
    /// a la primera resolución.
    /// </summary>
    private sealed class MutableTenantProvider : ITenantProvider
    {
        public bool IsMultiTenancyEnabled => true;

        public Guid? TenantId { get; set; }
    }

    [Fact]
    public void TenantId_ResolvesFromUnderlyingProvider_OnFirstAccess()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext(new FakeTenantProvider(tenantId));

        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void IsMultiTenancyEnabled_DelegatesToUnderlyingProvider()
    {
        var context = new TenantContext(new FakeTenantProvider(tenantId: null, isMultiTenancyEnabled: false));

        context.IsMultiTenancyEnabled.Should().BeFalse();
    }

    [Fact]
    public void TenantId_IsImmutableAfterFirstResolution_EvenIfUnderlyingProviderChanges()
    {
        var originalTenantId = Guid.NewGuid();
        var provider = new MutableTenantProvider { TenantId = originalTenantId };
        var context = new TenantContext(provider);

        // Primer acceso: fija (memoiza) el TenantId para el resto del scope.
        var firstRead = context.TenantId;

        // Un intento (accidental o malicioso) de "sobrescribir" el tenant mutando la fuente
        // subyacente después de la primera resolución no tiene ningún efecto sobre lo ya expuesto:
        // ITenantContext no vuelve a consultar al provider.
        provider.TenantId = Guid.NewGuid();

        firstRead.Should().Be(originalTenantId);
        context.TenantId.Should().Be(originalTenantId,
            "una vez resuelto el TenantId para el scope, ningún cambio posterior en la fuente subyacente puede sobrescribirlo");
    }

    [Fact]
    public void ITenantContext_ExposesNoPublicWriteMember_ForTenantId()
    {
        // Guarda estructural: ITenantContext no debe declarar ningún setter/método de escritura
        // para TenantId. Si en el futuro alguien agrega uno, este test falla explícitamente en vez
        // de dejar que el criterio de aceptación "el tenant no puede sobrescribirse" se erosione en
        // silencio.
        var tenantIdProperty = typeof(ITenantContext).GetProperty(nameof(ITenantContext.TenantId));

        tenantIdProperty.Should().NotBeNull();
        tenantIdProperty!.CanWrite.Should().BeFalse(
            "ITenantContext.TenantId solo debe exponer un getter: fijar el tenant es responsabilidad exclusiva de la resolución interna (F1-12/F1-15), nunca de un setter público");
    }
}
