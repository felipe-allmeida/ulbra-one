import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, HandCoins, Pencil, X } from 'lucide-react';
import { type ReceivableInstallment } from '../../api';
import { useCancelReceivable, useCostCenters, useReceivable, useReceivableAuditTrail } from '../../queries';
import { brl } from '../../lib';
import { P, usePermissions } from '../../permissions';
import { Button, Input, Panel, EmptyState, Drawer, ConfirmDialog } from '../../ui';
import { fmtDate } from '../../datetime';
import { AuditTimeline, SlaChips } from '../../components/workflow';
import { ReceivableStatusBadge } from './badges';
import { InstallmentsTable } from './installments-table';
import { ReceiptHistory } from './receipt-history';
import { ReceiptForm } from './receipt-form';

/* ============ DETAIL ============ */
export function ReceivableDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const { t } = useTranslation();
  const { can } = usePermissions();
  const query = useReceivable(id);
  const r = query.data ?? null;

  const centros = useCostCenters(r?.companyId ?? '').data ?? [];
  const cancel = useCancelReceivable();
  const audit = useReceivableAuditTrail(id);

  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [baixaInstallment, setBaixaInstallment] = useState<ReceivableInstallment | null>(null);

  if (query.isLoading) return <div className='empty-state'>{t('receivables.loading')}</div>;
  if (!r)
    return (
      <EmptyState
        icon={<HandCoins />}
        title={t('receivables.empty.notFoundTitle')}
        sub={t('receivables.empty.notFoundSub')}
      />
    );

  const centroNome = centros.find((c) => c.id === r.costCenterId)?.name ?? '—';
  const hasReceipts = r.installments.some((i) => i.receipts.some((rc) => !rc.isReversed));
  const editable = r.status === 'AwaitingReceipt' && !hasReceipts;

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/receivables')}>
        <ChevronLeft /> {t('receivables.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{r.description}</h1>
          <div className='detail-sub'>
            {t('receivables.sub')} · {r.payerName || '—'}
          </div>
        </div>
        <div className='detail-actions'>
          {r.installments[0] && (
            <SlaChips stageLabel={t('receivables.status.' + r.status)} dueDate={r.installments[0].dueDate} />
          )}
          {editable && can(P.receivablesWrite) && (
            <Button icon={<Pencil />} onClick={() => nav(`/app/receivables/${r.id}/edit`)}>
              {t('receivables.actions.edit')}
            </Button>
          )}
          {editable && can(P.receivablesWrite) && (
            <Button icon={<X />} onClick={() => setCancelOpen(true)}>
              {t('receivables.actions.cancel')}
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.totalAmount')}</div>
          <div className='info-value'>{brl(r.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.status')}</div>
          <div style={{ marginTop: 4 }}>
            <ReceivableStatusBadge s={r.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.costCenter')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {centroNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.payer')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.payerName || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.documentNumber')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.documentNumber || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('receivables.cards.issueDate')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {r.issueDate ? fmtDate(r.issueDate) : '—'}
          </div>
        </div>
      </div>

      {r.cancelReason && <p className='create-err'>{t('receivables.cancelReason', { reason: r.cancelReason })}</p>}

      <InstallmentsTable receivable={r} onRegister={setBaixaInstallment} />

      {[...r.installments]
        .sort((a, b) => a.sequence - b.sequence)
        .filter((inst) => inst.receipts.length > 0)
        .map((inst) => (
          <Panel
            key={inst.id}
            title={`${t('receipts.title')} — ${t('receivableInstallments.label')} ${inst.sequence}/${r.installments.length}`}
          >
            <ReceiptHistory receivable={r} installment={inst} />
          </Panel>
        ))}

      <Panel title={t('receivables.cards.timeline')}>
        <AuditTimeline events={audit.data ?? []} loading={audit.isLoading} />
      </Panel>

      {cancelOpen && (
        <ConfirmDialog
          title={t('receivables.cancel.title')}
          danger
          busy={cancel.isPending}
          confirmLabel={t('receivables.cancel.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('receivables.cancel.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('receivables.cancel.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate({ id: r.id, reason: cancelReason.trim() }, { onSuccess: () => setCancelOpen(false) })
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      <Drawer
        open={!!baixaInstallment}
        title={t('receipts.registerTitle')}
        sub={
          baixaInstallment
            ? `${t('receivableInstallments.label')} ${baixaInstallment.sequence}/${r.installments.length}`
            : undefined
        }
        onClose={() => setBaixaInstallment(null)}
      >
        {baixaInstallment && (
          <ReceiptForm
            receivable={r}
            installment={baixaInstallment}
            onDone={() => setBaixaInstallment(null)}
            onCancel={() => setBaixaInstallment(null)}
          />
        )}
      </Drawer>
    </>
  );
}
