import { type ReceivablesParams } from '../../queries';
import { toDateOnly } from './format';

export type TabKey = 'all' | 'overdue' | 'thisMonth' | 'future' | 'received';

/** Maps a tab to ListReceivables filters. Computed at call time so "today" stays correct past midnight. */
export function tabToParams(tab: TabKey): Pick<ReceivablesParams, 'received' | 'dueFrom' | 'dueTo'> {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (tab) {
    case 'overdue': // não recebidas, vencimento já passou (até ontem)
      return {
        received: false,
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1)),
      };
    case 'thisMonth': // não recebidas, a vencer ainda neste mês (hoje → último dia do mês)
      return {
        received: false,
        dueFrom: toDateOnly(today),
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
      };
    case 'future': // não recebidas, vencem em meses futuros (a partir do 1º dia do próximo mês)
      return { received: false, dueFrom: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 1)) };
    case 'received': // parcelas recebidas (qualquer data)
      return { received: true };
    case 'all':
    default:
      return {};
  }
}

export const TABS: TabKey[] = ['all', 'overdue', 'thisMonth', 'future', 'received'];
