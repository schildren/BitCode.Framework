using System.Net;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<TestServer> CreateServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .ConfigureServices(services => services.AddSharedExceptionHandling())
                .Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.Map("/boom", branch => branch.Run(_ => throw new InvalidOperationException("Boom")));
                    app.Map("/ok", branch => branch.Run(ctx => ctx.Response.WriteAsync("ok")));
                }))
            .StartAsync();

        return host.GetTestServer();
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithInternalServerError()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/boom");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("traceId");
    }

    [Fact]
    public async Task NoException_PassesThroughNormally()
    {
        var server = await CreateServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// F1-10 (timeouts y cancelación): un <see cref="OperationCanceledException"/> que llega hasta
    /// <c>GlobalExceptionHandler</c> porque el request ya fue abortado por el cliente NO debe tratarse
    /// como un error 500 genérico (no hay a quién responderle, y no es una falla del servidor) — se
    /// maneja (se devuelve <c>true</c> para que ASP.NET Core no intente nada más) sin escribir un
    /// status code de error sobre una conexión ya abortada.
    /// </summary>
    [Fact]
    public async Task OperationCanceled_WhenRequestAborted_IsHandledWithoutWritingErrorResponse()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cts.Token,
        };

        var handled = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException(cts.Token),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
            "GlobalExceptionHandler no debe escribir un status code de error para una cancelación " +
            "esperada del cliente");
    }

    /// <summary>
    /// Contraste con el test anterior: un <see cref="OperationCanceledException"/> que NO está
    /// relacionado con la cancelación del request (el <c>RequestAborted</c> del <c>HttpContext</c>
    /// sigue sin cancelar) sí es una excepción inesperada y debe tratarse como cualquier otra — 500
    /// con ProblemDetails.
    /// </summary>
    [Fact]
    public async Task OperationCanceled_WhenRequestNotAborted_IsHandledAsUnexpectedError()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new OperationCanceledException("No relacionado con el request"),
            CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
}
