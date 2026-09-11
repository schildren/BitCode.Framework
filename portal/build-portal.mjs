import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const rootDir = path.resolve(__dirname, '..');
const docsDir = path.join(rootDir, 'docs');
const adrDir = path.join(docsDir, 'adr');
const openapiDir = path.join(docsDir, 'openapi');
const portalDataFile = path.join(__dirname, 'portal-data.json');

function extractTitle(content, fallback) {
  const match = content.match(/^#\s+(.+)$/m);
  if (match) {
    return match[1].trim();
  }
  return fallback;
}

function extractAdrMetadata(content) {
  const statusMatch = content.match(/\*\*Estado:\*\*\s*([^\r\n]+)/i);
  const dateMatch = content.match(/\*\*Fecha:\*\*\s*([^\r\n]+)/i);
  const responsibleMatch = content.match(/\*\*Responsable:\*\*\s*([^\r\n]+)/i);

  return {
    status: statusMatch ? statusMatch[1].trim() : 'Accepted',
    date: dateMatch ? dateMatch[1].trim() : '',
    responsible: responsibleMatch ? responsibleMatch[1].trim() : ''
  };
}

function classifyDoc(fileName, title) {
  const lower = fileName.toLowerCase();
  if (lower.startsWith('guia-frontend-')) return 'Frontend';
  if (lower.startsWith('guia-')) return 'Guías de Desarrollo';
  if (lower.startsWith('politica-')) return 'Políticas y Gobierno';
  if (lower.startsWith('fase-') || lower.startsWith('gate-')) return 'Fases y Gates';
  if (lower.includes('regional') || lower.includes('failover') || lower.includes('failback') || lower.includes('chaos')) return 'Alta Disponibilidad y Resiliencia';
  if (lower.includes('benchmark') || lower.includes('rendimiento') || lower.includes('capacity') || lower.includes('slo-sla')) return 'Rendimiento y Capacidad';
  if (lower.includes('auditoria') || lower.includes('threat-model') || lower.includes('risk-register')) return 'Seguridad y Cumplimiento';
  if (lower.includes('principles') || lower.includes('convenciones') || lower.includes('inventario') || lower.includes('mapa-capacidades')) return 'Arquitectura Base';
  return 'General';
}

function collectDocs() {
  const docs = [];
  const entries = fs.readdirSync(docsDir, { withFileTypes: true });

  for (const entry of entries) {
    if (entry.isFile() && entry.name.endsWith('.md')) {
      const filePath = path.join(docsDir, entry.name);
      const content = fs.readFileSync(filePath, 'utf-8');
      const title = extractTitle(content, entry.name.replace('.md', ''));
      const category = classifyDoc(entry.name, title);

      docs.push({
        id: `doc-${entry.name.replace(/\.md$/, '')}`,
        type: 'doc',
        fileName: entry.name,
        title,
        category,
        content
      });
    }
  }

  // Sort by category then title
  docs.sort((a, b) => a.category.localeCompare(b.category) || a.title.localeCompare(b.title));
  return docs;
}

function collectAdrs() {
  const adrs = [];
  if (!fs.existsSync(adrDir)) return adrs;

  const entries = fs.readdirSync(adrDir, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isFile() && entry.name.endsWith('.md')) {
      const filePath = path.join(adrDir, entry.name);
      const content = fs.readFileSync(filePath, 'utf-8');
      const title = extractTitle(content, entry.name.replace('.md', ''));
      const metadata = extractAdrMetadata(content);
      const numberMatch = entry.name.match(/^(\d{4})/);
      const number = numberMatch ? numberMatch[1] : '';

      adrs.push({
        id: `adr-${entry.name.replace(/\.md$/, '')}`,
        type: 'adr',
        number,
        fileName: entry.name,
        title,
        status: metadata.status,
        date: metadata.date,
        responsible: metadata.responsible,
        content
      });
    }
  }

  adrs.sort((a, b) => a.number.localeCompare(b.number));
  return adrs;
}

function collectOpenApiSpecs() {
  const apis = [];
  if (!fs.existsSync(openapiDir)) return apis;

  const apiDirs = fs.readdirSync(openapiDir, { withFileTypes: true });
  for (const apiDir of apiDirs) {
    if (apiDir.isDirectory()) {
      const currentDir = path.join(openapiDir, apiDir.name);
      const files = fs.readdirSync(currentDir);
      for (const file of files) {
        if (file.endsWith('.json')) {
          const filePath = path.join(currentDir, file);
          try {
            const rawContent = fs.readFileSync(filePath, 'utf-8');
            const spec = JSON.parse(rawContent);

            const paths = [];
            if (spec.paths) {
              for (const [route, methods] of Object.entries(spec.paths)) {
                for (const [method, details] of Object.entries(methods)) {
                  if (typeof details === 'object' && details !== null) {
                    paths.push({
                      route,
                      method: method.toUpperCase(),
                      summary: details.summary || details.operationId || '',
                      tags: details.tags || [],
                      parameters: details.parameters || [],
                      requestBody: details.requestBody || null,
                      responses: details.responses || {}
                    });
                  }
                }
              }
            }

            apis.push({
              id: `api-${apiDir.name}-${file.replace('.json', '')}`,
              type: 'openapi',
              serviceName: apiDir.name,
              version: file.replace('.json', ''),
              title: spec.info?.title || apiDir.name,
              specVersion: spec.info?.version || '1.0.0',
              description: spec.info?.description || '',
              servers: spec.servers || [],
              endpointCount: paths.length,
              paths,
              rawSpec: spec
            });
          } catch (err) {
            console.warn(`Error parsing OpenAPI ${filePath}:`, err.message);
          }
        }
      }
    }
  }

  return apis;
}

