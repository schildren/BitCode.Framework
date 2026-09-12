using BitCode.Framework.Shared.Infrastructure.Web.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace MyApp.Modules.Elementos;

// Extension method que mapea el endpoint HTTP de este feature -- wireala desde el
// *EndpointRouteBuilderExtensions.cs del módulo destino, sobre el MapGroup ya versionado del módulo,
// encadenando ahí mismo ".RequireAuthorization(...)" (el builder devuelto es un IEndpointConventionBuilder,
// igual que el que devuelve directamente "endpoints.MapGet(...)") -- ver README.md de este template y
// ModuleNameEndpointRouteBuilderExtensions.cs, generado por "dotnet new bitcode-module", para el patrón
// completo -- ProblemDetails RFC 7807 vía ResultExtensions.ToOkOrProblem.
public static class FeatureNameEndpoints
{
    public static IEndpointConventionBuilder MapFeatureNameEndpoint(this IEndpointRouteBuilder group)
    {
        return group.MapGet("/", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new FeatureNameQuery(), ct);
            return result.ToOkOrProblem();
        });
    }
}
