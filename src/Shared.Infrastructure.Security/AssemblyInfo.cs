using System.Runtime.CompilerServices;

// F4-12: expone FeatureFlagChangeAuditingService.ComputeChanges (internal static, cálculo puro del diff
// old -> new entre dos snapshots de flags) a la suite de pruebas -- mismo patrón que
// src/Shared.Kernel/AssemblyInfo.cs. El resto del ensamblado sigue exponiendo únicamente su API pública
// declarada (PublicAPI.Shipped.txt/PublicAPI.Unshipped.txt); esta excepción es deliberadamente puntual
// para no forzar que un detalle de implementación interno se vuelva contrato público solo para poder
// probarlo unitariamente sin depender del comportamiento asíncrono end-to-end del hosted service.
[assembly: InternalsVisibleTo("Shared.Infrastructure.Security.Tests")]
