import { type PayablesParams } from '../../queries';
import { toDateOnly } from '../../datetime';

export type TabKey = 'all' | 'overdue' | 'thisMonth' | 'future' | 'paid';

/** Maps a tab to ListPayables filters. Computed at call time so "today" stays correct past midnight. */
export function tabToParams(tab: TabKey): Pick<PayablesParams, 'paid' | 'dueFrom' | 'dueTo'> {
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  switch (tab) {
    case 'overdue': // não pagas, vencimento já passou (até ontem)
      return { paid: false, dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1)) };
    case 'thisMonth': // não pagas, a vencer ainda neste mês (hoje → último dia do mês)
      return {
        paid: false,
        dueFrom: toDateOnly(today),
        dueTo: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0)),
      };
    case 'future': // não pagas, vencem em meses futuros (a partir do 1º dia do próximo mês)
      return { paid: false, dueFrom: toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 1)) };
    case 'paid': // parcelas liquidadas (qualquer data)
      return { paid: true };
    case 'all':
    default:
      return {};
  }
}

export const TABS: TabKey[] = ['all', 'overdue', 'thisMonth', 'future', 'paid'];
