using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

[CollectionDefinition(Name)]
public class VaultCollection : ICollectionFixture<VaultContainerFixture>
{
    public const string Name = "Vault secret provider integration tests";
}
