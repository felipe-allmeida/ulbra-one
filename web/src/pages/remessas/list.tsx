import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, ExternalLink, FileText, Upload } from 'lucide-react';
import { api } from '../../api';
import {
  useRemittances,
  useRemittance,
  useCreateRemittance,
  useBankAccounts,
  useImportRemittanceReturn,
  useConfirmRemittanceLine,
  useMarkRemittanceLineReturned,
  type RemittanceListItem,
  type Remittance,
} from '../../queries';
import { useApp } from '../../store';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { brlCents } from '../../lib';
import { downloadWithAuth } from '../../download';
import { Button, EmptyState, Field, Modal, PageHeader, Pager, Panel, Select } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { EmpresaPicker } from '../shared';
import { fmtDateTime, fmtDate } from '../../datetime';
import { RemittanceLineStatusBadge } from './badges';

/* ============ Status badge ============ */
function RemittanceStatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  const cls: Record<string, string> = {
    Generated: 'badge-aprovado',
    Cancelled: 'badge-rejeitado',
    Confirmed: 'badge-pago',
    PartiallyReturned: 'badge-pendente',
  };
  const label = t(`remittances.status.${status}`, { defaultValue: status });
  return <span className={`badge-status ${cls[status] ?? 'badge-pendente'}`}>{label}</span>;
}

/* ============ Generate remittance dialog ============ */
// A paying account can ride the CNAB240 rail only if it is Santander (COMPE 033, in any
// entered form) and has a convênio code — mirrors the backend eligibility. Filtering the
// dropdown to eligible accounts keeps the operator out of the "banco não suportado" dead-end.
function isSantander(bankCode?: string | null): boolean {
  return (bankCode ?? '').replace(/\D/g, '').replace(/^0+/, '') === '33';
}
function isCnabEligible(a: { bankCode?: string | null; cnabAgreementCode?: string | null }): boolean {
  return isSantander(a.bankCode) && !!a.cnabAgreementCode?.trim();
}

function GenerateDialog({ companyId, onClose }: { companyId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const bankAccounts = useBankAccounts(companyId);
  const accounts = (bankAccounts.data ?? []).filter((a) => a.active && isCnabEligible(a));
  const [bankAccountId, setBankAccountId] = useState('');
  const [dueCutoff, setDueCutoff] = useState('');

  const create = useCreateRemittance({
    onSuccess: (data) => {
      const included = data.remittance.lines.length;
      const skipped = data.skipped.length;
      toast.success(t('remittances.success', { count: included }));
      if (skipped > 0) {
        toast.success(t('remittances.skipped', { count: skipped }));
      }
      onClose();
    },
    onError: (e) => {
      toast.error(getErrorMessage(e));
    },
  });

  const handleSubmit = (ev: React.FormEvent) => {
    ev.preventDefault();
    if (!bankAccountId) return;
    create.mutate({
      payingBankAccountId: bankAccountId,
      dueCutoff: dueCutoff || null,
    });
  };

  const footer = (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
      <Button type='button' onClick={onClose}>
        {t('remittances.cancel')}
      </Button>
      <Button
        type='submit'
        form='generate-remittance-form'
        variant='brand'
        disabled={!bankAccountId || create.isPending}
      >
        {create.isPending ? t('remittances.generating') : t('remittances.generate_dialog.confirm')}
      </Button>
    </div>
  );

  return (
    <Modal title={t('remittances.generate_dialog.title')} onClose={onClose} footer={footer}>
      <form
        id='generate-remittance-form'
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 16, padding: '4px 0' }}
      >
        <Field label={t('remittances.generate_dialog.bankAccount')}>
          <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)} required>
            <option value=''>{t('remittances.generate_dialog.bankAccountSelect')}</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.alias} — {a.bankCode}
              </option>
            ))}
          </Select>
          {!bankAccounts.isLoading && accounts.length === 0 && (
            <p style={{ marginTop: 6, fontSize: 13, color: 'var(--muted, #6b7280)' }}>
              {t('remittances.generate_dialog.noEligibleAccount')}
            </p>
          )}
        </Field>
        <Field label={t('remittances.generate_dialog.dueCutoff')}>
          <input type='date' className='input' value={dueCutoff} onChange={(e) => setDueCutoff(e.target.value)} />
        </Field>
      </form>
    </Modal>
  );
}

