using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Shared.Application.Messaging;

public interface ICommand : IRequest<Result>, IBaseCommand;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>, IBaseCommand;

public interface IBaseCommand;
