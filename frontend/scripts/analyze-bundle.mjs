#!/usr/bin/env node
// F7-14 ("Lazy loading, budgets y análisis de bundle"): reporte de tamaño real (raw + gzip) de cada
// archivo JS/CSS del build de producción de `apps/shell`, agrupado en "initial" (bundle inicial, lo que
// descarga cualquier usuario al entrar) vs. "lazy" (chunks de rutas con `loadComponent`, sólo se descargan
// si el usuario navega ahí). Sin agregar una dependencia nueva (`webpack-bundle-analyzer`/
// `source-map-explorer`): `zlib` (nativo de Node) alcanza para el nivel de detalle que pide esta tarea --
// ver el pendiente explícito de un análisis con mapa de dependencias en `docs/guia-frontend-performance.md`.
//
// Uso: `npm run analyze:shell` (desde `frontend/`) -- construye `shell` en producción y después analiza
// `dist/apps/shell/browser`.

import { execSync } from 'node:child_process';
import { gzipSync } from 'node:zlib';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const DIST_DIR = join(import.meta.dirname, '..', 'dist', 'apps', 'shell', 'browser');

function build() {
  execSync('npx nx run shell:build:production', { stdio: 'inherit', cwd: join(import.meta.dirname, '..') });
}

function formatKb(bytes) {
  return `${(bytes / 1024).toFixed(2)} kB`;
}

function analyze() {
  const files = readdirSync(DIST_DIR).filter((name) => name.endsWith('.js') || name.endsWith('.css'));

  const rows = files.map((name) => {
    const filePath = join(DIST_DIR, name);
    const raw = statSync(filePath).size;
    const gzip = gzipSync(readFileSync(filePath)).length;
    // Angular nombra los chunks "initial" con un nombre reconocible (main-*, chunk-* referenciado desde
    // index.html) -- para distinguir "initial" de "lazy" de forma simple y verificable sin parsear el
    // grafo de módulos, se usa la convención de que index.html sólo referencia los chunks iniciales.
    return { name, raw, gzip };
  });

  const indexHtml = readFileSync(join(DIST_DIR, 'index.html'), 'utf-8');
  const initial = rows.filter((row) => indexHtml.includes(row.name));
  const lazy = rows.filter((row) => !indexHtml.includes(row.name));

  const totalInitialRaw = initial.reduce((sum, row) => sum + row.raw, 0);
  const totalInitialGzip = initial.reduce((sum, row) => sum + row.gzip, 0);

  console.log('\n=== Bundle inicial (descargado siempre) ===');
  for (const row of initial.sort((a, b) => b.raw - a.raw)) {
    console.log(`  ${row.name.padEnd(28)} raw=${formatKb(row.raw).padStart(10)}  gzip=${formatKb(row.gzip).padStart(10)}`);
  }
  console.log(`  TOTAL initial: raw=${formatKb(totalInitialRaw)}  gzip=${formatKb(totalInitialGzip)}`);

  console.log('\n=== Chunks lazy (sólo si el usuario navega ahí) ===');
  for (const row of lazy.sort((a, b) => b.raw - a.raw)) {
    console.log(`  ${row.name.padEnd(28)} raw=${formatKb(row.raw).padStart(10)}  gzip=${formatKb(row.gzip).padStart(10)}`);
  }
  console.log('');
}

if (process.argv.includes('--skip-build')) {
  analyze();
} else {
  build();
  analyze();
}
