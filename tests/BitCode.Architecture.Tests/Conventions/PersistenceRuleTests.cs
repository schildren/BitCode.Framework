using System.Reflection;
using BitCode.Framework.Shared.Domain.Persistence;
using FluentAssertions;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Conventions;

public class PersistenceRuleTests
{
    private static readonly Assembly PersistenceAssembly = typeof(BitCode.Framework.Shared.Infrastructure.Persistence.UnitOfWork).Assembly;
    private static readonly Assembly DomainAssembly = typeof(IRepository<,>).Assembly;

    private static readonly Assembly[] AllBusinessAssemblies =
    [
        typeof(BitCode.Framework.Platform.Identity.IdentityAdministrationDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Organization.OrganizationDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Catalogs.CatalogsDbContext).Assembly,
        typeof(BitCode.Framework.Platform.FeatureManagement.FeatureManagementDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Documents.DocumentsDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly
    ];

    [Fact]
    public void Repositories_Must_Never_Expose_IQueryable()
    {
        // Regla dura 5: "Nunca exponer IQueryable desde un repositorio. Toda consulta pasa por ISpecification<T> o métodos tipados."
        var allAssembliesToCheck = new List<Assembly> { PersistenceAssembly, DomainAssembly };
        allAssembliesToCheck.AddRange(AllBusinessAssemblies);

        foreach (var asm in allAssembliesToCheck)
        {
            var repoTypes = asm.GetTypes()
                .Where(t => t.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var type in repoTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var returnType = method.ReturnType;
                    var isQueryable = typeof(IQueryable).IsAssignableFrom(returnType);

                    isQueryable.Should().BeFalse(
                        $"Regla dura 5 violada: El repositorio '{type.FullName}' expone IQueryable en el método '{method.Name}'. Toda consulta debe usar especificaciones o métodos tipados.");
                }
            }
        }
    }

    [Fact]
    public void QueryHandlers_Must_Not_Inject_IRepository_Only_IReadRepository()
    {
        // Regla dura 15: "Un handler de IQuery inyecta IReadRepository<TEntity, TId>, nunca IRepository<TEntity, TId>."
        foreach (var asm in AllBusinessAssemblies)
        {
            var queryHandlers = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("QueryHandler", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var qh in queryHandlers)
            {
                var ctors = qh.GetConstructors();
                foreach (var ctor in ctors)
                {
                    var parameters = ctor.GetParameters();
                    foreach (var param in parameters)
                    {
                        var paramType = param.ParameterType;
                        var isWriteRepository = paramType.IsGenericType &&
                                                paramType.GetGenericTypeDefinition() == typeof(IRepository<,>);

                        isWriteRepository.Should().BeFalse(
                            $"Regla dura 15 violada: El QueryHandler '{qh.FullName}' inyecta IRepository ('{param.Name}') en lugar de IReadRepository.");
                    }
                }
            }
        }
    }
}
