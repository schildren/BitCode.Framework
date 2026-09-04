namespace BitCode.Framework.Shared.Modularity;

/// <summary>
/// Declara que un IFrameworkModule debe configurarse después de los módulos indicados
/// (equivalente al DependsOn de ASP.NET Boilerplate). AddModules ordena topológicamente respetando
/// estas dependencias antes de invocar ConfigureServices.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DependsOnAttribute(params Type[] dependedOnModuleTypes) : Attribute
{
    public Type[] DependedOnModuleTypes { get; } = dependedOnModuleTypes;
}
