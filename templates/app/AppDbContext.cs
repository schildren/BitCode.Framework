using AppName.Elementos;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace AppName;

// El nombre de esta clase NO se deriva de "AppName" a propósito: si el nombre del proyecto contiene un
// punto (ej. "MiEmpresa.Api", la convención habitual de docs/convenciones.md), reemplazar "AppName"
// dentro de un identificador de C# generaría un nombre de tipo inválido ("MiEmpresa.ApiDbContext" no
// compila -- un punto no es válido dentro de un nombre de clase). Mismo criterio que
// samples/Sample.Api/SampleDbContext.cs, que tampoco deriva su nombre del nombre completo del proyecto.
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Elemento> Elementos => Set<Elemento>();
}
