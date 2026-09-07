// k6-scale-get.js
//
// F4-14 (Fase 4 -- Capacity tests): variante de docs/perf/k6-smoke.js usada SOLO para el escenario
// "escalamiento horizontal" (cualitativo, sin HPA/clúster real -- ver
// docs/informe-capacity-tests-f4-14.md). Ejercita ÚNICAMENTE GET /api/v1/productos/{id} (lectura,
// sin Idempotency-Key) con más VUs que docs/perf/k6-smoke.js, para poder comparar el throughput de
// UNA instancia de samples/Sample.Api contra el throughput agregado de VARIAS instancias reales
// corriendo en paralelo contra el mismo SQL Server (mismo patrón de "pod reemplazable" que ya
// demostró docs/auditoria-estado-runtime-f4-03.md, sección 2, con dos procesos reales).
//
// Uso: k6 run -e BASE_URL=http://127.0.0.1:PUERTO -e VUS=20 -e DURATION=20s docs/perf/k6-scale-get.js

import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5269';
const VUS = parseInt(__ENV.VUS || '20', 10);
const DURATION = __ENV.DURATION || '20s';

export const options = {
  scenarios: {
    lectura_get_producto: {
      executor: 'constant-vus',
      exec: 'getProducto',
      vus: VUS,
      duration: DURATION,
    },
  },
};

export function setup() {
  const res = http.post(
    `${BASE_URL}/api/v1/productos`,
    JSON.stringify({ nombre: 'k6-scale-seed', precio: 10 }),
    {
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': `k6-scale-setup-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      },
    },
  );

  if (res.status !== 201) {
    throw new Error(`setup() no pudo crear el producto semilla: HTTP ${res.status} -- ${res.body}`);
  }

  return { productoId: JSON.parse(res.body) };
}

export function getProducto(data) {
  const res = http.get(`${BASE_URL}/api/v1/productos/${data.productoId}`);
  check(res, { 'GET /productos/{id} -> 200': (r) => r.status === 200 });
  sleep(0.05);
}
