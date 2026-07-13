import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Receipt, FileOutput, Lock } from 'lucide-react';
import { type PayableListItem } from '../../api';
import { usePayables, usePayable, useHoldInstallment, useReleaseInstallmentHold } from '../../queries';
import { brl } from '../../lib';
import { Panel, Button, Drawer, EmptyState, Input } from '../../ui';
import { fmtDate } from '../../datetime';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { railBucket, type RailLine } from './payment-rail';
import { InstallmentRailChip } from './badges';
import { PaymentForm } from './payment-form';

// ─── Types ────────────────────────────────────────────────────────────────────
type RailKey = 'remittance' | 'manual';

// ─── Data helpers ─────────────────────────────────────────────────────────────

/**
 * Converts a PayableListItem into a RailLine-compatible shape.
 * The `rail` and `manualReason` fields are computed by the list endpoint (batched,
 * via RemittanceEligibility.Evaluate) — no client-side heuristic needed.
 */
function toRailLine(item: PayableListItem): RailLine {
  return {
    installmentStatus: item.installmentStatus,
    isHeld: item.isHeld ?? false,
    rail: (item.rail as 'Remittance' | 'Manual') ?? 'Manual',
    manualReason: item.manualReason ?? null,
    scheduledPaymentDate: item.scheduledPaymentDate ?? item.dueDate,
    remittanceSequence: item.remittanceSequence ?? null,
  };
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

/** Groups items by scheduledPaymentDate for the Remessas rail. */
function groupByScheduledDate(
  items: PayableListItem[],
): { date: string; items: PayableListItem[]; subtotal: number }[] {
  const map = new Map<string, PayableListItem[]>();
  for (const item of items) {
    const d = item.scheduledPaymentDate ?? item.dueDate;
    (map.get(d) ?? map.set(d, []).get(d)!).push(item);
  }
  return Array.from(map.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, groupItems]) => ({
      date,
      items: groupItems,
      // Net of withholdings — what the remittance will actually debit on that date.
      subtotal: groupItems.reduce((s, i) => s + i.netAmount, 0),
    }));
}

// ─── Hold reason prompt ───────────────────────────────────────────────────────

function HoldPrompt({ onConfirm, onCancel }: { onConfirm: (reason: string) => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      <Input
        placeholder={t('workflow.payment.holdReasonPrompt')}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        style={{ flex: '1 1 180px' }}
      />
      <Button
        onClick={() => {
          if (reason.trim()) onConfirm(reason.trim());
        }}
        disabled={!reason.trim()}
      >
        {t('workflow.payment.confirm')}
      </Button>
      <Button variant='ghost' onClick={onCancel}>
        {t('workflow.payment.cancel')}
      </Button>
    </div>
  );
}

// ─── Remessas rail ────────────────────────────────────────────────────────────

