// k6-smoke.js
//
// Carga de referencia mínima para samples/Sample.Api (BitCode.Framework), usada por F0-10
// (Línea base de rendimiento) para reemplazar la aproximación anterior basada en `curl` en bucle
// (que no era representativa por el overhead de fork+exec de un proceso de curl por request).
//
// Ejercita los mismos dos endpoints usados en la aproximación por curl, para mantener
// comparabilidad narrativa (no numérica: la metodología cambió por completo):
//   - GET  /productos/{id}   (lectura vía ObtenerProductoQuery)
//   - POST /productos        (escritura vía CrearProductoCommand, con TransactionBehavior,
//                              FluentValidation e IAuditedEntity)
//
// Carga modesta y realista para una laptop de desarrollo (no un entorno de referencia dedicado,
// ver docs/entorno-referencia.md): pocos VUs, duración corta. No es un test de estrés ni busca el
// punto de saturación del servidor — busca una medición real y reproducible de RPS/latencia bajo
// una carga liviana, con la herramienta de referencia (k6) en vez de una aproximación con curl.
//
// Uso:
//   k6 run -e BASE_URL=http://localhost:5269 docs/perf/k6-smoke.js
//
// Requiere que samples/Sample.Api esté corriendo y accesible en BASE_URL, con
// ConnectionStrings__Default apuntando a una base disponible (LocalDB o SQL Server en contenedor).

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5269';

export const options = {
  scenarios: {
    // Réplica aproximada de la sección 4.2 del baseline anterior (GET, concurrencia 20).
    lectura_get_producto: {
      executor: 'constant-vus',
      exec: 'getProducto',
      vus: 20,
      duration: '30s',
      startTime: '0s',
    },
    // Réplica aproximada de la sección 4.3 del baseline anterior (POST, concurrencia 10).
    escritura_post_producto: {
      executor: 'constant-vus',
      exec: 'postProducto',
      vus: 10,
      duration: '30s',
      startTime: '0s',
    },
  },
  thresholds: {
    // No aborta el run (solo se reporta), pero deja explícito el criterio esperado.
    http_req_failed: ['rate<0.01'],
  },
};

const postErrors = new Counter('post_producto_errores');
const getErrors = new Counter('get_producto_errores');
// Trends separados por endpoint: el resumen agregado de k6 (http_req_duration) mezcla GET y POST
// porque corren en paralelo; estos dos Trends dan p50/p90/p95/p99 por endpoint, comparables con
// las secciones 4.2 (GET) y 4.3 (POST) del baseline anterior basado en curl.
const getDuration = new Trend('get_producto_duration', true);
const postDuration = new Trend('post_producto_duration', true);

// setup() crea un producto real vía la propia API para que el escenario de lectura tenga un id
// válido contra el cual medir GET /productos/{id} (en vez de medir solo el costo de un 404).
export function setup() {
  const res = http.post(
    `${BASE_URL}/productos`,
    JSON.stringify({ nombre: 'k6-smoke-seed', precio: 10 }),
    { headers: { 'Content-Type': 'application/json' } },
  );

  if (res.status !== 201) {
    throw new Error(
      `setup() no pudo crear el producto semilla: HTTP ${res.status} — ${res.body}`,
    );
  }

  const id = JSON.parse(res.body);
  return { productoId: id };
}

export function getProducto(data) {
  const res = http.get(`${BASE_URL}/productos/${data.productoId}`, {
    tags: { name: 'get_producto' },
  });
  const ok = check(res, { 'GET /productos/{id} → 200': (r) => r.status === 200 });
  if (!ok) getErrors.add(1);
  getDuration.add(res.timings.duration);
  sleep(0.1);
}

export function postProducto() {
  const payload = JSON.stringify({ nombre: 'k6-smoke', precio: 19.99 });
  const res = http.post(`${BASE_URL}/productos`, payload, {
    headers: { 'Content-Type': 'application/json' },
    tags: { name: 'post_producto' },
  });
  const ok = check(res, { 'POST /productos → 201': (r) => r.status === 201 });
  if (!ok) postErrors.add(1);
  postDuration.add(res.timings.duration);
  sleep(0.1);
}
