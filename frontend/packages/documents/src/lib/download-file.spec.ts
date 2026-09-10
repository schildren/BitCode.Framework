// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { downloadFile } from './download-file';

describe('downloadFile', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('crea un object URL, simula el click de un <a> con el nombre de archivo real y lo libera', () => {
    const createObjectURL = vi.fn(() => 'blob:mock-url');
    const revokeObjectURL = vi.fn();
    URL.createObjectURL = createObjectURL;
    URL.revokeObjectURL = revokeObjectURL;

    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    const blob = new Blob(['contenido'], { type: 'application/pdf' });
    downloadFile(blob, 'contrato.pdf');

    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
    expect(document.querySelectorAll('a[download]')).toHaveLength(0); // el <a> se removió del DOM
  });
});
