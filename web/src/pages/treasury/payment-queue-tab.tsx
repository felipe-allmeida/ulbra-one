import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ChevronDown, ChevronRight, Info, Lock, Receipt, Wallet } from 'lucide-react';
import { type PayableListItem, type PaymentMethod } from '../../api';
import {
  usePayable,
  usePayables,
  usePaymentQueueSummary,
  useHoldInstallment,
  useReleaseInstallmentHold,
  useUnits,
} from '../../queries';
import { P, usePermissions } from '../../permissions';
import { brlCents } from '../../lib';
import { useOriginNav, usePageParam, useResetPageOnChange } from '../../nav';
import { fmtDate } from '../../datetime';
import { Button, Drawer, EmptyState, LoadingState, Pager, Panel, Select, Skeleton, ToolbarField } from '../../ui';
import { daysUntil, dueTone } from '../../components/workflow';
import { railBucket, toRailLine, todayIso, type PaymentRailBucket } from '../payables/payment-rail';
import { InstallmentRailChip } from '../payables/badges';
import { PaymentForm } from '../payables/payment-form';
import { HoldPrompt } from '../payables/hold-prompt';
import { GenerateDialog } from '../remessas/generate-dialog';
import { PAYMENT_METHODS } from '../schemas';
import {
  competencyLabel,
  formatVendorDocument,
  groupQueueByDueDate,
  isAgreementBoleto,
  queueRowNumber,
  truncateDigitableLine,
  type QueueGroup,
} from './queue-groups';

type RailFilter = 'all' | 'remittance' | 'manual' | 'held';

const RAIL_FILTERS: { key: RailFilter; buckets: PaymentRailBucket[] | null }[] = [
  { key: 'all', buckets: null },
  { key: 'remittance', buckets: ['scheduled', 'nextSweep', 'inRemittance'] },
  { key: 'manual', buckets: ['manual'] },
  { key: 'held', buckets: ['held'] },
];

/**
 * Fila de Pagamentos — the Financeiro role's working queue (legacy "Gestão Financeira" screen):
 * Scheduled installments grouped by due date ("Vencidos" first, collapsed), searched/filtered
 * server-side; group headers show WHOLE-SET counts and net totals from the queue summary, never
 * page-local sums. Checkbox selection feeds "Gerar remessa" (selection mode).
 */
