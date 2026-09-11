using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using MediatR;
using NetArchTest.Rules;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Conventions;

public class CqrsConventionTests
{
    private static readonly System.Reflection.Assembly[] AllBusinessAssemblies =
    [
        typeof(BitCode.Framework.Platform.Identity.IdentityAdministrationDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Organization.OrganizationDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Catalogs.CatalogsDbContext).Assembly,
        typeof(BitCode.Framework.Platform.FeatureManagement.FeatureManagementDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Documents.DocumentsDbContext).Assembly,
        typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly
    ];

    [Fact]
    public void Command_Classes_Should_End_With_Command()
    {
        foreach (var asm in AllBusinessAssemblies)
        {
            var commandTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
                .Where(t => !t.Name.EndsWith("Query", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var cmd in commandTypes)
            {
                cmd.Name.Should().EndWith("Command",
                    $"El comando '{cmd.FullName}' en '{asm.GetName().Name}' debe seguir la convención de nomenclatura terminando en 'Command'.");
            }
        }
    }

    [Fact]
    public void Query_Classes_Should_End_With_Query()
    {
        foreach (var asm in AllBusinessAssemblies)
        {
            var queryTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
                .Where(t => !t.Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var qry in queryTypes)
            {
                qry.Name.Should().EndWith("Query",
                    $"La query '{qry.FullName}' en '{asm.GetName().Name}' debe seguir la convención de nomenclatura terminando en 'Query'.");
            }
        }
    }

    [Fact]
    public void Handlers_Should_Return_Result_Or_ResultOfT()
    {
        // Regla dura 4: Todo handler debe retornar Result o Result<T>, nunca lanzar excepciones para errores de negocio.
        foreach (var asm in AllBusinessAssemblies)
        {
            var handlerTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .SelectMany(t => t.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                    .Select(i => new { HandlerType = t, ResponseType = i.GetGenericArguments()[1] }))
                .ToList();

            foreach (var h in handlerTypes)
            {
                var resp = h.ResponseType;
                var isResult = resp == typeof(Result) ||
                               (resp.IsGenericType && resp.GetGenericTypeDefinition() == typeof(Result<>));

                isResult.Should().BeTrue(
                    $"El handler '{h.HandlerType.FullName}' retorna '{resp.Name}' pero debe retornar 'Result' o 'Result<T>'.");
            }
        }
    }

    [Fact]
    public void Handlers_Should_End_With_CommandHandler_Or_QueryHandler()
    {
        foreach (var asm in AllBusinessAssemblies)
        {
            var handlerTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .ToList();

            foreach (var h in handlerTypes)
            {
                var endsWithValidSuffix = h.Name.EndsWith("CommandHandler", StringComparison.OrdinalIgnoreCase) ||
                                         h.Name.EndsWith("QueryHandler", StringComparison.OrdinalIgnoreCase);

                endsWithValidSuffix.Should().BeTrue(
                    $"El handler '{h.FullName}' debe terminar en 'CommandHandler' o 'QueryHandler'.");
            }
        }
    }
}
