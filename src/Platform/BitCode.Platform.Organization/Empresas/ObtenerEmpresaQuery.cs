using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Organization.Empresas;

internal sealed record ObtenerEmpresaQuery(Guid Id) : IQuery<EmpresaResponse>;

internal sealed class ObtenerEmpresaQueryHandler(IReadRepository<Empresa, Guid> repository)
    : IRequestHandler<ObtenerEmpresaQuery, Result<EmpresaResponse>>
{
    public async Task<Result<EmpresaResponse>> Handle(ObtenerEmpresaQuery request, CancellationToken cancellationToken)
    {
        var empresa = await repository.GetByIdAsync(request.Id, cancellationToken);

        return empresa is null
            ? Result.Failure<EmpresaResponse>(
                Error.NotFound("Organizacion.Empresas.NoEncontrada", $"No existe la empresa {request.Id}."))
            : new EmpresaResponse(empresa.Id, empresa.RazonSocial, empresa.Identificador, empresa.Activa);
    }
}
