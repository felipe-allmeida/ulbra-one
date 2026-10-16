import { type ReactNode } from 'react';
import { Inbox, Tags, CheckCircle2, Calculator, Landmark, Wallet, Archive } from 'lucide-react';
import { type PayableStatus, type PayableStageSummaryResponse } from '../../api';
import { type PendencyCard } from '../../components/workflow';

export type StageKey =
  | 'entry'
  | 'classification'
  | 'approval'
  | 'accounting'
  | 'taxAnalysis'
  | 'payment'
  | 'reconciled';

export const STAGE_ORDER: StageKey[] = [
  'entry',
  'classification',
  'approval',
  'accounting',
  'taxAnalysis',
  'payment',
  'reconciled',
];

export const STAGE_STATUS: Record<StageKey, PayableStatus> = {
  entry: 'Received',
  classification: 'PendingClassification',
  approval: 'PendingApproval',
  accounting: 'PendingAccounting',
  taxAnalysis: 'PendingTaxAnalysis',
  payment: 'Scheduled',
  reconciled: 'Paid',
};

export function stageIcon(key: StageKey): ReactNode {
  switch (key) {
    case 'entry':
      return <Inbox size={18} />;
    case 'classification':
      return <Tags size={18} />;
    case 'approval':
      return <CheckCircle2 size={18} />;
    case 'accounting':
      return <Calculator size={18} />;
    case 'taxAnalysis':
      return <Landmark size={18} />;
    case 'payment':
      return <Wallet size={18} />;
    case 'reconciled':
      return <Archive size={18} />;
  }
}

export function dashboardCards(summary: PayableStageSummaryResponse, t: (k: string) => string): PendencyCard[] {
  return STAGE_ORDER.map((key) => {
    const bucket = summary[key];
    return {
      stageKey: key,
      label: t(`payables.stages.${key}`),
      icon: stageIcon(key),
      count: bucket.count,
      // Payment shows what will actually leave the bank (net of withholdings); other stages stay gross.
      totalAmount: key === 'payment' ? (bucket.netTotalAmount ?? bucket.totalAmount) : bucket.totalAmount,
    };
  });
}

export function isStageKey(v: string | null): v is StageKey {
  return v != null && (STAGE_ORDER as string[]).includes(v);
}
