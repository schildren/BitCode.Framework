import { Observable } from 'rxjs';

/**
 * Contrato que implementa el consumidor para enviar el valor de un formulario -- análogo a
 * `BitcodeGridDataSource<T>` (F7-07): `<lib-bitcode-dynamic-form>` NUNCA sabe a qué endpoint/acción de
 * negocio corresponde el envío, sólo invoca `submit(value)` con el `FormGroup.value` ya validado
 * (client-side) y reacciona al resultado/error del `Observable` devuelto.
 */
export interface BitcodeFormSubmitHandler<T> {
  submit(value: T): Observable<unknown>;
}
