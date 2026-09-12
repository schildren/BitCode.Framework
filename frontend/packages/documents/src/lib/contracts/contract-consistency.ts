import type { components } from './generated/sample-documents-api.v1';
import type { DocumentoResponse, DocumentoVersionResponse } from '../models/documento.model';

/**
 * Verificación en tiempo de COMPILACIÓN (no en runtime, no un test que se pueda "saltar") de que los
 * modelos escritos a mano en `models/documento.model.ts` siguen teniendo, campo por campo, la misma
 * forma que el contrato OpenAPI real generado por `Sample.Documents.Api` (ver
 * `docs/guia-contratos-frontend.md`). Si el backend agrega/quita/renombra un campo de
 * `DocumentoResponse`/`DocumentoVersionResponse` y nadie actualiza el modelo de mano, `npx nx build
 * documents`/`lint` falla acá -- no hace falta levantar el backend para detectarlo, sólo regenerar
 * `contracts/generated/` (`dotnet run --project samples/OpenApiExport` + `npx openapi-typescript`, ver
 * la guía) y correr el build.
 *
 * Por qué NO se re-exportan directamente los tipos generados en vez de mantener el modelo de mano: el
 * generador de OpenAPI nativo de .NET 10 (`Microsoft.AspNetCore.OpenApi`) describe los enteros
 * (`int32`/`int64`) como `type: ["integer", "string"]` en el schema (ver
 * `docs/openapi/sample-documents-api/v1.json`) -- una particularidad real, verificada, del generador,
 * no un error de este paquete -- que produce `number | string` en el tipo generado. Adoptar eso 1:1
 * degradaría la superficie pública de `@bitcode/documents` (todo consumidor tendría que angostar el tipo
 * en cada uso) sin ganar seguridad real, porque el backend siempre serializa esos campos como número
 * JSON. Por eso el modelo de mano sigue siendo la fuente de verdad pública, y esta verificación de
 * claves es lo que reemplaza al "copiar el contrato a mano sin validar" que el Gate de Fase 7 señaló
 * como ausente.
 */
type Expect<T extends true> = T;

type KeysMatch<Hand, Generated> = [keyof Hand] extends [keyof Generated]
  ? [keyof Generated] extends [keyof Hand]
    ? true
    : false
  : false;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _DocumentoResponseTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<DocumentoResponse, components['schemas']['DocumentoResponse']>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _DocumentoVersionResponseTieneLasMismasClavesQueElContratoGenerado = Expect<
  KeysMatch<DocumentoVersionResponse, components['schemas']['DocumentoVersionResponse']>
>;