/* ============ Return reason dialog ============ */
function ReturnReasonDialog({
  onConfirm,
  onClose,
  busy,
}: {
  onConfirm: (reason: string) => void;
  onClose: () => void;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  const handleSubmit = (ev: React.FormEvent) => {
    ev.preventDefault();
    if (reason.trim()) onConfirm(reason.trim());
  };
  const footer = (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
      <Button type='button' onClick={onClose} disabled={busy}>
        {t('remittances.return.returnLineCancel')}
      </Button>
      <Button type='submit' form='return-reason-form' variant='brand' disabled={!reason.trim() || busy}>
        {t('remittances.return.returnLineConfirm')}
      </Button>
    </div>
  );
  return (
    <Modal title={t('remittances.return.returnLine')} onClose={onClose} footer={footer}>
      <form
        id='return-reason-form'
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: '4px 0' }}
      >
        <Field label={t('remittances.return.returnLinePrompt')}>
          <input className='input' value={reason} onChange={(e) => setReason(e.target.value)} required />
        </Field>
      </form>
    </Modal>
  );
}

/* ============ Remittance detail panel ============ */
function RemittanceDetail({ remittance, isDev }: { remittance: Remittance; isDev?: boolean }) {
  const { t } = useTranslation();
  const toast = useToast();
  // Importar retorno / confirmar pago / devolver linha são backed por treasury:write.
  const canWrite = usePermissions().can(P.treasuryWrite);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [returnLineId, setReturnLineId] = useState<string | null>(null);

  const importReturn = useImportRemittanceReturn({
    onSuccess: (outcome) => {
      toast.success(
        t('remittances.return.importSuccess', {
          paid: outcome.paid,
          returned: outcome.returned,
          unmatched: outcome.unmatched,
          divergent: outcome.divergent,
        }),
      );
    },
    onError: (e) => {
      toast.error(getErrorMessage(e) || t('remittances.return.importError'));
    },
  });

  const confirmPaid = useConfirmRemittanceLine({
    onSuccess: () => toast.success(t('remittances.return.confirmPaidSuccess')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const markReturned = useMarkRemittanceLineReturned({
    onSuccess: () => {
      setReturnLineId(null);
      toast.success(t('remittances.return.returnLineSuccess'));
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const handleFileChange = (ev: React.ChangeEvent<HTMLInputElement>) => {
    const file = ev.target.files?.[0];
    if (!file) return;
    importReturn.mutate({ id: remittance.id, file });
    ev.target.value = '';
  };

  return (
    <div style={{ marginTop: 16 }}>
      {/* Import .RET + simulate (dev) */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 12, flexWrap: 'wrap' }}>
        {canWrite && (
          <label
            className='btn btn-default'
            style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 6 }}
          >
            <Upload size={14} />
            {importReturn.isPending ? t('remittances.loading') : t('remittances.return.importButton')}
            <input
              ref={fileInputRef}
              type='file'
              accept='.ret,.RET,text/plain'
              style={{ display: 'none' }}
              onChange={handleFileChange}
              disabled={importReturn.isPending}
            />
          </label>
        )}

        {isDev && (
          <a
            href={api.remittanceSimulateReturnUrl(remittance.id)}
            download
            className='btn btn-ghost'
            style={{ display: 'flex', alignItems: 'center', gap: 6 }}
          >
            <ExternalLink size={14} />
            {t('remittances.return.simulateReturn')}
          </a>
        )}
      </div>

      {/* Lines table */}
      <div style={{ fontSize: 13 }}>
        <strong>{t('remittances.return.linesTitle')}</strong>
        <table style={{ marginTop: 8 }}>
          <thead>
            <tr>
              <th>{t('remittances.return.columns.reference')}</th>
              <th>{t('remittances.return.columns.favored')}</th>
              <th style={{ textAlign: 'right' }}>{t('remittances.return.columns.amount')}</th>
              <th>{t('remittances.return.columns.status')}</th>
              <th>{t('remittances.return.columns.effectiveDate')}</th>
              <th>{t('remittances.return.columns.bankRef')}</th>
              <th>{t('remittances.return.columns.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {remittance.lines.map((line) => {
              const lineStatus = line.status as 'Pending' | 'Paid' | 'Returned';
              const isPending = lineStatus === 'Pending';
              return (
                <tr key={line.id}>
                  <td>{line.reference}</td>
                  <td>{line.favoredName}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(line.amount)}
                  </td>
                  <td>
                    <RemittanceLineStatusBadge s={lineStatus} />
                  </td>
                  <td>{line.effectiveDate ? fmtDate(line.effectiveDate) : '—'}</td>
                  <td>{line.bankReference ?? '—'}</td>
                  <td>
                    {isPending && canWrite && (
                      <div style={{ display: 'flex', gap: 6 }}>
                        <Button
                          style={{ padding: '2px 8px', fontSize: 12 }}
                          disabled={confirmPaid.isPending}
                          onClick={() => confirmPaid.mutate({ id: remittance.id, lineId: line.id })}
                        >
                          {t('remittances.return.confirmPaid')}
                        </Button>
                        <Button
                          style={{ padding: '2px 8px', fontSize: 12 }}
                          disabled={markReturned.isPending}
                          onClick={() => setReturnLineId(line.id)}
                        >
                          {t('remittances.return.returnLine')}
                        </Button>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {returnLineId && (
        <ReturnReasonDialog
          busy={markReturned.isPending}
          onConfirm={(reason) => markReturned.mutate({ id: remittance.id, lineId: returnLineId, reason })}
          onClose={() => setReturnLineId(null)}
        />
      )}
    </div>
  );
}

/* ============ Remittance detail modal ============ */
function RemittanceDetailModal({
  remittanceId,
  onClose,
  isDev,
}: {
  remittanceId: string;
  onClose: () => void;
  isDev?: boolean;
}) {
  const { t } = useTranslation();
  const query = useRemittance(remittanceId);

  return (
    <Modal
      title={t('remittances.title')}
      onClose={onClose}
      footer={
        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button onClick={onClose}>{t('remittances.cancel')}</Button>
        </div>
      }
    >
      {query.isLoading && <div className='help'>{t('remittances.loading')}</div>}
      {query.data && <RemittanceDetail remittance={query.data} isDev={isDev} />}
    </Modal>
  );
}

/* ============ List row ============ */
function RemittanceRow({ item, onDetail }: { item: RemittanceListItem; onDetail: (id: string) => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  // The .REM download endpoint requires the Bearer token, which a plain `<a href download>` navigation
  // cannot send (→ 401). Fetch it with auth and trigger a blob download instead.
  const handleDownload = async () => {
    try {
      await downloadWithAuth(api.remittanceFileUrl(item.id), `remessa-${item.sequenceNumber}.rem`);
    } catch {
      toast.error(t('remittances.downloadError'));
    }
  };
  return (
    <tr>
      <td>{item.sequenceNumber}</td>
      <td>{fmtDateTime(item.generatedAt)}</td>
      <td>{item.lineCount}</td>
      <td className='amount-cell' style={{ textAlign: 'right' }}>
        {brlCents(item.totalAmount)}
      </td>
      <td>
        <RemittanceStatusBadge status={item.status} />
      </td>
      <td>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          {item.status === 'Generated' && (
            <button
              type='button'
              className='btn btn-ghost'
              style={{ padding: '4px 8px' }}
              onClick={() => void handleDownload()}
            >
              <Download size={14} style={{ marginRight: 4 }} />
              {t('remittances.download')}
            </button>
          )}
          <Button style={{ padding: '4px 8px' }} onClick={() => onDetail(item.id)}>
            {t('remittances.return.linesTitle')}
          </Button>
        </div>
      </td>
    </tr>
  );
}

/* ============ Page ============ */
export function RemessasPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [page, setPage] = useState(1);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);

  // Detect development mode to show the simulate-return link
  const isDev = import.meta.env.DEV;

  const list = useRemittances({ companyId: empresaId || undefined, page }, { enabled: !!empresaId });
  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  return (
    <>
      <PageHeader title={t('remittances.title')} sub={t('remittances.subtitle')}>
        <EmpresaPicker />
        {canWrite && (
          <Button variant='brand' icon={<FileText size={16} />} disabled={!empresa} onClick={() => setDialogOpen(true)}>
            {t('remittances.generate')}
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.empty.noCompanyTitle')}
          sub={t('remittances.empty.noCompanySub')}
        />
      ) : (
        <Panel title={t('remittances.title')}>
          {list.isLoading ? (
            <div className='help'>{t('remittances.loading')}</div>
          ) : items.length === 0 ? (
            <div className='empty-state' style={{ padding: 24 }}>
              <div className='t'>{t('remittances.empty.title')}</div>
              <div className='s'>{t('remittances.empty.sub')}</div>
            </div>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>{t('remittances.columns.sequence')}</th>
                  <th>{t('remittances.columns.date')}</th>
                  <th>{t('remittances.columns.lines')}</th>
                  <th style={{ textAlign: 'right' }}>{t('remittances.columns.total')}</th>
                  <th>{t('remittances.columns.status')}</th>
                  <th>{t('remittances.columns.actions')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <RemittanceRow key={item.id} item={item} onDetail={setDetailId} />
                ))}
              </tbody>
            </table>
          )}
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </Panel>
      )}

      {dialogOpen && empresaId && <GenerateDialog companyId={empresaId} onClose={() => setDialogOpen(false)} />}
      {detailId && <RemittanceDetailModal remittanceId={detailId} onClose={() => setDetailId(null)} isDev={isDev} />}
    </>
  );
}
