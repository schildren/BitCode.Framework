#!/usr/bin/env node
// Verificación de punta a punta del pipeline OpenAPI -> TypeScript (F8-04, ver
// docs/guia-contratos-frontend.md): prueba que el cliente TypeScript generado en
// frontend/packages/*/src/lib/contracts/generated/ es 100% reproducible desde
// docs/openapi/<api>/v{N}.json y nunca requiere edición manual.
//
// Corre las tres etapas reales del pipeline (no un mock):
//   1. `dotnet run --project samples/OpenApiExport` (requiere Docker: levanta cada
//      Sample.*.Api real contra Testcontainers.MsSql y vuelca su OpenAPI real).
//   2. `openapi-typescript` sobre cada documento (scripts/generate-contracts.mjs).
//   3. `git diff --exit-code` sobre TODO lo regenerado (docs/openapi/**/*.json y
//      contracts/generated/**/*.ts): si el working tree cambia, alguien editó a mano
//      el output generado o el backend cambió su contrato sin regenerar -- ambos casos
//      deben romper la verificación.
//   4. `tsc --noEmit --strict --skipLibCheck` sobre cada archivo `.ts` generado
//      (aislado del resto del paquete a propósito: valida que el output de
//      openapi-typescript compila por sí mismo, sin acoplarse a bugs preexistentes no
//      relacionados en otros archivos del paquete).
//
// Uso: `npm run contracts:verify` (desde frontend/, requiere Docker corriendo).
// Pensado para CI: falla (exit 1) ante cualquier archivo generado desactualizado o que
// no compile.

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const frontendRoot = path.resolve(fileURLToPath(new URL('.', import.meta.url)), '..');
const repoRoot = path.resolve(frontendRoot, '..');

const generatedTsFiles = [
  'packages/core/src/lib/contracts/generated/sample-api.v1.ts',
  'packages/documents/src/lib/contracts/generated/sample-documents-api.v1.ts',
  'packages/workflow/src/lib/contracts/generated/sample-workflow-api.v1.ts',
];

const openApiDiffPaths = ['docs/openapi'];
const generatedDiffPaths = generatedTsFiles.map((f) => path.posix.join('frontend', f));

function run(command, args, options) {
  console.log(`$ ${command} ${args.join(' ')}`);
  execFileSync(command, args, {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    ...options,
  });
}

console.log('== 1/4: exportando OpenAPI real de cada Sample.*.Api (requiere Docker) ==');
run('dotnet', ['run', '--project', 'samples/OpenApiExport'], { cwd: repoRoot });

console.log('\n== 2/4: generando tipos TypeScript desde los documentos exportados ==');
run('node', ['scripts/generate-contracts.mjs'], { cwd: frontendRoot });

console.log('\n== 3/4: verificando que nada quedó editado a mano (git diff --exit-code) ==');
try {
  run('git', ['diff', '--exit-code', '--stat', '--', ...openApiDiffPaths, ...generatedDiffPaths], {
    cwd: repoRoot,
  });
} catch {
  console.error(
    '\nERROR: regenerar el pipeline (dotnet run --project samples/OpenApiExport + ' +
      'npm run contracts:generate) produjo cambios respecto de lo committeado.\n' +
      'Esto significa una de dos cosas:\n' +
      '  - El contrato HTTP real de algún Sample.*.Api cambió y nadie regeneró/commiteó\n' +
      '    docs/openapi/**/*.json ni contracts/generated/**/*.ts.\n' +
      '  - Alguien editó a mano un archivo bajo contracts/generated/ (prohibido, ver\n' +
      '    docs/guia-contratos-frontend.md).\n' +
      'Corré "npm run contracts:refresh" desde frontend/ y commiteá el resultado.',
  );
  process.exitCode = 1;
}

console.log('\n== 4/4: compilando cada .ts generado de forma aislada (tsc --strict) ==');
for (const relativeFile of generatedTsFiles) {
  run('npx', ['tsc', '--noEmit', '--strict', '--skipLibCheck', relativeFile], {
    cwd: frontendRoot,
  });
}

if (process.exitCode === 1) {
  console.error('\nVerificación de contratos TypeScript: FALLÓ.');
  process.exit(1);
}

console.log('\nVerificación de contratos TypeScript: OK (reproducible, sin edición manual, compila).');
