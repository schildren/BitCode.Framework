import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BITCODE_TELEMETRY_SINK, BitcodeTelemetryEvent } from './telemetry.model';
import { BitcodeTelemetryService } from './telemetry.service';
import { observeBitcodeWebVitals } from './web-vitals';

class RecordingSink {
  readonly events: BitcodeTelemetryEvent[] = [];
  send(event: BitcodeTelemetryEvent): void {
    this.events.push(event);
  }
}

describe('observeBitcodeWebVitals (F7-13)', () => {
  it('no lanza en jsdom aunque PerformanceObserver no soporte los entryType usados (degradación silenciosa documentada)', () => {
    const sink = new RecordingSink();
    TestBed.configureTestingModule({ providers: [{ provide: BITCODE_TELEMETRY_SINK, useValue: sink }] });
    const service = TestBed.inject(BitcodeTelemetryService);

    expect(() => observeBitcodeWebVitals(service)).not.toThrow();
  });
});
