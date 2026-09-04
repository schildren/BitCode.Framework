using System.Net;
using BitCode.Framework.Shared.Infrastructure.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
}
