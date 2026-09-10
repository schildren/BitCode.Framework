// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58240/"}
import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { ProblemDetailsTestServer } from '../testing/problem-details-test-server';
import { BITCODE_TELEMETRY_SINK, BitcodeTelemetryEvent } from './telemetry.model';
import { bitcodeTelemetryInterceptor } from './telemetry.interceptor';

const PORT = 58240;
const BASE_URL = `http://127.0.0.1:${PORT}`;

class RecordingSink {
  readonly events: BitcodeTelemetryEvent[] = [];
  send(event: BitcodeTelemetryEvent): void {
    this.events.push(event);
  }
}

describe('bitcodeTelemetryInterceptor (contra un servidor HTTP real, F7-13)', () => {
  let server: ProblemDetailsTestServer;

  beforeAll(async () => {
    server = new ProblemDetailsTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup() {
    const sink = new RecordingSink();
    TestBed.configureTestingModule({
      providers: [
        { provide: BITCODE_TELEMETRY_SINK, useValue: sink },
        provideHttpClient(withXhr(), withInterceptors([bitcodeTelemetryInterceptor])),
      ],
    });
    return { http: TestBed.inject(HttpClient), sink };
  }

  it('agrega un traceparent al request y registra un evento de trace en un 200 exitoso', async () => {
    const { http, sink } = setup();

    await firstValueFrom(http.get<{ ok: boolean }>(`${BASE_URL}/ok`));

    const traceEvents = sink.events.filter((event) => event.kind === 'trace');
    expect(traceEvents).toHaveLength(1);
    expect(traceEvents[0].correlationId).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/);
    expect(traceEvents[0].context).toMatchObject({ method: 'GET', status: 200 });
    expect(typeof traceEvents[0].value).toBe('number');
  });

  it('no sobreescribe un traceparent ya presente en el request', async () => {
    const { http, sink } = setup();
    const existing = '00-11111111111111111111111111111111-2222222222222222-01';

    await firstValueFrom(http.get(`${BASE_URL}/ok`, { headers: { traceparent: existing } }));

    const traceEvents = sink.events.filter((event) => event.kind === 'trace');
    expect(traceEvents[0].correlationId).toBe(existing);
  });

  it('en un error HTTP, registra trace + error y relanza el error original sin transformarlo', async () => {
    const { http, sink } = setup();

    await expect(firstValueFrom(http.get(`${BASE_URL}/not-found`))).rejects.toSatisfy((error: unknown) => {
      // No se envolvió en BitcodeHttpError: este interceptor no compite con bitcodeErrorInterceptor.
      expect((error as { status?: number }).status).toBe(404);
      return true;
    });

    expect(sink.events.some((event) => event.kind === 'trace' && event.context['status'] === 404)).toBe(true);
    const errorEvent = sink.events.find((event) => event.kind === 'error');
    expect(errorEvent).toBeDefined();
    expect(errorEvent?.name).toBe('not-found');
  });
});
