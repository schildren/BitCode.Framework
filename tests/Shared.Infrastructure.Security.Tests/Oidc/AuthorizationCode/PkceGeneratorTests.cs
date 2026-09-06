using BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// F2-02: PKCE (RFC 7636) es la pieza criptográfica central de Authorization Code + PKCE -- sin ella
/// no hay diferencia real entre este flujo y un Authorization Code clásico vulnerable a interceptación
/// del code en un cliente público (una SPA).
/// </summary>
public class PkceGeneratorTests
{
    [Fact]
    public void GenerateCodeVerifier_UsesDefaultLength_WithinRfc7636Bounds()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        verifier.Length.Should().BeInRange(43, 128);
        verifier.Should().MatchRegex("^[A-Za-z0-9._~-]+$");
    }

    [Theory]
    [InlineData(42)]
    [InlineData(129)]
    public void GenerateCodeVerifier_OutsideRfc7636Bounds_Throws(int length)
    {
        var act = () => PkceGenerator.GenerateCodeVerifier(length);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GenerateCodeVerifier_TwoCalls_ProduceDifferentValues()
    {
        var first = PkceGenerator.GenerateCodeVerifier();
        var second = PkceGenerator.GenerateCodeVerifier();

        first.Should().NotBe(second, "cada intento de login debe usar un code_verifier de un solo uso");
    }

    /// <summary>
    /// Vector de prueba oficial de RFC 7636 (apéndice B): confirma que <c>CreateCodeChallenge</c>
    /// implementa exactamente BASE64URL-ENCODE(SHA256(ASCII(code_verifier))) sin padding, no una
    /// variante propia.
    /// </summary>
    [Fact]
    public void CreateCodeChallenge_WithRfc7636TestVector_MatchesSpecExample()
    {
        const string codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = PkceGenerator.CreateCodeChallenge(codeVerifier);

        challenge.Should().Be(expectedChallenge);
    }

    [Fact]
    public void CreateCodeChallenge_IsDeterministic_ForTheSameVerifier()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        var challengeA = PkceGenerator.CreateCodeChallenge(verifier);
        var challengeB = PkceGenerator.CreateCodeChallenge(verifier);

        challengeA.Should().Be(challengeB);
    }

    [Fact]
    public void CreateCodeChallenge_DifferentVerifiers_ProduceDifferentChallenges()
    {
        var challengeA = PkceGenerator.CreateCodeChallenge(PkceGenerator.GenerateCodeVerifier());
        var challengeB = PkceGenerator.CreateCodeChallenge(PkceGenerator.GenerateCodeVerifier());

        challengeA.Should().NotBe(challengeB, "un code_challenge distinto por login es lo que hace inútil interceptar un code sin el verifier original");
    }

    [Fact]
    public void CreateCodeChallenge_NeverContainsBase64PaddingOrUnsafeUrlCharacters()
    {
        var challenge = PkceGenerator.CreateCodeChallenge(PkceGenerator.GenerateCodeVerifier());

        challenge.Should().NotContain("=").And.NotContain("+").And.NotContain("/");
    }

    [Fact]
    public void GenerateCorrelationToken_TwoCalls_ProduceDifferentHighEntropyValues()
    {
        var state = PkceGenerator.GenerateCorrelationToken();
        var nonce = PkceGenerator.GenerateCorrelationToken();

        state.Should().NotBe(nonce);
        state.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public void GenerateCorrelationToken_WithTooFewBytes_Throws()
    {
        var act = () => PkceGenerator.GenerateCorrelationToken(8);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
