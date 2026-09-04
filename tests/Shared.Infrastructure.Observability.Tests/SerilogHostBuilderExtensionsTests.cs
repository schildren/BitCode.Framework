using BitCode.Framework.Shared.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Observability.Tests;

public class SerilogHostBuilderExtensionsTests
{
    [Fact]
    public void UseSharedSerilog_BuildsHostWithoutThrowing()
    {
        var builder = new HostBuilder().UseSharedSerilog();

        var act = () => builder.Build();

        act.Should().NotThrow();
    }
}