function RemessasRail({
  items,
  onNavigateRemessa,
}: {
  items: PayableListItem[];
  onNavigateRemessa: (remittanceId: string) => void;
}) {
  const { t } = useTranslation();
  const today = todayIso();
  const holdMutation = useHoldInstallment();
  const [holdingId, setHoldingId] = useState<string | null>(null);

  const groups = groupByScheduledDate(items);

  // nextSweep banner: earliest group with scheduledPaymentDate <= today
  const nextSweepGroups = groups.filter((g) => g.date <= today);
  const bannerGroup = nextSweepGroups[0] ?? groups[0];
  const showBanner = bannerGroup != null;

  return (
    <>
      {showBanner && (
        <div className='chip' style={{ marginBottom: 12, display: 'inline-block' }}>
          {t('workflow.payment.nextRemittance', {
            date: fmtDate(bannerGroup.date),
            count: bannerGroup.items.length,
            amount: brl(bannerGroup.subtotal),
          })}
        </div>
      )}

      {groups.map((g) => (
        <Panel key={g.date} title={`${fmtDate(g.date)} · ${brl(g.subtotal)}`}>
          <table>
            <thead>
              <tr>
                <th>{t('payables.columns.dueDate')}</th>
                <th>{t('payables.columns.vendor')}</th>
                <th>{t('payables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.grossAmount')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.toPay')}</th>
                <th>{t('payables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {g.items.map((l) => {
                const railLine = toRailLine(l);
                const bucket = railBucket(railLine, today);
                const canHold = bucket === 'scheduled' || bucket === 'nextSweep';
                const isInRemittance = bucket === 'inRemittance';

                return (
                  <tr key={l.installmentId}>
                    <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                    <td>{l.counterpartyName || '—'}</td>
                    <td className='t-mono'>
                      {l.sequence}/{l.installmentCount}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brl(l.amount)}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      <strong>{brl(l.netAmount)}</strong>
                    </td>
                    <td>
                      {isInRemittance && l.remittanceSequence != null ? (
                        <button
                          type='button'
                          className='badge-status badge-remessa'
                          style={{ cursor: 'pointer', background: 'none', border: 'none', padding: 0 }}
                          onClick={() => l.remittanceId && onNavigateRemessa(l.remittanceId)}
                        >
                          {t('installments.rail.inRemittance', { seq: l.remittanceSequence })}
                        </button>
                      ) : (
                        <InstallmentRailChip line={railLine} today={today} />
                      )}
                    </td>
                    <td>
                      {canHold && holdingId !== l.installmentId && (
                        <Button icon={<Lock />} onClick={() => setHoldingId(l.installmentId)}>
                          {t('workflow.payment.hold')}
                        </Button>
                      )}
                      {holdingId === l.installmentId && (
                        <HoldPrompt
                          onConfirm={(reason) => {
                            holdMutation.mutate({ id: l.payableId, installmentId: l.installmentId, reason });
                            setHoldingId(null);
                          }}
                          onCancel={() => setHoldingId(null)}
                        />
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </Panel>
      ))}

      {items.length === 0 && <EmptyState icon={<Receipt />} title={t('payables.empty.noItemsTitle')} />}
    </>
  );
}

// ─── Manuais rail ─────────────────────────────────────────────────────────────

function ManuaisRail({ items, onBaixa }: { items: PayableListItem[]; onBaixa: (item: PayableListItem) => void }) {
  const { t } = useTranslation();
  const today = todayIso();
  const releaseMutation = useReleaseInstallmentHold();

  // Split into held vs manual (non-held) sub-groups
  const heldItems = items.filter((l) => l.isHeld);
  const manualItems = items.filter((l) => !l.isHeld);

  if (items.length === 0) {
    return <EmptyState icon={<Receipt />} title={t('payables.empty.noItemsTitle')} />;
  }

  return (
    <>
      {manualItems.length > 0 && (
        <Panel
          title={`${t('installments.rail.payManual')} · ${brl(manualItems.reduce((s, i) => s + i.netAmount, 0))}`}
        >
          <table>
            <thead>
              <tr>
                <th>{t('payables.columns.dueDate')}</th>
                <th>{t('payables.columns.vendor')}</th>
                <th>{t('payables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.grossAmount')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.toPay')}</th>
                <th>{t('payables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {manualItems.map((l) => (
                <tr key={l.installmentId}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.counterpartyName || '—'}</td>
                  <td className='t-mono'>
                    {l.sequence}/{l.installmentCount}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    <strong>{brl(l.netAmount)}</strong>
                  </td>
                  <td>
                    <InstallmentRailChip line={toRailLine(l)} today={today} />
                  </td>
                  <td>
                    {l.installmentStatus !== 'InRemittance' && l.installmentStatus !== 'Paid' && (
                      <Button icon={<Receipt />} onClick={() => onBaixa(l)}>
                        {t('payments.register')}
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}

      {heldItems.length > 0 && (
        <Panel title={`${t('workflow.payment.heldGroup')} · ${brl(heldItems.reduce((s, i) => s + i.netAmount, 0))}`}>
          <table>
            <thead>
              <tr>
                <th>{t('payables.columns.dueDate')}</th>
                <th>{t('payables.columns.vendor')}</th>
                <th>{t('payables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.grossAmount')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.toPay')}</th>
                <th>{t('payables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {heldItems.map((l) => (
                <tr key={l.installmentId}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.counterpartyName || '—'}</td>
                  <td className='t-mono'>
                    {l.sequence}/{l.installmentCount}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    <strong>{brl(l.netAmount)}</strong>
                  </td>
                  <td>
                    <InstallmentRailChip line={toRailLine(l)} today={today} />
                  </td>
                  <td>
                    <Button onClick={() => releaseMutation.mutate({ id: l.payableId, installmentId: l.installmentId })}>
                      {t('workflow.payment.release')}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}
    </>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export function PaymentTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  const [activeRail, setActiveRail] = useState<RailKey>('remittance');
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: 'Scheduled', page }, { enabled: !!companyId });
  const items = list.data?.items ?? [];
  const today = todayIso();

  // Partition items by rail
  const remessasItems = items.filter((l) => {
    const bucket = railBucket(toRailLine(l), today);
    return bucket === 'scheduled' || bucket === 'nextSweep' || bucket === 'inRemittance' || bucket === 'paid';
  });
  const manuaisItems = items.filter((l) => {
    const bucket = railBucket(toRailLine(l), today);
    return bucket === 'manual' || bucket === 'held';
  });

  // "A pagar": net of withholdings — matches what the remittances/baixas will actually debit.
  const total = items.reduce((s, i) => s + i.netAmount, 0);

  // Dar baixa drawer state (used in Manuais rail)
  const [baixa, setBaixa] = useState<PayableListItem | null>(null);
  const detailQuery = usePayable(baixa?.payableId);
  const detail = detailQuery ?? { data: undefined };
  const installment = detail.data?.installments.find((i) => i.id === baixa?.installmentId) ?? null;

  const railTabs: StageTab[] = [
    { key: 'remittance', label: t('workflow.payment.railRemittance'), count: remessasItems.length },
    { key: 'manual', label: t('workflow.payment.railManual'), count: manuaisItems.length },
  ];

  if (items.length === 0) {
    return (
      <EmptyState icon={<Receipt />} title={t('payables.empty.noItemsTitle')} sub={t('payables.empty.noItemsSub')} />
    );
  }

  return (
    <>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <strong>{t('workflow.payment.weekSummaryToPay', { amount: brl(total), count: items.length })}</strong>
        {activeRail === 'remittance' && (
          <Button
            icon={<FileOutput />}
            onClick={() => {
              void nav('/app/remittances');
            }}
          >
            {t('payables.payment.generateRemittance')}
          </Button>
        )}
      </div>

      <StageTabBar tabs={railTabs} active={activeRail} onChange={(k) => setActiveRail(k as RailKey)} />

      <div style={{ marginTop: 16 }}>
        {activeRail === 'remittance' ? (
          <RemessasRail
            items={remessasItems}
            onNavigateRemessa={(remittanceId) => {
              void nav(`/app/remittances?remittance=${remittanceId}`);
            }}
          />
        ) : (
          <ManuaisRail items={manuaisItems} onBaixa={setBaixa} />
        )}
      </div>

      <Drawer
        open={!!baixa}
        onClose={() => setBaixa(null)}
        title={t('payments.register')}
        sub={baixa?.counterpartyName}
      >
        {detail.data && installment && (
          <PaymentForm
            payable={detail.data}
            installment={installment}
            onDone={() => setBaixa(null)}
            onCancel={() => setBaixa(null)}
          />
        )}
      </Drawer>
    </>
  );
}
