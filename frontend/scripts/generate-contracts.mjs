#!/usr/bin/env node
// Contratos TypeScript generados desde OpenAPI (Gate de Fase 7, ver docs/guia-contratos-frontend.md):
// convierte cada docs/openapi/<api>/v{N}.json (ya exportado por `npm run contracts:export`, requiere
// Docker) al `.ts` de tipos correspondiente bajo frontend/packages/<pkg>/src/lib/contracts/generated/,
// vía `openapi-typescript`. No agrega ningún cliente HTTP ni runtime -- sólo tipos.
//
// Uso: `npm run contracts:generate` (desde `frontend/`), o `npm run contracts:refresh` para correr el
// paso de exportación (samples/OpenApiExport) primero.

import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendRoot = path.resolve(fileURLToPath(new URL('.', import.meta.url)), '..');
const repoRoot = path.resolve(frontendRoot, '..');

// api: carpeta bajo docs/openapi/<api>/. document: nombre del documento (v1, v2, ...).
// output: ruta del .ts generado, relativa a frontend/.
const targets = [
  {
    api: 'sample-api',
    document: 'v1',
    output: 'packages/core/src/lib/contracts/generated/sample-api.v1.ts',
  },
  {
    api: 'sample-documents-api',
    document: 'v1',
    output: 'packages/documents/src/lib/contracts/generated/sample-documents-api.v1.ts',
  },
  {
    api: 'sample-workflow-api',
    document: 'v1',
    output: 'packages/workflow/src/lib/contracts/generated/sample-workflow-api.v1.ts',
  },
];

let exitCode = 0;

for (const target of targets) {
  const inputFile = path.join(repoRoot, 'docs', 'openapi', target.api, `${target.document}.json`);
  if (!existsSync(inputFile)) {
    console.error(
      `ERROR: no existe ${path.relative(frontendRoot, inputFile)}. Corré primero ` +
        `"npm run contracts:export" (requiere Docker) -- ver docs/guia-contratos-frontend.md.`,
    );
    exitCode = 1;
    continue;
  }

  const outputFile = path.join(frontendRoot, target.output);
  console.log(`==> ${path.relative(frontendRoot, inputFile)} -> ${target.output}`);
  execFileSync(
    'npx',
    ['openapi-typescript', path.relative(frontendRoot, inputFile), '-o', target.output],
    { cwd: frontendRoot, stdio: 'inherit', shell: process.platform === 'win32' },
  );
}

process.exit(exitCode);
