using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BitCode.Framework.Platform.Documents;
using BitCode.Framework.Platform.Documents.Almacenamiento;
using BitCode.Framework.Platform.Documents.Antivirus;
using BitCode.Framework.Platform.Documents.Documentos;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Documents.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers) y un filesystem real (no un mock),
/// el módulo Documents (Fase 6, módulo 5): alta con primera versión (con idempotencia), hash de
/// contenido, escaneo antivirus (incluido el caso Infectado con el string de prueba EICAR), versionado
/// (preserva versiones anteriores), descarga segura (RBAC + ABAC por documento), retención/disposición y,
/// el criterio explícito del Gate de salida de Fase 6 ("Workflow y Documents tienen pruebas de seguridad y
/// recuperación"), al menos una prueba de seguridad (descarga fuera de alcance ABAC/de una versión
/// infectada) y una de recuperación (metadata presente, archivo físico ausente). Genera el JWT
/// directamente vía <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Sample.FeatureManagement.Api.Tests/Integration/FeatureManagementEndpointsIntegrationTests.cs</c>.
/// </summary>
public class DocumentsEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string _blobRootPath = string.Empty;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleDocumentsApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleDocumentsApiTestsIdentity", Guid.NewGuid().ToString("N")));

        // Directorio de blobs aislado por instancia de este suite -- filesystem real, nunca un mock de
        // IDocumentBlobStore (mismo criterio de "pruebas reales" que SQL Server vía Testcontainers).
        _blobRootPath = Path.Combine(Path.GetTempPath(), "bitcode-documents-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_blobRootPath);

        // Configura, solo para este suite de tests, la regla ABAC de alcance por "documentoId" que
        // docs/guia-documents.md documenta como la extensión típica de un consumidor real (el host de
        // referencia, Sample.Documents.Api, no la registra por defecto) -- necesaria para demostrar el
        // criterio de aceptación "RBAC y ABAC en operaciones sensibles" de punta a punta, y sobrescribe
        // Documents:BlobStore:RootPath con el directorio aislado de este suite.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Configure<AbacOptions>(options =>
                    options.ScopeRules.Add(new AbacScopeAttributeRule
                    {
                        ResourceType = "documents.documentos",
                        ResourceAttributeKey = "documentoId",
                        ClaimType = "documento_id",
                    }));
                services.Configure<DocumentBlobStoreOptions>(options => options.RootPath = _blobRootPath);
            }));
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

        if (Directory.Exists(_blobRootPath))
        {
            Directory.Delete(_blobRootPath, recursive: true);
        }
    }

    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<(Guid UserId, string Token)> SeedActorAsync(
        string userName, IReadOnlyList<string>? permissions = null, IReadOnlyList<Claim>? extraClaims = null)
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

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local", TenantId = TenantId };
        (await userManager.CreateAsync(user, "Contraseña!Segura1")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        var token = tokenGenerator.GenerateAccessToken(user, [roleName], extraClaims ?? []);
        return (user.Id, token);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string token, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        }

        return request;
    }

    private static HttpRequestMessage BuildCrearDocumentoRequest(
        string token, byte[] contenido, string nombreArchivo = "archivo.txt", string titulo = "Contrato de prueba",
        string clasificacion = "Contrato", int retencionDias = 30, string? idempotencyKey = null)
    {
        var request = BuildRequest(HttpMethod.Post, "/api/v1/documentos", token, idempotencyKey);
        var content = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent(clasificacion), "clasificacion" },
            { new StringContent(retencionDias.ToString()), "retencionDias" },
        };
        var archivoContent = new ByteArrayContent(contenido);
        archivoContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(archivoContent, "archivo", nombreArchivo);
        request.Content = content;
        return request;
    }

    private static HttpRequestMessage BuildSubirVersionRequest(
        Guid documentoId, string token, byte[] contenido, string nombreArchivo = "archivo-v2.txt", string? idempotencyKey = null)
    {
        var request = BuildRequest(HttpMethod.Post, $"/api/v1/documentos/{documentoId}/versiones", token, idempotencyKey);
        var content = new MultipartFormDataContent();
        var archivoContent = new ByteArrayContent(contenido);
        archivoContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(archivoContent, "archivo", nombreArchivo);
        request.Content = content;
        return request;
    }

    private static string ComputeHash(byte[] contenido) => Convert.ToHexString(SHA256.HashData(contenido));

    [Fact]
    public async Task CrearDocumento_SinAutenticacion_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos")
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent("Título"), "titulo" },
                { new StringContent("Contrato"), "clasificacion" },
                { new StringContent("30"), "retencionDias" },
                { new ByteArrayContent("contenido"u8.ToArray()), "archivo", "a.txt" },
            },
        };

        var response = await _client!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearDocumento_ConPermiso_PersisteConHashYQuedaLimpio_YEsIdempotente()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-crea-documento", [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosVer]);
        var contenido = "contenido de prueba del documento"u8.ToArray();
        var idempotencyKey = Guid.NewGuid().ToString();

        var primeraRespuesta = await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, contenido, idempotencyKey: idempotencyKey));
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var documentoId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();

        // F1-22: mismo Idempotency-Key + mismo cuerpo -> mismo resultado, sin crear un segundo documento.
        var segundaRespuesta = await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, contenido, idempotencyKey: idempotencyKey));
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(documentoId);

        var versiones = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/versiones", adminToken)))
            .Content.ReadFromJsonAsync<List<DocumentoVersionResponseDto>>();
        versiones.Should().HaveCount(1);
        versiones![0].Numero.Should().Be(1);
        versiones[0].HashSha256.Should().Be(ComputeHash(contenido));
        versiones[0].EstadoEscaneo.Should().Be(EstadoEscaneo.Limpio);

        var documento = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}", adminToken)))
            .Content.ReadFromJsonAsync<DocumentoResponseDto>();
        documento!.VersionActualNumero.Should().Be(1);
        documento.RetencionDias.Should().Be(30);
    }

    [Fact]
    public async Task Descargar_VersionVigente_DevuelveElContenidoOriginalIdentico()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-descarga-ok",
            [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosDescargar]);
        var contenido = "el contenido exacto que debe volver intacto en la descarga"u8.ToArray();

        var documentoId = await (await _client!.SendAsync(BuildCrearDocumentoRequest(adminToken, contenido)))
            .Content.ReadFromJsonAsync<Guid>();

        var descargaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar", adminToken));
        descargaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var descargado = await descargaResponse.Content.ReadAsByteArrayAsync();
        descargado.Should().Equal(contenido);
    }

    /// <summary>Prueba de seguridad #1 (Gate de salida de Fase 6): un actor sin el permiso RBAC de
    /// descarga nunca recibe el contenido, sea cual sea el resultado del escaneo.</summary>
    [Fact]
    public async Task Descargar_SinPermisoRbac_Retorna403()
    {
        var (_, creadorToken) = await SeedActorAsync("admin-crea-para-sin-permiso", [DocumentsPermissions.DocumentosCrear]);
        var documentoId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(creadorToken, "contenido"u8.ToArray())))
            .Content.ReadFromJsonAsync<Guid>();

        var (_, actorSinPermisoToken) = await SeedActorAsync("actor-sin-permiso-descarga");

        var respuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar", actorSinPermisoToken));

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Prueba de seguridad #2 (Gate de salida de Fase 6, caso de referencia de "RBAC y ABAC en
    /// operaciones sensibles"): el actor TIENE el permiso RBAC pero su claim <c>documento_id</c> solo cubre
    /// OTRO documento -- la regla ABAC de alcance deniega la descarga de todos modos.</summary>
    [Fact]
    public async Task Descargar_FueraDeAlcanceAbac_EsDenegadoAunqueTengaElPermisoRbac()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-crea-documentos-abac", [DocumentsPermissions.DocumentosCrear]);

        var documentoPropioId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, "contenido propio"u8.ToArray())))
            .Content.ReadFromJsonAsync<Guid>();
        var documentoAjenoId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, "contenido ajeno"u8.ToArray())))
            .Content.ReadFromJsonAsync<Guid>();

        var (_, actorLimitadoToken) = await SeedActorAsync(
            "actor-limitado-descarga-abac",
            [DocumentsPermissions.DocumentosDescargar],
            [new Claim("documento_id", documentoPropioId.ToString())]);

        var descargaPropia = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoPropioId}/descargar", actorLimitadoToken));
        descargaPropia.StatusCode.Should().Be(HttpStatusCode.OK);

        var descargaAjena = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoAjenoId}/descargar", actorLimitadoToken));
        descargaAjena.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Prueba de seguridad #3, criterio de aceptación central de "Escaneo antivirus": el string de
    /// prueba estándar de la industria (EICAR) queda marcado Infectado y NUNCA puede descargarse, aunque el
    /// actor tenga permiso RBAC y esté dentro de alcance ABAC.</summary>
    [Fact]
    public async Task SubirDocumentoConFirmaEicar_QuedaInfectado_YLaDescargaSeRechaza()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-eicar", [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosVer, DocumentsPermissions.DocumentosDescargar]);
        var contenidoInfectado = Encoding.ASCII.GetBytes(ReferenceAntivirusScanner.EicarSignature);

        var documentoId = await (await _client!.SendAsync(BuildCrearDocumentoRequest(adminToken, contenidoInfectado)))
            .Content.ReadFromJsonAsync<Guid>();

        var versiones = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/versiones", adminToken)))
            .Content.ReadFromJsonAsync<List<DocumentoVersionResponseDto>>();
        versiones![0].EstadoEscaneo.Should().Be(EstadoEscaneo.Infectado);

        var descargaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar", adminToken));
        descargaResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Épica de Documents, "Versionado": subir una versión nueva nunca borra ni reemplaza el
    /// contenido de la anterior -- ambas siguen siendo descargables por número explícito.</summary>
    [Fact]
    public async Task SubirNuevaVersion_IncrementaNumero_YPreservaElContenidoDeLaAnterior()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-versiona-documento",
            [
                DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosSubirVersion,
                DocumentsPermissions.DocumentosDescargar, DocumentsPermissions.DocumentosVer,
            ]);
        var contenidoV1 = "contenido de la version 1"u8.ToArray();
        var contenidoV2 = "contenido de la version 2, distinto del anterior"u8.ToArray();

        var documentoId = await (await _client!.SendAsync(BuildCrearDocumentoRequest(adminToken, contenidoV1)))
            .Content.ReadFromJsonAsync<Guid>();

        var subirVersionResponse = await _client!.SendAsync(
            BuildSubirVersionRequest(documentoId, adminToken, contenidoV2));
        subirVersionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var documento = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}", adminToken)))
            .Content.ReadFromJsonAsync<DocumentoResponseDto>();
        documento!.VersionActualNumero.Should().Be(2);

        var descargaVigente = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar", adminToken)))
            .Content.ReadAsByteArrayAsync();
        descargaVigente.Should().Equal(contenidoV2);

        var descargaV1 = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar?numero=1", adminToken)))
            .Content.ReadAsByteArrayAsync();
        descargaV1.Should().Equal(contenidoV1);
    }

    /// <summary>Épica de Documents, "Retención y disposición": disponer antes de cumplirse la retención
    /// es un conflicto de negocio esperado (nunca una excepción).</summary>
    [Fact]
    public async Task Disponer_AntesDeVencerLaRetencion_Retorna409()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-disponer-temprano", [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosDisponer]);
        var documentoId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, "contenido"u8.ToArray(), retencionDias: 3650)))
            .Content.ReadFromJsonAsync<Guid>();

        var respuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Delete, $"/api/v1/documentos/{documentoId}", adminToken));

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Disponer_ConRetencionCero_DisponeInmediatamenteYElDocumentoDejaDeSerVisible()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-disponer-inmediato",
            [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosDisponer, DocumentsPermissions.DocumentosVer]);
        var documentoId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, "contenido"u8.ToArray(), retencionDias: 0)))
            .Content.ReadFromJsonAsync<Guid>();

        var respuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Delete, $"/api/v1/documentos/{documentoId}", adminToken));
        respuesta.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var obtenerLuegoDeDisponer = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}", adminToken));
        obtenerLuegoDeDisponer.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Prueba de RECUPERACIÓN (Gate de salida de Fase 6): metadata presente en <see cref="DocumentsDbContext"/>,
    /// archivo físico ausente en <see cref="IDocumentBlobStore"/> (simulado borrándolo directamente del
    /// filesystem real de este suite) -- la descarga debe degradar a un <c>Result.Failure</c> auditado y
    /// controlado (500 con <c>ProblemDetails</c>), nunca una excepción sin manejar/una respuesta rota.
    /// </summary>
    [Fact]
    public async Task Descargar_ConMetadataPeroSinArchivoFisico_DevuelveErrorControladoNoUnaExcepcion()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-recuperacion",
            [DocumentsPermissions.DocumentosCrear, DocumentsPermissions.DocumentosVer, DocumentsPermissions.DocumentosDescargar]);
        var documentoId = await (await _client!.SendAsync(
            BuildCrearDocumentoRequest(adminToken, "contenido que luego desaparece del disco"u8.ToArray())))
            .Content.ReadFromJsonAsync<Guid>();

        var versiones = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/versiones", adminToken)))
            .Content.ReadFromJsonAsync<List<DocumentoVersionResponseDto>>();
        var versionId = versiones![0].Id;

        // Simula corrupción/pérdida del almacenamiento físico subyacente sin tocar la metadata -- mismo
        // formato de blobKey que BlobKeyFactory.Crear (documentoId/versionId.bin).
        var archivoFisico = Path.Combine(_blobRootPath, documentoId.ToString("N"), $"{versionId:N}.bin");
        File.Exists(archivoFisico).Should().BeTrue("la subida debe haber escrito el archivo físico");
        File.Delete(archivoFisico);

        var descargaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/documentos/{documentoId}/descargar", adminToken));

        descargaResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var problem = await descargaResponse.Content.ReadFromJsonAsync<ProblemDetailsDto>();
        problem!.Title.Should().Be("Documents.Versiones.ContenidoNoDisponible");
    }

    private sealed record DocumentoResponseDto(
        Guid Id, string Titulo, string? Descripcion, string Clasificacion, int RetencionDias,
        Guid VersionActualId, int VersionActualNumero, DateTime DisponibleParaDisposicionDesde);

    private sealed record DocumentoVersionResponseDto(
        Guid Id, Guid DocumentoId, int Numero, string NombreArchivo, string ContentType,
        long TamanioBytes, string HashSha256, EstadoEscaneo EstadoEscaneo);

    private sealed record ProblemDetailsDto(string? Title, string? Detail);
}
