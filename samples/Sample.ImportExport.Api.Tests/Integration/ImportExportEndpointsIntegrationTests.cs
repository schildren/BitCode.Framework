using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BitCode.Framework.Platform.ImportExport;
using BitCode.Framework.Platform.ImportExport.Exportacion;
using BitCode.Framework.Platform.ImportExport.Importacion;
using BitCode.Framework.Platform.ImportExport.Procesamiento;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sample.ImportExport.Api.Clientes;

namespace Sample.ImportExport.Api.Tests.Integration;

/// <summary>
/// Verifica, contra SQL Server real (Testcontainers) y un filesystem real (no un mock de
/// <c>IImportExportFileStore</c>), el módulo Import and Export (Fase 6, módulo 10): validación por fila,
/// procesamiento en lotes con progreso incremental, aislamiento de errores por fila y reanudación tras
/// simular una caída del proceso entre ciclos del job.
/// </summary>
public class ImportExportEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private readonly string _fileStoreRootPath = Path.Combine(Path.GetTempPath(), $"importexport-tests-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly IReadOnlyList<string> TodosLosPermisos =
    [
        ImportExportPermissions.ImportacionesIniciar,
        ImportExportPermissions.ImportacionesVer,
        ImportExportPermissions.ExportacionesIniciar,
        ImportExportPermissions.ExportacionesVer,
    ];

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();
        Directory.CreateDirectory(_fileStoreRootPath);

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleImportExportApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleImportExportApiTestsIdentity", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Clientes",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleImportExportApiTestsClientes", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("ImportExport__FileStore__RootPath", _fileStoreRootPath);
        Environment.SetEnvironmentVariable("ImportExport__Options__TamanoLoteFilas", "3");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sqlServerFixture.DisposeAsync();

        if (Directory.Exists(_fileStoreRootPath))
        {
            Directory.Delete(_fileStoreRootPath, recursive: true);
        }
    }

    private async Task<string> SeedActorAsync(string userName, IReadOnlyList<string>? permissions = null, Guid? tenantId = null)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var roleName = $"rol-{userName}";
        var role = new ApplicationRole(roleName);
        (await roleManager.CreateAsync(role)).Succeeded.Should().BeTrue();

        foreach (var permission in permissions ?? [])
        {
            (await roleManager.AddPermissionAsync(role, permission)).Succeeded.Should().BeTrue();
        }

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local", TenantId = tenantId ?? TenantId };
        (await userManager.CreateAsync(user, "Contraseña!Segura1")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        return tokenGenerator.GenerateAccessToken(user, [roleName], []);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }

        return request;
    }

    private async Task<Guid> IniciarImportacionAsync(string token, string tipoImportacion, string contenidoCsv)
    {
        using var content = new MultipartFormDataContent();
        var archivoContent = new ByteArrayContent(Encoding.UTF8.GetBytes(contenidoCsv));
        archivoContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(archivoContent, "archivo", "clientes.csv");
        content.Add(new StringContent(tipoImportacion), "tipoImportacion");

        var request = BuildRequest(HttpMethod.Post, "/api/v1/importexport/importaciones", token);
        request.Content = content;

        var response = await _client!.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>Invoca <see cref="ImportBatchProcessorJob"/> directamente (mismo criterio que
    /// <c>Sample.IntegrationHub.Api.Tests</c> con <c>IntegrationOutboundProcessorJob</c>) -- CADA llamada
    /// simula UN disparo del trigger de Quartz, en un scope de DI NUEVO cada vez (mismo <c>DbContext</c>
    /// nunca reutilizado entre ciclos) para ejercer honestamente la reanudación basada en checkpoint
    /// persistido, no en estado en memoria de un DbContext de larga vida.</summary>
    private async Task ProcesarUnCicloDeImportacionAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ImportExportDbContext>();
        var fileStore = scope.ServiceProvider.GetRequiredService<BitCode.Framework.Platform.ImportExport.Almacenamiento.IImportExportFileStore>();
        var rowHandlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IImportRowHandler>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ImportExportOptions>>();

        var job = new ImportBatchProcessorJob(dbContext, fileStore, rowHandlers, options);
        await job.ProcesarPendientesAsync(CancellationToken.None);
    }

    private async Task ProcesarUnCicloDeExportacionAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ImportExportDbContext>();
        var fileStore = scope.ServiceProvider.GetRequiredService<BitCode.Framework.Platform.ImportExport.Almacenamiento.IImportExportFileStore>();
        var dataSources = scope.ServiceProvider.GetRequiredService<IEnumerable<IExportDataSource>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ImportExportOptions>>();

        var job = new ExportBatchProcessorJob(dbContext, fileStore, dataSources, options);
        await job.ProcesarPendientesAsync(CancellationToken.None);
    }

    private async Task<ImportJobResponseDto> ObtenerImportJobAsync(string token, Guid id)
    {
        var response = await _client!.SendAsync(BuildRequest(HttpMethod.Get, $"/api/v1/importexport/importaciones/{id}", token));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportJobResponseDto>())!;
    }

    [Fact]
    public async Task ObtenerImportacion_SinAutenticacion_Retorna401()
    {
        var response = await _client!.GetAsync($"/api/v1/importexport/importaciones/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IniciarImportacion_ConTipoNoRegistrado_Retorna404()
    {
        var token = await SeedActorAsync("admin-tipo-no-registrado", TodosLosPermisos);

        using var content = new MultipartFormDataContent();
        var archivoContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Nombre,Email\r\nAna,ana@test.local\r\n"));
        content.Add(archivoContent, "archivo", "clientes.csv");
        content.Add(new StringContent("tipo-inexistente"), "tipoImportacion");

        var request = BuildRequest(HttpMethod.Post, "/api/v1/importexport/importaciones", token);
        request.Content = content;

        var response = await _client!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Cierra la brecha de cobertura señalada por auditoría de arquitectura (2026-09-09):
    /// <c>IniciarImportacionCommandValidator</c> declara el límite de 10 MB pero ningún test lo ejercía
    /// -- el validador SÍ corre de verdad gracias al fix de <c>includeInternalTypes: true</c>
    /// (docs/gate-fase6-hallazgo-validadores.md), pero eso no bastaba como evidencia sin un test real que
    /// envíe un archivo por encima del límite.</summary>
    [Fact]
    public async Task IniciarImportacion_ConArchivoMayorA10MB_Retorna400()
    {
        var token = await SeedActorAsync("admin-archivo-grande", TodosLosPermisos);

        var contenidoDemasiadoGrande = new byte[(10 * 1024 * 1024) + 1];
        using var content = new MultipartFormDataContent();
        var archivoContent = new ByteArrayContent(contenidoDemasiadoGrande);
        content.Add(archivoContent, "archivo", "clientes-grande.csv");
        content.Add(new StringContent("clientes"), "tipoImportacion");

        var request = BuildRequest(HttpMethod.Post, "/api/v1/importexport/importaciones", token);
        request.Content = content;

        var response = await _client!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Cierra la brecha de cobertura señalada por auditoría de arquitectura (2026-09-09): el
    /// rechazo de contenido no-UTF8 válido (<c>IniciarImportacionCommandHandler.cs</c>,
    /// "ImportExport.Importaciones.ArchivoNoEsTextoValido") nunca se había ejercido con un test real.</summary>
    [Fact]
    public async Task IniciarImportacion_ConContenidoNoUtf8Valido_Retorna400()
    {
        var token = await SeedActorAsync("admin-archivo-no-utf8", TodosLosPermisos);

        // Secuencia de bytes que NO es UTF-8 válido (0xFF 0xFE seguido de bytes que no forman una
        // secuencia UTF-8 completable) -- Encoding.UTF8.GetString con throwOnInvalidBytes lanza para
        // este patrón.
        byte[] contenidoInvalido = [0xFF, 0xFE, 0x00, 0xD8, 0x00, 0x00];
        using var content = new MultipartFormDataContent();
        var archivoContent = new ByteArrayContent(contenidoInvalido);
        content.Add(archivoContent, "archivo", "clientes-invalido.csv");
        content.Add(new StringContent("clientes"), "tipoImportacion");

        var request = BuildRequest(HttpMethod.Post, "/api/v1/importexport/importaciones", token);
        request.Content = content;

        var response = await _client!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Ejercita "lotes" y "progreso" (Fase 6, módulo 10): con <c>TamanoLoteFilas=3</c> (ver
    /// appsettings.json de este host) y 7 filas de datos, hacen falta TRES ciclos del job para completar
    /// -- el progreso debe avanzar de forma visible entre cada uno, nunca saltar directo a "completo".</summary>
    [Fact]
    public async Task Importacion_CaminoFeliz_AvanzaElProgresoEnCadaCicloYTerminaCompletado()
    {
        var token = await SeedActorAsync("admin-camino-feliz", TodosLosPermisos);
        var csv = "Nombre,Email\r\n" + string.Join("\r\n", Enumerable.Range(1, 7).Select(i => $"Cliente{i},cliente{i}@test.local")) + "\r\n";

        var jobId = await IniciarImportacionAsync(token, "clientes", csv);

        var inicial = await ObtenerImportJobAsync(token, jobId);
        inicial.Estado.Should().Be((int)ImportJobEstado.Pendiente);
        inicial.FilasTotales.Should().Be(7);
        inicial.FilasProcesadas.Should().Be(0);

        await ProcesarUnCicloDeImportacionAsync();
        var trasPrimerCiclo = await ObtenerImportJobAsync(token, jobId);
        trasPrimerCiclo.Estado.Should().Be((int)ImportJobEstado.EnProgreso);
        trasPrimerCiclo.FilasProcesadas.Should().Be(3);

        await ProcesarUnCicloDeImportacionAsync();
        var trasSegundoCiclo = await ObtenerImportJobAsync(token, jobId);
        trasSegundoCiclo.Estado.Should().Be((int)ImportJobEstado.EnProgreso);
        trasSegundoCiclo.FilasProcesadas.Should().Be(6);

        await ProcesarUnCicloDeImportacionAsync();
        var final = await ObtenerImportJobAsync(token, jobId);
        final.Estado.Should().Be((int)ImportJobEstado.Completado);
        final.FilasProcesadas.Should().Be(7);
        final.FilasConError.Should().Be(0);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var clientesDbContext = scope.ServiceProvider.GetRequiredService<SampleClientesDbContext>();
        (await clientesDbContext.Clientes.CountAsync(c => c.TenantId == TenantId)).Should().Be(7);
    }

    /// <summary>Ejercita "reanudación" (regla dura 28) de forma honesta: procesa el primer ciclo, y recién
    /// DESPUÉS de que el checkpoint quedó persistido en la base construye un job completamente NUEVO (otro
    /// scope de DI, otro DbContext) para continuar -- simula que el proceso que corrió el primer ciclo
    /// murió y una instancia distinta del host retoma el trabajo, sin reprocesar las filas ya
    /// confirmadas.</summary>
    [Fact]
    public async Task Importacion_ReanudadaEnUnaInstanciaDeJobDistinta_NoReprocesaFilasYaConfirmadas()
    {
        var token = await SeedActorAsync("admin-reanudacion", TodosLosPermisos);
        var csv = "Nombre,Email\r\n" + string.Join("\r\n", Enumerable.Range(1, 5).Select(i => $"Reanudado{i},reanudado{i}@test.local")) + "\r\n";
        var jobId = await IniciarImportacionAsync(token, "clientes", csv);

        await ProcesarUnCicloDeImportacionAsync();
        var trasPrimerCiclo = await ObtenerImportJobAsync(token, jobId);
        trasPrimerCiclo.FilasProcesadas.Should().Be(3);

        // "Caída" simulada -- el siguiente ciclo corre en una instancia de job completamente nueva, que
        // solo conoce el checkpoint ya persistido, nunca el estado en memoria del ciclo anterior.
        await ProcesarUnCicloDeImportacionAsync();
        var final = await ObtenerImportJobAsync(token, jobId);
        final.Estado.Should().Be((int)ImportJobEstado.Completado);
        final.FilasProcesadas.Should().Be(5);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var clientesDbContext = scope.ServiceProvider.GetRequiredService<SampleClientesDbContext>();
        // Sin duplicados: el upsert por Email de ClientesImportRowHandler es idempotente, pero además
        // ninguna fila debería haberse aplicado dos veces porque el checkpoint saltea las ya confirmadas.
        (await clientesDbContext.Clientes.CountAsync(c => c.TenantId == TenantId)).Should().Be(5);
    }

    /// <summary>Ejercita "errores" (aislamiento por ítem desde el diseño inicial): dos de siete filas
    /// tienen un email inválido -- el job debe completar las otras cinco y reportar exactamente esas dos
    /// como errores DE FILA, sin abortar el resto del lote.</summary>
    [Fact]
    public async Task Importacion_ConFilasInvalidas_AislaLosErroresYProcesaElRestoDelLote()
    {
        var token = await SeedActorAsync("admin-errores", TodosLosPermisos);
        var csv = "Nombre,Email\r\n"
            + "Valido1,valido1@test.local\r\n"
            + "Invalido1,no-es-un-email\r\n"
            + "Valido2,valido2@test.local\r\n"
            + "Invalido2,tampoco-es-un-email\r\n"
            + "Valido3,valido3@test.local\r\n";
        var jobId = await IniciarImportacionAsync(token, "clientes", csv);

        await ProcesarUnCicloDeImportacionAsync();
        await ProcesarUnCicloDeImportacionAsync();

        var final = await ObtenerImportJobAsync(token, jobId);
        final.Estado.Should().Be((int)ImportJobEstado.CompletadoConErrores);
        final.FilasProcesadas.Should().Be(5);
        final.FilasConError.Should().Be(2);

        var erroresResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/importexport/importaciones/{jobId}/errores", token));
        var errores = await erroresResponse.Content.ReadFromJsonAsync<List<ImportJobErrorResponseDto>>();
        errores.Should().HaveCount(2);
        errores!.Select(e => e.NumeroFila).Should().BeEquivalentTo([2, 4]);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var clientesDbContext = scope.ServiceProvider.GetRequiredService<SampleClientesDbContext>();
        (await clientesDbContext.Clientes.CountAsync(c => c.TenantId == TenantId)).Should().Be(3);
    }

    /// <summary>Hallazgo de diseño verificado explícitamente: <c>ImportBatchProcessorJob</c> corre
    /// cross-tenant en un mismo ciclo -- dos tenants distintos importando el mismo tipo "clientes" nunca
    /// deben mezclar sus filas.</summary>
    [Fact]
    public async Task Importacion_DeDosTenantsDistintos_NuncaMezclaLosDatos()
    {
        var otroTenantId = Guid.NewGuid();
        var tokenTenantA = await SeedActorAsync("admin-tenant-a-import", TodosLosPermisos);
        var tokenTenantB = await SeedActorAsync("admin-tenant-b-import", TodosLosPermisos, otroTenantId);

        await IniciarImportacionAsync(tokenTenantA, "clientes", "Nombre,Email\r\nDeA,dea@test.local\r\n");
        await IniciarImportacionAsync(tokenTenantB, "clientes", "Nombre,Email\r\nDeB,deb@test.local\r\n");

        await ProcesarUnCicloDeImportacionAsync();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var clientesDbContext = scope.ServiceProvider.GetRequiredService<SampleClientesDbContext>();
        (await clientesDbContext.Clientes.CountAsync(c => c.TenantId == TenantId)).Should().Be(1);
        (await clientesDbContext.Clientes.CountAsync(c => c.TenantId == otroTenantId)).Should().Be(1);
    }

    /// <summary>Camino completo de exportación: siembra clientes vía importación, exporta, procesa los
    /// ciclos necesarios y descarga el resultado -- verifica que el CSV generado contenga exactamente las
    /// filas del tenant que lo pidió.</summary>
    [Fact]
    public async Task Exportacion_CaminoFeliz_GeneraElArchivoYPermiteDescargarlo()
    {
        var token = await SeedActorAsync("admin-exportacion", TodosLosPermisos);
        var csv = "Nombre,Email\r\n" + string.Join("\r\n", Enumerable.Range(1, 4).Select(i => $"ExportCliente{i},exportcliente{i}@test.local")) + "\r\n";
        await IniciarImportacionAsync(token, "clientes", csv);
        await ProcesarUnCicloDeImportacionAsync();
        await ProcesarUnCicloDeImportacionAsync();

        var iniciarRequest = BuildRequest(HttpMethod.Post, "/api/v1/importexport/exportaciones", token);
        iniciarRequest.Content = JsonContent.Create(new { TipoExportacion = "clientes", FiltroJson = (string?)null });
        var iniciarResponse = await _client!.SendAsync(iniciarRequest);
        iniciarResponse.StatusCode.Should().Be(HttpStatusCode.Created, await iniciarResponse.Content.ReadAsStringAsync());
        var exportJobId = await iniciarResponse.Content.ReadFromJsonAsync<Guid>();

        await ProcesarUnCicloDeExportacionAsync();
        await ProcesarUnCicloDeExportacionAsync();

        var detalleResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/importexport/exportaciones/{exportJobId}", token));
        var detalle = await detalleResponse.Content.ReadFromJsonAsync<ExportJobResponseDto>();
        detalle!.Estado.Should().Be((int)ExportJobEstado.Completado);
        detalle.FilasExportadas.Should().Be(4);

        var descargaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/importexport/exportaciones/{exportJobId}/descargar", token));
        descargaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var contenido = await descargaResponse.Content.ReadAsStringAsync();

        contenido.Should().StartWith("Nombre,Email\r\n");
        for (var i = 1; i <= 4; i++)
        {
            contenido.Should().Contain($"ExportCliente{i},exportcliente{i}@test.local");
        }
    }

    [Fact]
    public async Task Exportacion_AunNoFinalizada_NoPermiteDescargar()
    {
        var token = await SeedActorAsync("admin-exportacion-pendiente", TodosLosPermisos);
        await IniciarImportacionAsync(token, "clientes", "Nombre,Email\r\nUno,uno@test.local\r\n");
        await ProcesarUnCicloDeImportacionAsync();

        var iniciarRequest = BuildRequest(HttpMethod.Post, "/api/v1/importexport/exportaciones", token);
        iniciarRequest.Content = JsonContent.Create(new { TipoExportacion = "clientes", FiltroJson = (string?)null });
        var iniciarResponse = await _client!.SendAsync(iniciarRequest);
        var exportJobId = await iniciarResponse.Content.ReadFromJsonAsync<Guid>();

        var descargaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/importexport/exportaciones/{exportJobId}/descargar", token));

        descargaResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed record ImportJobResponseDto(
        Guid Id, string TipoImportacion, string NombreArchivoOriginal, int Estado, int FilasTotales, int FilasProcesadas,
        int FilasConError, string? ErrorMensaje, DateTime? FinalizadoAtUtc);

    private sealed record ImportJobErrorResponseDto(Guid Id, int NumeroFila, string MensajeError, string ContenidoFilaCrudo);

    private sealed record ExportJobResponseDto(
        Guid Id, string TipoExportacion, int Estado, int? FilasTotales, int FilasExportadas, int FilasConError,
        string? ErrorMensaje, DateTime? FinalizadoAtUtc);
}
