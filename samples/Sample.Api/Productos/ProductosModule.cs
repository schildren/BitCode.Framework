using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Modularity;
using Sample.Api;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Api.Productos;

[DependsOn(typeof(InfrastructureModule))]
public class ProductosModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Sin registros propios: el repositorio genérico (Fase 1) y MediatR (Fase 2) ya cubren
        // todo lo que este feature necesita, registrados desde InfrastructureModule.
    }

    public void ConfigureApplication(WebApplication app)
    {
        var group = app.MapGroup("/productos");

        group.MapPost("/", async (CrearProductoCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/productos/{result.Value}", result.Value)
                : result.ToProblemDetails();
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ObtenerProductoQuery(id), ct);
            return result.ToOkOrProblem();
        });
    }
}
