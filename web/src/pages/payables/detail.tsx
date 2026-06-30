import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, Check, X, Wallet, Pencil } from 'lucide-react';
import { type Installment, type PayableAttachment } from '../../api';
import {
  useApprovePayable,
  useCancelPayable,
  useChartAccounts,
  useCostCenters,
  useDeletePayableAttachment,
  useExpenseCategories,
  usePayable,
  usePayableAuditTrail,
  useRejectPayable,
} from '../../queries';
import { brl } from '../../lib';
import { Button, Input, Panel, EmptyState, Drawer, ConfirmDialog } from '../../ui';
import { fmtDate, fmtDateTime } from '../../datetime';
import { AuditTimeline, BalanceForecastBanner, SlaChips } from '../../components/workflow';
import { PayableStatusBadge } from './badges';
import { InstallmentsTable } from './installments-table';
import { AttachmentsPanel } from './attachments-panel';
import { PaymentHistory } from './payment-history';
import { PaymentForm } from './payment-form';
import { WithholdingSection } from './withholding-section';

/* ============ DETAIL ============ */
export function PayableDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const { t } = useTranslation();
  const query = usePayable(id);
  const p = query.data ?? null;
  const audit = usePayableAuditTrail(id);

  const centros = useCostCenters(p?.companyId ?? '').data ?? [];
  const categorias = useExpenseCategories(true).data ?? [];
  const accountingAccounts = useChartAccounts({ chartType: 'Accounting' }).data ?? [];
  const financialAccounts = useChartAccounts({ chartType: 'Financial' }).data ?? [];

  const approve = useApprovePayable();
  const reject = useRejectPayable();
  const cancel = useCancelPayable();
  const deleteAttachment = useDeletePayableAttachment();

  const [rejectOpen, setRejectOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [cancelReason, setCancelReason] = useState('');
  const [attToDelete, setAttToDelete] = useState<PayableAttachment | null>(null);
  const [baixaInstallment, setBaixaInstallment] = useState<Installment | null>(null);

  if (query.isLoading) return <div className='empty-state'>{t('payables.loading')}</div>;
  if (!p)
    return (
      <EmptyState icon={<Wallet />} title={t('payables.empty.notFoundTitle')} sub={t('payables.empty.notFoundSub')} />
    );

  const centroNome = centros.find((c) => c.id === p.costCenterId)?.name ?? '—';
  const categoriaNome = categorias.find((c) => c.id === p.categoryId)?.name ?? '—';
  const accountLabel = (accs: typeof accountingAccounts, accountId: string | null) => {
    if (!accountId) return t('payables.notClassified');
    const a = accs.find((x) => x.id === accountId);
    return a ? `${a.code} — ${a.name}` : t('payables.notClassified');
  };
  const editable = p.status === 'PendingApproval' || p.status === 'Received' || p.status === 'PendingClassification';

  return (
    <>
      <button className='backlink' onClick={() => nav('/app/payables')}>
        <ChevronLeft /> {t('payables.back')}
      </button>
      <div className='detail-head'>
        <div>
          <h1 className='detail-title'>{p.description}</h1>
          <div className='detail-sub' style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <span>
              {t('payables.sub')} · {p.counterpartyName || '—'}
            </span>
            <span className={`badge-status ${p.isClassified ? 'badge-pago' : 'badge-pendente'}`}>
              {p.isClassified ? t('payables.classified') : t('payables.unclassified')}
            </span>
            {p.installments[0] && (
              <SlaChips stageLabel={t('payables.status.' + p.status)} dueDate={p.installments[0].dueDate} />
            )}
          </div>
        </div>
        <div className='detail-actions'>
          {p.status === 'PendingApproval' && (
            <BalanceForecastBanner companyId={p.companyId} additionalOutflow={p.totalAmount} />
          )}
          {p.status === 'PendingApproval' && (
            <Button className='btn-approve' icon={<Check />} onClick={() => approve.mutate({ id: p.id })}>
              {t('payables.actions.approve')}
            </Button>
          )}
          {p.status === 'PendingApproval' && (
            <Button icon={<X />} onClick={() => setRejectOpen(true)}>
              {t('payables.actions.reject')}
            </Button>
          )}
          {editable && (
            <Button icon={<Pencil />} onClick={() => nav(`/app/payables/${p.id}/edit`)}>
              {t('payables.actions.edit')}
            </Button>
          )}
          {editable && (
            <Button icon={<X />} onClick={() => setCancelOpen(true)}>
              {t('payables.actions.cancel')}
            </Button>
          )}
        </div>
      </div>

      <div className='info-grid'>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.totalAmount')}</div>
          <div className='info-value'>{brl(p.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.status')}</div>
          <div style={{ marginTop: 4 }}>
            <PayableStatusBadge s={p.status} />
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.costCenter')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {centroNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.category')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {categoriaNome}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.accountingAccount')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {accountLabel(accountingAccounts, p.accountingAccountId)}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.financialAccount')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {accountLabel(financialAccounts, p.financialAccountId)}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.documentNumber')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {p.documentNumber || '—'}
          </div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.cards.issueDate')}</div>
          <div className='info-value' style={{ fontSize: 15 }}>
            {p.issueDate ? fmtDate(p.issueDate) : '—'}
          </div>
        </div>
      </div>

      {p.rejectionReason && (
        <p className='create-err'>{t('payables.rejectionReason', { reason: p.rejectionReason })}</p>
      )}
      {p.cancelReason && <p className='create-err'>{t('payables.cancelReason', { reason: p.cancelReason })}</p>}

      {(p.status === 'Scheduled' || p.status === 'Paid') && p.approvedBy && p.approvedAt && (
        <p className='detail-sub'>
          {t('payables.approvedBy', { user: p.approvedByName ?? p.approvedBy, date: fmtDateTime(p.approvedAt) })}
        </p>
      )}
      {p.status === 'Rejected' && p.rejectedBy && p.rejectedAt && (
        <p className='detail-sub'>
          {t('payables.rejectedBy', { user: p.rejectedByName ?? p.rejectedBy, date: fmtDateTime(p.rejectedAt) })}
        </p>
      )}

      <InstallmentsTable payable={p} onRegister={setBaixaInstallment} />

      <WithholdingSection payable={p} editable={editable} />

      {[...p.installments]
        .sort((a, b) => a.sequence - b.sequence)
        .filter((inst) => inst.payments.length > 0)
        .map((inst) => (
          <Panel
            key={inst.id}
            title={`${t('payments.title')} — ${t('installments.label')} ${inst.sequence}/${p.installments.length}`}
          >
            <PaymentHistory payable={p} installment={inst} />
          </Panel>
        ))}

      <AttachmentsPanel payable={p} onDelete={setAttToDelete} />

      <Panel title={t('payables.cards.timeline')}>
        <AuditTimeline events={audit.data ?? []} loading={audit.isLoading} />
      </Panel>

      {rejectOpen && (
        <ConfirmDialog
          title={t('payables.reject.title')}
          danger
          busy={reject.isPending}
          confirmLabel={t('payables.reject.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.reject.message')}</div>
              <Input
                autoFocus
                value={rejectReason}
                placeholder={t('payables.reject.placeholder')}
                onChange={(e) => setRejectReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            reject.mutate({ id: p.id, reason: rejectReason.trim() }, { onSuccess: () => setRejectOpen(false) })
          }
          onCancel={() => setRejectOpen(false)}
        />
      )}

      {cancelOpen && (
        <ConfirmDialog
          title={t('payables.cancel.title')}
          danger
          busy={cancel.isPending}
          confirmLabel={t('payables.cancel.confirm')}
          message={
            <div>
              <div style={{ marginBottom: 8 }}>{t('payables.cancel.message')}</div>
              <Input
                autoFocus
                value={cancelReason}
                placeholder={t('payables.cancel.placeholder')}
                onChange={(e) => setCancelReason(e.target.value)}
              />
            </div>
          }
          onConfirm={() =>
            cancel.mutate({ id: p.id, reason: cancelReason.trim() }, { onSuccess: () => setCancelOpen(false) })
          }
          onCancel={() => setCancelOpen(false)}
        />
      )}

      <Drawer
        open={!!baixaInstallment}
        title={t('payments.registerTitle')}
        sub={
          baixaInstallment
            ? `${t('installments.label')} ${baixaInstallment.sequence}/${p.installments.length}`
            : undefined
        }
        onClose={() => setBaixaInstallment(null)}
      >
        {baixaInstallment && (
          <PaymentForm
            payable={p}
            installment={baixaInstallment}
            onDone={() => setBaixaInstallment(null)}
            onCancel={() => setBaixaInstallment(null)}
          />
        )}
      </Drawer>

      {attToDelete && (
        <ConfirmDialog
          title={t('attachments.deleteTitle')}
          danger
          busy={deleteAttachment.isPending}
          confirmLabel={t('attachments.remove')}
          message={t('attachments.deleteMessage', { name: attToDelete.fileName })}
          onConfirm={() =>
            deleteAttachment.mutate(
              { id: p.id, attachmentId: attToDelete.id },
              { onSuccess: () => setAttToDelete(null) },
            )
          }
          onCancel={() => setAttToDelete(null)}
        />
      )}
    </>
  );
}
