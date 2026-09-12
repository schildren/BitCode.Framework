import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BITCODE_TELEMETRY_SINK, BitcodeTelemetryEvent } from './telemetry.model';
import { provideBitcodeTelemetry } from './provide-telemetry';

class RecordingSink {
  readonly events: BitcodeTelemetryEvent[] = [];
  send(event: BitcodeTelemetryEvent): void {
    this.events.push(event);
  }
}

describe('provideBitcodeTelemetry (F7-13)', () => {
  it('registra los listeners de errores no controlados sin lanzar durante el bootstrap', () => {
    const sink = new RecordingSink();
    TestBed.configureTestingModule({
      providers: [{ provide: BITCODE_TELEMETRY_SINK, useValue: sink }, provideBitcodeTelemetry()],
    });

    // Forzar la resolución de providers (ENVIRONMENT_INITIALIZER corre al crear el injector raíz del
    // TestBed) -- no debe lanzar ni requerir un componente real.
    expect(() => TestBed.inject(BITCODE_TELEMETRY_SINK)).not.toThrow();

    window.dispatchEvent(new ErrorEvent('error', { message: 'boom' }));

    expect(sink.events.some((event) => event.kind === 'error' && event.context['message'] === 'boom')).toBe(true);
  });
});
