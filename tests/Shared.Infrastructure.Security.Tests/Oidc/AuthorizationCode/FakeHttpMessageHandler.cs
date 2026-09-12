namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Oidc.AuthorizationCode;

/// <summary>
/// Doble de prueba de <see cref="HttpMessageHandler"/> que nunca hace una llamada de red real -- mismo
/// patrón que <c>Shared.Infrastructure.Http.Tests.Resilience.FakeHttpMessageHandler</c>, reproducido
/// aquí porque ese tipo es <c>internal</c> a su propio assembly de test.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public int InvocationCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        InvocationCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }
}
