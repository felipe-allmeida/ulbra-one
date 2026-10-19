import type { QueryClient, QueryKey } from '@tanstack/react-query';

/**
 * `Query.fetch` (tanstack `query-core`@5.101.0) só cancela-e-refaz um fetch em andamento quando
 * `state.data !== undefined`; pra uma query no primeiro fetch da vida dela (sem dado ainda), ele
 * devolve a promise já em voo e não inicia nada. Um sinal chegando durante esse primeiro fetch é
 * engolido: a resposta em voo carrega o snapshot de ANTES da mudança, e a própria action de
 * sucesso zera `isInvalidated` — com `staleTime` alto e sem refetch por foco/timer (a configuração
 * de toda query aqui), nada dispara de novo até outro sinal chegar por acaso. Isto vale pro sino
 * de notificações (`staleTime: 30_000`) tanto quanto pra presença.
 *
 * Cancelar antes de invalidar tira o fetch em voo do caminho: ele termina em `cancelled`, não
 * `success`, então não zera `isInvalidated`, e a invalidação que vem a seguir sempre dispara um
 * fetch de verdade. `refetchType: 'all'`/`cancelRefetch`/`refetchOnMount: 'always'` foram testados
 * e não resolvem isto — o problema é o dedupe em `Query.fetch`, não o tipo de refetch pedido.
 */
export function invalidateAfterCancel(qc: QueryClient, queryKey: QueryKey): void {
  void qc.cancelQueries({ queryKey }).then(() => qc.invalidateQueries({ queryKey }));
}
