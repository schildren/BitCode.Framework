using BitCode.Framework.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity.Tests.NegativeCases;

public class ModuleWithoutParameterlessConstructor(string requiredArgument) : IFrameworkModule
{
    public string RequiredArgument { get; } = requiredArgument;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
