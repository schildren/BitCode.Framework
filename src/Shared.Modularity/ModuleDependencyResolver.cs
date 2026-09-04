namespace BitCode.Framework.Shared.Modularity;

public static class ModuleDependencyResolver
{
    /// <summary>
    /// Ordenamiento topológico (DFS) de los tipos de módulo según [DependsOn]. Un módulo sin
    /// [DependsOn] no tiene restricciones de orden respecto a los demás. Lanza si detecta un ciclo.
    /// </summary>
    public static IReadOnlyList<Type> OrderByDependencies(IReadOnlyCollection<Type> moduleTypes)
    {
        var moduleTypeSet = moduleTypes.ToHashSet();
        var visited = new HashSet<Type>();
        var visiting = new HashSet<Type>();
        var ordered = new List<Type>();

        foreach (var moduleType in moduleTypes)
        {
            Visit(moduleType);
        }

        return ordered;

        void Visit(Type moduleType)
        {
            if (visited.Contains(moduleType))
            {
                return;
            }

            if (!visiting.Add(moduleType))
            {
                throw new InvalidOperationException(
                    $"Se detectó una dependencia circular de módulos que incluye '{moduleType.FullName}'.");
            }

            var dependsOn = moduleType.GetCustomAttributes(typeof(DependsOnAttribute), inherit: false)
                .Cast<DependsOnAttribute>()
                .SelectMany(attribute => attribute.DependedOnModuleTypes);

            foreach (var dependency in dependsOn)
            {
                if (!moduleTypeSet.Contains(dependency))
                {
                    throw new InvalidOperationException(
                        $"El módulo '{moduleType.FullName}' depende de '{dependency.FullName}', que no fue " +
                        "encontrado entre los assemblies escaneados por AddModules.");
                }

                Visit(dependency);
            }

            visiting.Remove(moduleType);
            visited.Add(moduleType);
            ordered.Add(moduleType);
        }
    }
}
