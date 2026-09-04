using System.Net;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.Modularity;

public class WebApplicationModularityExtensionsTests
{
    [Fact]
    public async Task UseModules_InvokesConfigureApplicationOfRegisteredWebModules()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModules(
            new ConfigurationBuilder().Build(),
            typeof(ProbeWebModule).Assembly);

        var app = builder.Build();
        app.UseModules();

        await app.StartAsync();
        var testServer = (TestServer)app.Services.GetRequiredService<IServer>();
        using var client = testServer.CreateClient();

        var response = await client.GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("probe-module-ran");

        await app.StopAsync();
    }
}
