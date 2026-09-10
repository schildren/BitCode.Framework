using BitCode.Framework.Shared.Infrastructure.Web.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MyApp.Modules.Elementos;

// Extension method que mapea el endpoint HTTP de este feature -- wireala desde el
// *EndpointRouteBuilderExtensions.cs del módulo destino, sobre el MapGroup ya versionado del módulo,
// encadenando ahí mismo ".RequireAuthorization(...)" (el builder devuelto es un IEndpointConventionBuilder,
// igual que el que devuelve directamente "endpoints.MapPost(...)") -- ver README.md de este template y
// ModuleNameEndpointRouteBuilderExtensions.cs, generado por "dotnet new bitcode-module", para el patrón
// completo -- ProblemDetails RFC 7807 vía ResultExtensions.ToProblemDetails.
public static class FeatureNameEndpoints
{
    public static IEndpointConventionBuilder MapFeatureNameEndpoint(this IEndpointRouteBuilder group)
    {
        return group.MapPost("/", async (FeatureNameCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/{result.Value}", result.Value)
                : result.ToProblemDetails();
        });
    }
}
