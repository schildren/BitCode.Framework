using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Shared.Application.Messaging;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
