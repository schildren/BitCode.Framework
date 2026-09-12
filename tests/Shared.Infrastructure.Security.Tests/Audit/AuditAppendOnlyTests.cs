using System.Reflection;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

/// <summary>
/// Verifica el criterio "append-only a nivel de API del propio módulo" de F2-15: ni <see cref="IAuditWriter"/>
/// ni <see cref="AuditEntry"/> exponen ningún método/propiedad que permita actualizar o eliminar un
/// registro ya escrito -- independiente de cualquier restricción adicional que se configure a nivel de
/// base de datos (fuera del alcance de este test).
/// </summary>
public class AuditAppendOnlyTests
{
    private static readonly string[] MutationKeywords = ["Update", "Delete", "Remove", "Modify", "Edit"];

    [Fact]
    public void IAuditWriter_SoloExponeWriteAsync_SinMetodosDeActualizacionNiEliminacion()
    {
        var methods = typeof(IAuditWriter).GetMethods();

        methods.Should().ContainSingle(m => m.Name == nameof(IAuditWriter.WriteAsync));
        methods.Should().NotContain(m => MutationKeywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void InMemoryAuditWriter_NoExponeMetodosDeActualizacionNiEliminacion()
    {
        var publicMethods = typeof(InMemoryAuditWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        publicMethods.Should().NotContain(m => MutationKeywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AuditEntry_TodasLasPropiedadesSonDeSoloLectura()
    {
        var properties = typeof(AuditEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        properties.Should().NotBeEmpty();
        properties.Should().OnlyContain(p => p.CanRead && !p.CanWrite);
    }

    [Fact]
    public void AuditEntry_NoExponeMetodosDeActualizacionNiEliminacion()
    {
        var publicMethods = typeof(AuditEntry)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        publicMethods.Should().NotContain(m => MutationKeywords.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }
}
