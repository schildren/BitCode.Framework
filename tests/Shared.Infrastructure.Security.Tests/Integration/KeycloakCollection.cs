using BitCode.Framework.Shared.Testing;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

[CollectionDefinition(Name)]
public class KeycloakCollection : ICollectionFixture<KeycloakContainerFixture>
{
    public const string Name = "Keycloak OIDC token validation integration tests";
}
