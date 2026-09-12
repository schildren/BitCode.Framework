using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BitCode.Framework.Shared.Infrastructure.Http.Tests.Resilience;

/// <summary>
/// Doble de prueba de <see cref="HttpMessageHandler"/> que nunca hace una llamada de red real: cada
/// invocación se resuelve con la función provista por el test, y se cuenta para poder verificar
/// cuántas veces la pipeline de resiliencia efectivamente reintentó la llamada.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int InvocationCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        InvocationCount++;
        return Task.FromResult(respond(request));
    }
}
