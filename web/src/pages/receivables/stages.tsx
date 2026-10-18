import { type ReactNode } from 'react';
import { Inbox, HandCoins, Archive } from 'lucide-react';
import { type ReceivableStatus, type ReceivableStageSummaryResponse } from '../../api';
import { type PendencyCard } from '../../components/workflow';

export type TabKey = 'areceber' | 'reconciled';
export const TAB_ORDER: TabKey[] = ['areceber', 'reconciled'];
export const TAB_STATUS: Record<TabKey, ReceivableStatus> = {
  areceber: 'AwaitingReceipt',
  reconciled: 'Received',
};

export type DashboardKey = 'entry' | 'receipt' | 'reconciled';
const DASHBOARD_ORDER: DashboardKey[] = ['entry', 'receipt', 'reconciled'];

function cardIcon(key: DashboardKey): ReactNode {
  switch (key) {
    case 'entry':
      return <Inbox size={18} />;
    case 'receipt':
      return <HandCoins size={18} />;
    case 'reconciled':
      return <Archive size={18} />;
  }
}

export function dashboardCards(summary: ReceivableStageSummaryResponse, t: (k: string) => string): PendencyCard[] {
  return DASHBOARD_ORDER.map((key) => {
    const bucket = summary[key];
    return {
      stageKey: key,
      label: t(`receivables.stages.${key}`),
      icon: cardIcon(key),
      count: bucket.count,
      totalAmount: bucket.totalAmount,
    };
  });
}

export function cardTargetTab(cardKey: string): TabKey {
  return cardKey === 'reconciled' ? 'reconciled' : 'areceber';
}

export function isTabKey(v: string | null): v is TabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}
