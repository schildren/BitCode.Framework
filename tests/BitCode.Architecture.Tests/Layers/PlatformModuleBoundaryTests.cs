using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.Layers;

public class PlatformModuleBoundaryTests
{
    private static readonly System.Reflection.Assembly IdentityAssembly = typeof(BitCode.Framework.Platform.Identity.IdentityAdministrationDbContext).Assembly;
    private static readonly System.Reflection.Assembly OrganizationAssembly = typeof(BitCode.Framework.Platform.Organization.OrganizationDbContext).Assembly;
    private static readonly System.Reflection.Assembly CatalogsAssembly = typeof(BitCode.Framework.Platform.Catalogs.CatalogsDbContext).Assembly;
    private static readonly System.Reflection.Assembly FeatureManagementAssembly = typeof(BitCode.Framework.Platform.FeatureManagement.FeatureManagementDbContext).Assembly;
    private static readonly System.Reflection.Assembly DocumentsAssembly = typeof(BitCode.Framework.Platform.Documents.DocumentsDbContext).Assembly;
    private static readonly System.Reflection.Assembly WorkflowAssembly = typeof(BitCode.Framework.Platform.Workflow.WorkflowDbContext).Assembly;

    [Fact]
    public void Platform_Modules_Must_Not_Access_Other_Modules_DbContexts_Directly()
    {
        // Regla: Ownership de datos estricto. Ningún módulo puede referenciar el DbContext de otro módulo.
        var allPlatformAssemblies = new[]
        {
            IdentityAssembly,
            OrganizationAssembly,
            CatalogsAssembly,
            FeatureManagementAssembly,
            DocumentsAssembly,
            WorkflowAssembly
        };

        foreach (var asm in allPlatformAssemblies)
        {
            var otherDbContextNames = new List<string>();

            if (asm != IdentityAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.Identity.IdentityAdministrationDbContext));
            if (asm != OrganizationAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.Organization.OrganizationDbContext));
            if (asm != CatalogsAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.Catalogs.CatalogsDbContext));
            if (asm != FeatureManagementAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.FeatureManagement.FeatureManagementDbContext));
            if (asm != DocumentsAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.Documents.DocumentsDbContext));
            if (asm != WorkflowAssembly) otherDbContextNames.Add(nameof(BitCode.Framework.Platform.Workflow.WorkflowDbContext));

            var result = Types.InAssembly(asm)
                .ShouldNot()
                .HaveDependencyOnAny(otherDbContextNames.ToArray())
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"El módulo '{asm.GetName().Name}' viola el ownership de datos al acceder al DbContext de otro módulo. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void Identity_Module_Should_Not_Depend_On_Downstream_Platform_Modules()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "BitCode.Framework.Platform.Organization",
                "BitCode.Framework.Platform.Catalogs",
                "BitCode.Framework.Platform.Documents",
                "BitCode.Framework.Platform.Workflow",
                "BitCode.Framework.Platform.FeatureManagement")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Identity es un módulo base y no debe acoplarse a módulos aguas abajo. Violaciones: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
