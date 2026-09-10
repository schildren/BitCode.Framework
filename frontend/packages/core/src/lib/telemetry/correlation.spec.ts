import { describe, expect, it } from 'vitest';
import { generateTraceparent } from './correlation';

describe('generateTraceparent (F7-13)', () => {
  it('genera un traceparent W3C válido (version-traceid(32 hex)-spanid(16 hex)-flags)', () => {
    const traceparent = generateTraceparent();
    const parts = traceparent.split('-');

    expect(parts).toHaveLength(4);
    expect(parts[0]).toBe('00');
    expect(parts[1]).toMatch(/^[0-9a-f]{32}$/);
    expect(parts[2]).toMatch(/^[0-9a-f]{16}$/);
    expect(parts[3]).toBe('01');
  });

  it('genera valores distintos en llamadas sucesivas', () => {
    const a = generateTraceparent();
    const b = generateTraceparent();
    expect(a).not.toBe(b);
  });
});
