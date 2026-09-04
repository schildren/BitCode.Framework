using BitCode.Framework.Shared.Application.Messaging;

namespace MyApp.Application.Features;

public record FeatureNameCommand : ICommand<Guid>;