function collectScaffoldingExamples() {
  return [
    {
      id: 'scaffold-module',
      type: 'example',
      category: 'Backend Modularity',
      title: 'Creación de Módulo Backend Limpio (Vertical Slice / Clean)',
      description: 'Estructura estándar de un bounded context en BitCode.Framework respetando contratos CQRS, Entity<TId> y aislamiento de DbContext.',
      code: `// 1. Entidad de Dominio
public class CatalogoEntity : Entity<Guid>, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;

    private CatalogoEntity() { }
    public CatalogoEntity(Guid id, Guid tenantId, string nombre) : base(id)
    {
        TenantId = tenantId;
        Nombre = Guard.AgainstNullOrWhiteSpace(nombre);
    }
}

// 2. Comando y Handler CQRS
public record CrearCatalogoCommand(string Nombre) : IRequest<Result<Guid>>;

public class CrearCatalogoCommandHandler : IRequestHandler<CrearCatalogoCommand, Result<Guid>>
{
    private readonly IRepository<CatalogoEntity, Guid> _repository;
    private readonly IUnitOfWork _unitOfWork;

    public CrearCatalogoCommandHandler(IRepository<CatalogoEntity, Guid> repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CrearCatalogoCommand request, CancellationToken ct)
    {
        var entity = new CatalogoEntity(Guid.NewGuid(), Guid.NewGuid(), request.Nombre);
        await _repository.AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Guid>.Success(entity.Id);
    }
}`
    },
    {
      id: 'scaffold-frontend-view',
      type: 'example',
      category: 'Frontend Angular',
      title: 'Componente Standalone con Signals e Inyección Tipada',
      description: 'Componente frontend reactivo utilizando Angular 19+ Signals y cliente tipado generado por OpenAPI.',
      code: `import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SampleApiClient } from '@bitcode/contracts-sample-api';

@Component({
  selector: 'bitcode-catalogo-view',
  standalone: true,
  imports: [CommonModule],
  template: \`
    <div class="catalogo-container">
      <h2>Catálogos Activos</h2>
      @if (loading()) {
        <p class="loading">Cargando datos...</p>
      } @else {
        <ul>
          @for (item of items(); track item.id) {
            <li>{{ item.nombre }}</li>
          }
        </ul>
      }
    </div>
  \`
})
export class CatalogoViewComponent implements OnInit {
  private api = inject(SampleApiClient);
  items = signal<any[]>([]);
  loading = signal(true);

  async ngOnInit() {
    try {
      const data = await this.api.obtenerCatalogos();
      this.items.set(data);
    } finally {
      this.loading.set(false);
    }
  }
}`
    },
    {
      id: 'scaffold-architecture-test',
      type: 'example',
      category: 'Testing de Gobernanza',
      title: 'Regla de Arquitectura con NetArchTest',
      description: 'Patrón para asegurar que las capas de Dominio y Aplicación no se contaminen con dependencias de infraestructura.',
      code: `[Fact]
public void Domain_Must_Not_Reference_Infrastructure()
{
    var domainAssembly = typeof(DomainEntity).Assembly;
    var result = Types.InAssembly(domainAssembly)
        .ShouldNot()
        .HaveDependencyOnAny("BitCode.Framework.Shared.Infrastructure.Persistence", "Microsoft.EntityFrameworkCore")
        .GetResult();

    result.IsSuccessful.Should().BeTrue("El dominio debe permanecer puro e independiente de la persistencia.");
}`
    }
  ];
}

console.log('Construyendo datos del portal técnico...');
const docs = collectDocs();
const adrs = collectAdrs();
const openapis = collectOpenApiSpecs();
const examples = collectScaffoldingExamples();

const portalData = {
  buildDate: new Date().toISOString(),
  counts: {
    docs: docs.length,
    adrs: adrs.length,
    apis: openapis.length,
    examples: examples.length
  },
  docs,
  adrs,
  openapis,
  examples
};

fs.writeFileSync(portalDataFile, JSON.stringify(portalData, null, 2), 'utf-8');
console.log(`Portal data compilado exitosamente en: ${portalDataFile}`);
console.log(`- Documentos técnicos: ${docs.length}`);
console.log(`- ADRs procesados: ${adrs.length}`);
console.log(`- Especificaciones OpenAPI: ${openapis.length}`);
console.log(`- Ejemplos interactivos: ${examples.length}`);
