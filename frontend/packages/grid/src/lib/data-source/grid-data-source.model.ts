import { Observable } from 'rxjs';
import { BitcodeGridPageRequest, BitcodeGridPageResult } from '../models/grid-request.model';

/**
 * Contrato que un consumidor de `<lib-bitcode-grid>` implementa para servir UNA página de datos concreta
 * (F7-07). `@bitcode/grid` orquesta el pedido (qué página, qué orden, qué filtro) y renderiza la
 * respuesta -- nunca pagina/ordena/filtra sobre un dataset completo cargado en el cliente.
 *
 * `Observable`, no `Promise` ni `Signal`, a propósito: es el tipo de retorno NATIVO de
 * `HttpClient.get<T>(...)` (el caso de uso más común -- un data source real casi siempre envuelve una
 * llamada HTTP), y permite cancelar un pedido en curso mediante `unsubscribe()` cuando llega un pedido más
 * nuevo (ver `BitcodeGrid`, que descarta explícitamente respuestas fuera de orden) sin reimplementar ese
 * mecanismo con `AbortController` a mano.
 */
export interface BitcodeGridDataSource<T> {
  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<T>>;
}
