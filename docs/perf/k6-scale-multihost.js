// k6-scale-multihost.js
//
// F4-14 (Fase 4 -- Capacity tests): variante de k6-scale-get.js que reparte las requests de LECTURA
// entre VARIAS instancias reales de samples/Sample.Api (round-robin simple del lado del cliente),
// para aproximar -- sin un Ingress/Service de Kubernetes real disponible en este entorno -- lo que
// hace un balanceador/Service real: enrutar tráfico solo hacia instancias vivas. Usado para la
// prueba "muerte de un pod de API" de la sección "Pruebas obligatorias" de la Fase 4: mientras el
// tráfico se reparte entre N hosts, uno de ellos se mata a mitad de la corrida y se observa qué
// fracción de requests falla (la fracción enrutada a ESE host, no el total).
//
// Uso: k6 run -e HOSTS="http://127.0.0.1:15601,http://127.0.0.1:15602,http://127.0.0.1:15603" \
//             -e VUS=30 -e DURATION=40s docs/perf/k6-scale-multihost.js

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';

const HOSTS = (__ENV.HOSTS || 'http://localhost:5269').split(',');
const VUS = parseInt(__ENV.VUS || '30', 10);
const DURATION = __ENV.DURATION || '40s';

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

const failuresPorHost = HOSTS.map((h) => new Counter(`fallos_${h.replace(/[^a-z0-9]/gi, '_')}`));
const requestsPorHost = HOSTS.map((h) => new Counter(`requests_${h.replace(/[^a-z0-9]/gi, '_')}`));

export function setup() {
  const ids = HOSTS.map((host) => {
    const res = http.post(
      `${host}/api/v1/productos`,
      JSON.stringify({ nombre: 'k6-multihost-seed', precio: 10 }),
      {
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': `k6-multihost-setup-${Date.now()}-${Math.random().toString(36).slice(2)}`,
        },
      },
    );
    if (res.status !== 201) {
      throw new Error(`setup() no pudo crear el producto semilla en ${host}: HTTP ${res.status}`);
    }
    return JSON.parse(res.body);
  });
  return { ids };
}

let counter = 0;

export function getProducto(data) {
  const index = counter % HOSTS.length;
  counter++;
  const host = HOSTS[index];
  const id = data.ids[index];

  const res = http.get(`${host}/api/v1/productos/${id}`, { timeout: '3s' });
  requestsPorHost[index].add(1);
  const ok = check(res, { 'GET -> 200': (r) => r.status === 200 });
  if (!ok) {
    failuresPorHost[index].add(1);
  }
  sleep(0.05);
}
