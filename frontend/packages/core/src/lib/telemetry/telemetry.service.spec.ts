import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import {
  BITCODE_TELEMETRY_SINK,
  BitcodeTelemetryEvent,
  provideBitcodeTelemetryContext,
} from './telemetry.model';
import { BitcodeTelemetryService } from './telemetry.service';

class RecordingSink {
  readonly events: BitcodeTelemetryEvent[] = [];
  send(event: BitcodeTelemetryEvent): void {
    this.events.push(event);
  }
}

function setup(context: Record<string, unknown> = {}) {
  const sink = new RecordingSink();
  TestBed.configureTestingModule({
    providers: [{ provide: BITCODE_TELEMETRY_SINK, useValue: sink }, provideBitcodeTelemetryContext(context)],
  });
  return { service: TestBed.inject(BitcodeTelemetryService), sink };
}

describe('BitcodeTelemetryService (F7-13)', () => {
  it('recordError emite un evento kind=error reutilizando el kind del BitcodeUiError', () => {
    const { service, sink } = setup();

    service.recordError({ kind: 'not-found', userMessage: 'No encontrado', httpStatus: 404 });

    expect(sink.events).toHaveLength(1);
    expect(sink.events[0]).toMatchObject({ kind: 'error', name: 'not-found', context: { httpStatus: 404 } });
    expect(typeof sink.events[0].timestamp).toBe('number');
  });

  it('recordUncaughtError emite un evento sin correlationId', () => {
    const { service, sink } = setup();

    service.recordUncaughtError('boom');

    expect(sink.events[0]).toMatchObject({ kind: 'error', name: 'uncaught', context: { message: 'boom' } });
  });

  it('recordWebVital incluye el value numérico', () => {
    const { service, sink } = setup();

    service.recordWebVital('LCP', 1234.5);

    expect(sink.events[0]).toMatchObject({ kind: 'web-vital', name: 'LCP', value: 1234.5 });
  });

  it('recordTrace incluye correlationId y duración', () => {
    const { service, sink } = setup();

    service.recordTrace('/api/productos', '00-abc-def-01', 42, { status: 200 });

    expect(sink.events[0]).toMatchObject({
      kind: 'trace',
      name: '/api/productos',
      correlationId: '00-abc-def-01',
      value: 42,
      context: { status: 200 },
    });
  });

  it('mergea el contexto estático (BITCODE_TELEMETRY_CONTEXT) en todos los eventos', () => {
    const { service, sink } = setup({ app: 'shell', version: '1.0.0' });

    service.recordWebVital('CLS', 0.05);

    expect(sink.events[0].context).toMatchObject({ app: 'shell', version: '1.0.0' });
  });
});
