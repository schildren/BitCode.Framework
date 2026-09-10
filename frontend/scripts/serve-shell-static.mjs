#!/usr/bin/env node
// Servidor estático mínimo (sin dependencia nueva) para `apps/shell` en modo E2E (F7-15): sirve
// `dist/apps/shell/browser` con fallback SPA a `index.html` para cualquier ruta que no matchee un archivo
// -- necesario porque `app.routes.ts` usa el `Router` de Angular del lado del cliente (F7-14), así que
// `/procesos/documentos` no existe como archivo real en disco.
//
// Uso: `node scripts/serve-shell-static.mjs [--port=4300] [--skip-build]`

import { execSync } from 'node:child_process';
import { createServer } from 'node:http';
import { createReadStream, existsSync, statSync } from 'node:fs';
import { extname, join } from 'node:path';

const args = process.argv.slice(2);
const port = Number(args.find((arg) => arg.startsWith('--port='))?.split('=')[1] ?? 4300);
const skipBuild = args.includes('--skip-build');

const DIST_DIR = join(import.meta.dirname, '..', 'dist', 'apps', 'shell', 'browser');

const MIME_TYPES = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
};

if (!skipBuild) {
  execSync('npx nx run shell:build:production', { stdio: 'inherit', cwd: join(import.meta.dirname, '..') });
}

createServer((req, res) => {
  const requestedPath = decodeURIComponent((req.url ?? '/').split('?')[0]);
  let filePath = join(DIST_DIR, requestedPath);

  if (!existsSync(filePath) || statSync(filePath).isDirectory()) {
    filePath = join(DIST_DIR, 'index.html');
  }

  const contentType = MIME_TYPES[extname(filePath)] ?? 'application/octet-stream';
  res.writeHead(200, { 'Content-Type': contentType });
  createReadStream(filePath).pipe(res);
}).listen(port, () => {
  console.log(`[bitcode:e2e] sirviendo apps/shell en http://127.0.0.1:${port}`);
});
