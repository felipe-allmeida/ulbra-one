import { type RequisitionStatus } from '../../api';

/**
 * Estágios da esteira. Os "ativos" mapeiam 1:1 a um status da máquina de estados;
 * `encerradas` agrupa os estados terminais / Finance-owned (a API filtra por um único
 * status, então essa aba usa um seletor — ver `CLOSED_STATUSES`).
 */
export type StageKey =
  | 'rascunhos'
  | 'triagem'
  | 'complemento-financeiro'
  | 'aprovacao'
  | 'complemento'
  | 'pagamento'
  | 'encerradas';

export const STAGE_ORDER: StageKey[] = [
  'rascunhos',
  'triagem',
  'complemento-financeiro',
  'aprovacao',
  'complemento',
  'pagamento',
  'encerradas',
];

export const STAGE_STATUS: Record<Exclude<StageKey, 'encerradas'>, RequisitionStatus> = {
  rascunhos: 'Draft',
  triagem: 'Triage',
  'complemento-financeiro': 'FinancialComplement',
  aprovacao: 'Approval',
  complemento: 'Complement',
  pagamento: 'InPayment',
};

/** Estados terminais / Finance-owned exibidos na aba "Encerradas". `Cancelled` é o único
 * alcançável por este front hoje; os demais chegam com a integração Finance (AEL-793). */
export const CLOSED_STATUSES: RequisitionStatus[] = ['Cancelled', 'AwaitingFiscalDocument', 'Paid', 'Finalized'];

export function isStageKey(v: string | null): v is StageKey {
  return v != null && (STAGE_ORDER as string[]).includes(v);
}
