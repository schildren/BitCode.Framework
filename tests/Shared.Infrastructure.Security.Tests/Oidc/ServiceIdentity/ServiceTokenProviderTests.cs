using System.Net;
using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;
using BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.ServiceIdentity;

/// <summary>
/// F2-04: identidad de servicio (OAuth2 Client Credentials, RFC 6749 sección 4.4) -- obtención de
/// token, reutilización mientras es válido, renovación al expirar y fallo de credenciales, exactamente
/// los casos pedidos por el criterio de prueba de la tarea.
/// </summary>
public class ServiceTokenProviderTests
{
    private const string MetadataAddress = "https://keycloak.local/realms/bitcode/.well-known/openid-configuration";
    private const string TokenEndpoint = "https://keycloak.local/realms/bitcode/protocol/openid-connect/token";

    private static (ServiceTokenProvider Sut, FakeHttpMessageHandler Handler) CreateSut(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        ServiceIdentityOptions? options = null)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler);

        var serviceIdentityOptions = Options.Create(options ?? new ServiceIdentityOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            ClientId = "bitcode-workload-a",
            ClientSecret = "s3cr3t",
        });

        return (new ServiceTokenProvider(httpClient, serviceIdentityOptions, new ServiceTokenCache()), handler);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, string tokenJson)
    {
        if (request.RequestUri!.ToString() == MetadataAddress)
        {
            return JsonResponse(HttpStatusCode.OK, $$"""{"token_endpoint":"{{TokenEndpoint}}"}""");
        }

        return JsonResponse(HttpStatusCode.OK, tokenJson);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetAccessTokenAsync_WithValidCredentials_ReturnsToken()
    {
        var (sut, handler) = CreateSut(request => Respond(
            request,
            """{"access_token":"svc-at-123","token_type":"Bearer","expires_in":300}"""));

        var result = await sut.GetAccessTokenAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("svc-at-123");
        result.Value.TokenType.Should().Be("Bearer");
        handler.InvocationCount.Should().Be(2, "primero resuelve el token_endpoint por descubrimiento, luego pide el token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_PostsClientCredentialsGrantWithClientIdAndSecret()
    {
        var (sut, handler) = CreateSut(request => Respond(
            request,
            """{"access_token":"svc-at-123","expires_in":300}"""));

        await sut.GetAccessTokenAsync();

        var tokenRequestBody = handler.LastRequestBody!;
        tokenRequestBody.Should().Contain("grant_type=client_credentials");
        tokenRequestBody.Should().Contain("client_id=bitcode-workload-a");
        tokenRequestBody.Should().Contain("client_secret=s3cr3t");
    }

    [Fact]
    public async Task GetAccessTokenAsync_IncludesScope_WhenConfigured()
    {
        var options = new ServiceIdentityOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            ClientId = "bitcode-workload-a",
            ClientSecret = "s3cr3t",
            Scope = "api.productos.read api.productos.write",
        };

        var (sut, handler) = CreateSut(
            request => Respond(request, """{"access_token":"svc-at-123","expires_in":300}"""),
            options);

        await sut.GetAccessTokenAsync();

        handler.LastRequestBody!.Should().Contain("scope=api.productos.read+api.productos.write");
    }

    [Fact]
    public async Task GetAccessTokenAsync_CalledTwiceWhileTokenValid_ReusesCachedTokenWithoutNewHttpCall()
    {
        var (sut, handler) = CreateSut(request => Respond(
            request,
            """{"access_token":"svc-at-123","expires_in":300}"""));

        var first = await sut.GetAccessTokenAsync();
        var invocationsAfterFirst = handler.InvocationCount;
        var second = await sut.GetAccessTokenAsync();

        first.Value.AccessToken.Should().Be(second.Value.AccessToken);
        handler.InvocationCount.Should().Be(invocationsAfterFirst, "el segundo llamado debe reutilizar el token cacheado, sin ninguna llamada HTTP adicional");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenCachedTokenIsCloseToExpiring_RequestsANewOne()
    {
        // expires_in=30 queda por debajo del margen de renovación (60s) apenas se obtiene -- la
        // siguiente llamada debe pedir un token nuevo en vez de reutilizar el que está por vencer.
        var callCount = 0;
        var (sut, handler) = CreateSut(request =>
        {
            if (request.RequestUri!.ToString() == MetadataAddress)
            {
                return JsonResponse(HttpStatusCode.OK, $$"""{"token_endpoint":"{{TokenEndpoint}}"}""");
            }

            callCount++;
            return JsonResponse(HttpStatusCode.OK, $$"""{"access_token":"svc-at-{{callCount}}","expires_in":30}""");
        });

        var first = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        first.Value.AccessToken.Should().Be("svc-at-1");
        second.Value.AccessToken.Should().Be("svc-at-2", "el primer token expira dentro del margen de renovación, así que debe pedirse uno nuevo");
        handler.InvocationCount.Should().Be(3, "descubrimiento (cacheado) + 2 solicitudes de token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithInvalidClientCredentials_ReturnsUnauthorizedFailure()
    {
        var (sut, _) = CreateSut(request =>
        {
            if (request.RequestUri!.ToString() == MetadataAddress)
            {
                return JsonResponse(HttpStatusCode.OK, $$"""{"token_endpoint":"{{TokenEndpoint}}"}""");
            }

            return JsonResponse(HttpStatusCode.Unauthorized, """
                {"error":"invalid_client","error_description":"Client secret inválido"}
                """);
        });

        var result = await sut.GetAccessTokenAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("ServiceIdentity.TokenRequest.invalid_client");
        result.Error.Description.Should().Contain("Client secret");
    }

    [Fact]
    public async Task GetAccessTokenAsync_FailedAttempt_IsNotCached_NextCallRetries()
    {
        var attempt = 0;
        var (sut, _) = CreateSut(request =>
        {
            if (request.RequestUri!.ToString() == MetadataAddress)
            {
                return JsonResponse(HttpStatusCode.OK, $$"""{"token_endpoint":"{{TokenEndpoint}}"}""");
            }

            attempt++;
            return attempt == 1
                ? JsonResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""")
                : JsonResponse(HttpStatusCode.OK, """{"access_token":"svc-at-recovered","expires_in":300}""");
        });

        var first = await sut.GetAccessTokenAsync();
        var second = await sut.GetAccessTokenAsync();

        first.IsFailure.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.AccessToken.Should().Be("svc-at-recovered");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenClientSecretIsRevokedButTheCachedTokenIsStillValid_KeepsServingItWithoutContactingTheIdp()
    {
        // F2-06, política de revocación de identidad de servicio: OAuth2 Client Credentials no tiene un
        // canal de revocación "push" -- el IdP no puede avisarle a este proceso que un ClientSecret ya
        // fue revocado/rotado. Mientras el token ya obtenido siga vigente (fuera del margen de
        // renovación, TokenRefreshSkew=60s), ServiceTokenCache lo sigue sirviendo sin volver a
        // autenticarse contra el IdP -- exactamente el comportamiento esperado (evitar una llamada HTTP
        // por request es la razón de ser del cache), no un agujero de seguridad: el token en sí ya fue
        // validado por su propia vigencia ("exp") en cada API que lo reciba (F2-05), la revocación del
        // ClientSecret solo importa para la PRÓXIMA renovación (ver el otro test de este mismo grupo).
        var (sut, handler) = CreateSut(request => Respond(
            request,
            """{"access_token":"svc-at-vigente","expires_in":300}"""));

        var mientrasElSecretEraValido = await sut.GetAccessTokenAsync();
        var invocacionesTrasElPrimerToken = handler.InvocationCount;

        // El secreto se revoca AHORA en el IdP -- pero como el token de arriba sigue vigente, ninguna
        // llamada subsiguiente debería siquiera intentar contactar al IdP para descubrirlo.
        var inmediatamenteDespuesDeRevocar = await sut.GetAccessTokenAsync();

        inmediatamenteDespuesDeRevocar.IsSuccess.Should().BeTrue();
        inmediatamenteDespuesDeRevocar.Value.AccessToken.Should().Be(mientrasElSecretEraValido.Value.AccessToken);
        handler.InvocationCount.Should().Be(invocacionesTrasElPrimerToken,
            "un token ya cacheado y todavía vigente no debe generar ninguna llamada nueva al IdP, revocado o no el ClientSecret");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenClientSecretIsRevoked_TheNextRenewalFailsInsteadOfServingAStaleToken()
    {
        // Complemento del test anterior: en cuanto ServiceTokenCache decide que el token cacheado ya no
        // es utilizable (aquí, simulado con expires_in=30, por debajo del margen de renovación de 60s --
        // mismo mecanismo que GetAccessTokenAsync_WhenCachedTokenIsCloseToExpiring_RequestsANewOne), la
        // siguiente llamada SÍ intenta renovar contra el IdP -- y si el ClientSecret ya fue revocado, el
        // IdP la rechaza (invalid_client) igual que rechazaría a un cliente nuevo con esa misma
        // credencial: no existe una vía por la que el cache siga sirviendo tokens indefinidamente después
        // de una revocación real.
        var secretYaRevocado = false;
        var (sut, _) = CreateSut(request =>
        {
            if (request.RequestUri!.ToString() == MetadataAddress)
            {
                return JsonResponse(HttpStatusCode.OK, $$"""{"token_endpoint":"{{TokenEndpoint}}"}""");
            }

            return secretYaRevocado
                ? JsonResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"Client secret inválido"}""")
                : JsonResponse(HttpStatusCode.OK, """{"access_token":"svc-at-por-vencer","expires_in":30}""");
        });

        var primerToken = await sut.GetAccessTokenAsync();
        secretYaRevocado = true;
        var resultadoTrasRenovar = await sut.GetAccessTokenAsync();

        primerToken.IsSuccess.Should().BeTrue();
        resultadoTrasRenovar.IsFailure.Should().BeTrue(
            "el token anterior ya no es utilizable (expires_in=30 está dentro del margen de renovación) y el intento de renovarlo usa el ClientSecret ya revocado");
        resultadoTrasRenovar.Error.Code.Should().Be("ServiceIdentity.TokenRequest.invalid_client");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithoutClientSecretOrCertificate_ReturnsFailure_WithoutAnyHttpCall()
    {
        var options = new ServiceIdentityOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            ClientId = "bitcode-workload-a",
        };

        var (sut, handler) = CreateSut(_ => throw new InvalidOperationException("No debería llamar al IdP sin credencial"), options);

        var result = await sut.GetAccessTokenAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceIdentity.MissingCredential");
        handler.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithCertificateThumbprintConfigured_ReturnsNotSupportedFailure()
    {
        // F2-04: contrato preparado para autenticación por certificado, pero el flujo concreto todavía
        // no está implementado -- debe fallar explícitamente, nunca ignorar el thumbprint en silencio.
        var options = new ServiceIdentityOptions
        {
            Authority = "https://keycloak.local/realms/bitcode",
            ClientId = "bitcode-workload-a",
            CertificateThumbprint = "AA11BB22",
        };

        var (sut, handler) = CreateSut(_ => throw new InvalidOperationException("No debería llamar al IdP"), options);

        var result = await sut.GetAccessTokenAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceIdentity.CertificateAuthenticationNotSupported");
        handler.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithSuccessStatusButNoAccessToken_ReturnsFailure()
    {
        var (sut, _) = CreateSut(request => Respond(request, "{}"));

        var result = await sut.GetAccessTokenAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ServiceIdentity.TokenRequest.InvalidResponse");
    }
}
