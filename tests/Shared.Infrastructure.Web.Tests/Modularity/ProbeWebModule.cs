using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests.Modularity;

public class ProbeWebModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.Map("/probe", branch => branch.Run(ctx => ctx.Response.WriteAsync("probe-module-ran")));
    }
}
