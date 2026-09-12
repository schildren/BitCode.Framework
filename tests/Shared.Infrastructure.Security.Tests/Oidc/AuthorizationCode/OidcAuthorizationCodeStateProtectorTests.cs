using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// F2-02: la cookie de correlación (state/code_verifier/nonce) debe ser ilegible e infalsificable para
/// cualquiera que no tenga las claves de Data Protection del servidor -- ni la SPA ni un atacante que
/// intercepte la cookie.
/// </summary>
public class OidcAuthorizationCodeStateProtectorTests
{
    private static IDataProtectionProvider CreateDataProtectionProvider() =>
        new ServiceCollection()
            .AddDataProtection()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

    private static readonly OidcAuthorizationCodeState SampleState = new(
        State: "state-abc",
        CodeVerifier: "verifier-abc",
        Nonce: "nonce-abc",
        RedirectUri: "https://app.bitcode.local/auth/callback",
        ReturnUrl: "/dashboard");

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsTheOriginalState()
    {
        var sut = new OidcAuthorizationCodeStateProtector(CreateDataProtectionProvider());

        var protectedValue = sut.Protect(SampleState);
        var result = sut.Unprotect(protectedValue);

        result.Should().Be(SampleState);
    }

    [Fact]
    public void Protect_NeverEmitsThePlainCodeVerifierOrState()
    {
        var sut = new OidcAuthorizationCodeStateProtector(CreateDataProtectionProvider());

        var protectedValue = sut.Protect(SampleState);

        protectedValue.Should().NotContain(SampleState.CodeVerifier);
        protectedValue.Should().NotContain(SampleState.State);
    }

    [Fact]
    public void Unprotect_WithTamperedValue_ReturnsNullInsteadOfThrowing()
    {
        var sut = new OidcAuthorizationCodeStateProtector(CreateDataProtectionProvider());
        var protectedValue = sut.Protect(SampleState);
        var tampered = protectedValue[..^1] + (protectedValue[^1] == 'A' ? 'B' : 'A');

        var result = sut.Unprotect(tampered);

        result.Should().BeNull();
    }

    [Fact]
    public void Unprotect_WithValueFromADifferentProtectorPurpose_ReturnsNull()
    {
        var provider = CreateDataProtectionProvider();
        var sut = new OidcAuthorizationCodeStateProtector(provider);
        var unrelatedProtector = provider.CreateProtector("Otro.Proposito.No.Relacionado");
        var valueFromAnotherPurpose = unrelatedProtector.Protect("{}");

        var result = sut.Unprotect(valueFromAnotherPurpose);

        result.Should().BeNull();
    }

    [Fact]
    public void Unprotect_WithNullOrEmptyValue_ReturnsNull()
    {
        var sut = new OidcAuthorizationCodeStateProtector(CreateDataProtectionProvider());

        sut.Unprotect(string.Empty).Should().BeNull();
    }
}