export function PaymentQueueTab({ companyId, search }: { companyId: string; search: string }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const openPayable = useOriginNav();
  const { can } = usePermissions();
  const canGenerate = can(P.treasuryWrite);
  const canPay = can(P.payablesPay);
  const canHold = can(P.payablesWrite);
  const today = todayIso();

  const [page, setPage] = usePageParam();
  const [method, setMethod] = useState<PaymentMethod | ''>('');
  const [railFilter, setRailFilter] = useState<RailFilter>('all');
  // installmentId → net amount, so the bulk bar can total without re-finding rows across pages.
  const [selected, setSelected] = useState<Record<string, number>>({});
  const [overdueOpen, setOverdueOpen] = useState(false);
  const [holdingId, setHoldingId] = useState<string | null>(null);
  const [generateOpen, setGenerateOpen] = useState(false);
  const [baixa, setBaixa] = useState<PayableListItem | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  // Copy the full linha digitável (the row shows a truncated preview) to the clipboard, with brief feedback.
  const copyDigitableLine = async (id: string, line: string) => {
    try {
      await navigator.clipboard?.writeText(line);
      setCopiedId(id);
      setTimeout(() => setCopiedId((cur) => (cur === id ? null : cur)), 1500);
    } catch {
      /* clipboard unavailable (e.g. insecure context) — no-op */
    }
  };

  // "Não dá para remeter, e por quê" — refina VendorBankInfoMissing pela forma da parcela (AEL-933).
  const manualReasonText = (reason: string | null, m: PaymentMethod | null | undefined) => {
    if (!reason) return undefined;
    if (reason === 'VendorBankInfoMissing' && (m === 'Pix' || m === 'Ted'))
      return t(`installments.manualReason.VendorBankInfoMissing_${m}`);
    return t(`installments.manualReason.${reason}`);
  };

  useResetPageOnChange(setPage, [companyId, search, method, railFilter]);

  const filterParams = {
    companyId,
    status: 'Scheduled' as const,
    paid: false,
    search: search || undefined,
    paymentMethod: method || undefined,
  };
  const list = usePayables({ ...filterParams, page, pageSize: 50 }, { enabled: !!companyId });
  const summary = usePaymentQueueSummary(filterParams, { enabled: !!companyId }).data;
  const units = useUnits(companyId).data ?? [];
  const unitAcronym = (unitId: string | null | undefined) =>
    unitId ? (units.find((u) => u.id === unitId)?.acronym ?? '—') : '—';

  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  const activeBuckets = RAIL_FILTERS.find((f) => f.key === railFilter)?.buckets ?? null;
  const visible = activeBuckets ? items.filter((l) => activeBuckets.includes(railBucket(toRailLine(l), today))) : items;
  const groups = groupQueueByDueDate(visible, today);

  const hold = useHoldInstallment();
  const release = useReleaseInstallmentHold();

  // Baixa drawer (manual rail) — same wiring as the payables payment tab.
  const baixaDetail = usePayable(baixa?.payableId);
  const baixaInstallment = baixaDetail.data?.installments.find((i) => i.id === baixa?.installmentId) ?? null;

  const selectable = (l: PayableListItem) => {
    const bucket = railBucket(toRailLine(l), today);
    return bucket === 'scheduled' || bucket === 'nextSweep';
  };
  const toggleSelected = (l: PayableListItem) =>
    setSelected((prev) => {
      const next = { ...prev };
      if (next[l.installmentId] !== undefined) delete next[l.installmentId];
      else next[l.installmentId] = l.netAmount;
      return next;
    });
  const selectedIds = Object.keys(selected);
  const selectedNet = Object.values(selected).reduce((s, v) => s + v, 0);

  // Nothing here can be remitted → tell the operator what to configure, instead of leaving a wall of
  // disabled checkboxes. Only on the "all"/"remessa" views: on the "manual"/"held" chips it's expected.
  const noneEligible = canGenerate && visible.length > 0 && !visible.some(selectable);
  const showNoEligibleHint = noneEligible && (railFilter === 'all' || railFilter === 'remittance');

  const headerFor = (g: QueueGroup) => {
    if (g.key === 'overdue') {
      return {
        label: t('treasury.queue.groupOverdue'),
        count: summary?.overdueCount ?? g.items.length,
        net: summary?.overdueNetTotal ?? g.items.reduce((s, i) => s + i.netAmount, 0),
        cls: 'pq-group-head--danger',
      };
    }
    const sg = summary?.groups.find((x) => x.dueDate === g.key);
    return {
      label: g.key === today ? `${t('treasury.queue.groupToday')} — ${fmtDate(g.key)}` : fmtDate(g.key),
      count: sg?.count ?? g.items.length,
      net: sg?.netTotal ?? g.items.reduce((s, i) => s + i.netAmount, 0),
      cls: g.key === today ? 'pq-group-head--today' : '',
    };
  };

  return (
    <>
      <div className='pq-toolbar'>
        <div className='pq-chips' role='group' aria-label={t('treasury.queue.railFilterLabel')}>
          {RAIL_FILTERS.map((f) => (
            <button
              key={f.key}
              type='button'
              className={`pq-chip ${railFilter === f.key ? 'on' : ''}`}
              onClick={() => setRailFilter(f.key)}
            >
              {t(`treasury.queue.rail.${f.key}`)}
            </button>
          ))}
        </div>
        <ToolbarField label={t('treasury.queue.methodFilter')}>
          <Select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod | '')}>
            <option value=''>{t('treasury.queue.methodAll')}</option>
            {PAYMENT_METHODS.map((m) => (
              <option key={m} value={m}>
                {t(`payments.methods.${m}`)}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>

      {list.isLoading ? (
        <Panel title={t('treasury.home.tabs.queue')}>
          <div className='pq-list'>
            {Array.from({ length: 5 }).map((_, i) => (
              <div className='pq-row' key={i}>
                <div className='pq-row-main' style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <Skeleton width={200} />
                  <Skeleton width={260} height={10} />
                </div>
                <div className='pq-row-right'>
                  <Skeleton width={90} />
                </div>
              </div>
            ))}
          </div>
        </Panel>
      ) : visible.length === 0 ? (
        <EmptyState icon={<Wallet />} title={t('treasury.queue.emptyTitle')} sub={t('treasury.queue.emptySub')} />
      ) : (
        <Panel title={t('treasury.home.tabs.queue')}>
          {showNoEligibleHint && <div className='pq-hint'>{t('treasury.queue.noEligibleHint')}</div>}
          {/* Flex list (not a table): the queue reflows on any width — no clipped cells/actions. */}
          <div className='pq-list'>
            {groups.map((g) => {
              const head = headerFor(g);
              const collapsed = g.key === 'overdue' && !overdueOpen;
              const isOverdue = g.key === 'overdue';
              const headerInner = (
                <>
                  {isOverdue && (collapsed ? <ChevronRight size={15} /> : <ChevronDown size={15} />)}
                  <span className='pq-group-title'>{head.label}</span>
                  <span className='pq-group-meta'>
                    {t('treasury.queue.groupSummary', { count: head.count, amount: brlCents(head.net) })}
                  </span>
                </>
              );
              return (
                <div key={g.key} className='pq-group'>
                  {isOverdue ? (
                    <button
                      type='button'
                      className={`pq-group-head ${head.cls}`}
                      onClick={() => setOverdueOpen((v) => !v)}
                    >
                      {headerInner}
                    </button>
                  ) : (
                    <div className={`pq-group-head ${head.cls}`}>{headerInner}</div>
                  )}
                  {!collapsed &&
                    g.items.map((l) => {
                      const line = toRailLine(l);
                      const bucket = railBucket(line, today);
                      const overdueRow = l.dueDate < today;
                      const tone = overdueRow ? 'danger' : dueTone(daysUntil(l.dueDate));
                      const rowSelectable = selectable(l);
                      // Non-selectable manual rows explain WHY (in the checkbox's place) instead of a blank spacer.
                      const manualWhy =
                        bucket === 'manual' ? manualReasonText(line.manualReason, l.effectiveMethod) : undefined;
                      return (
                        // Clicking the row opens the título, the same as the "Analisar" button — every other list
                        // in the app enters on row click, and this one used to demand hunting for the button.
                        // Checkbox and action buttons stop propagation so they keep their own behaviour.
                        <div
                          key={l.installmentId}
                          className={`pq-row row-click ${overdueRow ? 'pq-row--overdue' : ''}`}
                          onClick={() => {
                            void openPayable(`/app/payables/${l.payableId}`);
                          }}
                        >
                          {/* Selectable rows get a checkbox; a non-selectable manual row shows an "i" in the
                              same slot naming WHY it can't be remitted (hover). Other non-selectable rows
                              (held / already in a remittance) keep an aligned spacer — their state is on the
                              rail chip. */}
                          {canGenerate &&
                            (rowSelectable ? (
                              <input
                                type='checkbox'
                                className='pq-row-check'
                                checked={selected[l.installmentId] !== undefined}
                                onClick={(ev) => ev.stopPropagation()}
                                onChange={() => toggleSelected(l)}
                                aria-label={t('treasury.queue.selectRow', { vendor: l.counterpartyName })}
                              />
                            ) : manualWhy ? (
                              <span
                                className='pq-row-why'
                                title={manualWhy}
                                aria-label={manualWhy}
                                role='img'
                                style={{
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                  justifyContent: 'center',
                                  width: 18,
                                  opacity: 0.7,
                                  cursor: 'help',
                                }}
                              >
                                <Info size={16} aria-hidden='true' />
                              </span>
                            ) : (
                              <span className='pq-row-check-spacer' aria-hidden='true' />
                            ))}
                          <div className='pq-row-main'>
                            <div className='pq-row-title'>
                              <span className='pq-row-vendor'>{l.counterpartyName || '—'}</span>
                              {l.vendorDocument && (
                                <span className='pq-sub t-mono'>{formatVendorDocument(l.vendorDocument)}</span>
                              )}
                            </div>
                            <div className='pq-row-meta'>
                              <span className='t-mono'>{queueRowNumber(l)}</span>
                              <span>{t('treasury.queue.competency', { value: competencyLabel(l.issueDate) })}</span>
                              <span>{unitAcronym(l.originUnitId)}</span>
                            </div>
                            <div className='pq-row-tags'>
                              <span className={`sla-chip sla-${tone}`}>{fmtDate(l.dueDate)}</span>
                              <span className='pq-method'>
                                {l.effectiveMethod ? t(`payments.methods.${l.effectiveMethod}`) : '—'}
                              </span>
                              {bucket === 'inRemittance' && l.remittanceSequence != null ? (
                                <button
                                  type='button'
                                  className='badge-status badge-remessa'
                                  style={{ cursor: 'pointer', background: 'none', border: 'none', padding: 0 }}
                                  onClick={() =>
                                    l.remittanceId && nav(`/app/treasury?tab=remittances&remittance=${l.remittanceId}`)
                                  }
                                >
                                  {t('installments.rail.inRemittance', { seq: l.remittanceSequence })}
                                </button>
                              ) : (
                                <InstallmentRailChip line={line} today={today} />
                              )}
                              {isAgreementBoleto(l.boletoDigitableLine) && (
                                <span className='badge-status badge-pendente'>{t('treasury.queue.agreementChip')}</span>
                              )}
                              {l.hasFiscalDocument === false && (
                                <span className='badge-status badge-rejeitado'>
                                  {t('treasury.queue.fiscalDocPending')}
                                </span>
                              )}
                              {l.boletoDigitableLine && (
                                <button
                                  type='button'
                                  className='pq-sub pq-digitable'
                                  title={t('treasury.queue.copyDigitableLine')}
                                  onClick={() => copyDigitableLine(l.installmentId, l.boletoDigitableLine!)}
                                  style={{
                                    background: 'none',
                                    border: 'none',
                                    padding: 0,
                                    font: 'inherit',
                                    color: 'inherit',
                                    cursor: 'pointer',
                                  }}
                                >
                                  {copiedId === l.installmentId
                                    ? t('treasury.queue.digitableCopied')
                                    : truncateDigitableLine(l.boletoDigitableLine)}
                                </button>
                              )}
                            </div>
                          </div>
                          <div className='pq-row-right'>
                            <span className='pq-row-value'>{brlCents(l.netAmount)}</span>
                            {holdingId === l.installmentId ? (
                              <HoldPrompt
                                onConfirm={(reason) => {
                                  hold.mutate({ id: l.payableId, installmentId: l.installmentId, reason });
                                  setHoldingId(null);
                                }}
                                onCancel={() => setHoldingId(null)}
                              />
                            ) : (
                              <div className='pq-row-actions' onClick={(ev) => ev.stopPropagation()}>
                                <Button
                                  variant='ghost'
                                  onClick={() => {
                                    void openPayable(`/app/payables/${l.payableId}`);
                                  }}
                                >
                                  {t('treasury.queue.analyze')}
                                </Button>
                                {bucket === 'manual' && canPay && (
                                  <Button icon={<Receipt />} onClick={() => setBaixa(l)}>
                                    {t('payments.register')}
                                  </Button>
                                )}
                                {rowSelectable && canHold && (
                                  <Button icon={<Lock />} onClick={() => setHoldingId(l.installmentId)}>
                                    {t('workflow.payment.hold')}
                                  </Button>
                                )}
                                {bucket === 'held' && canHold && (
                                  <Button
                                    onClick={() => release.mutate({ id: l.payableId, installmentId: l.installmentId })}
                                  >
                                    {t('workflow.payment.release')}
                                  </Button>
                                )}
                              </div>
                            )}
                          </div>
                        </div>
                      );
                    })}
                </div>
              );
            })}
          </div>
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </Panel>
      )}

      {selectedIds.length > 0 && canGenerate && (
        <div className='pq-bulkbar' role='region' aria-label={t('treasury.queue.bulkBarLabel')}>
          <strong>
            {t('treasury.queue.selectedSummary', { count: selectedIds.length, amount: brlCents(selectedNet) })}
          </strong>
          <span className='pq-bulkbar-spacer' />
          <Button variant='brand' onClick={() => setGenerateOpen(true)}>
            {t('treasury.queue.generateRemittance')}
          </Button>
          <Button onClick={() => setSelected({})}>{t('treasury.queue.clearSelection')}</Button>
        </div>
      )}

      {generateOpen && (
        <GenerateDialog
          companyId={companyId}
          installmentIds={selectedIds}
          onClose={() => setGenerateOpen(false)}
          onGenerated={() => setSelected({})}
        />
      )}

      <Drawer
        open={!!baixa}
        onClose={() => setBaixa(null)}
        title={t('payments.register')}
        sub={baixa?.counterpartyName}
      >
        {baixaDetail.isLoading ? (
          <LoadingState />
        ) : (
          baixaDetail.data &&
          baixaInstallment && (
            <PaymentForm
              payable={baixaDetail.data}
              installment={baixaInstallment}
              onDone={() => setBaixa(null)}
              onCancel={() => setBaixa(null)}
            />
          )
        )}
      </Drawer>
    </>
  );
}
