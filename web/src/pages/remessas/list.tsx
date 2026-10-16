import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Download, ExternalLink, FileText } from 'lucide-react';
import { api } from '../../api';
import {
  useRemittances,
  useRemittance,
  useMarkRemittanceLineReturned,
  type RemittanceListItem,
  type Remittance,
} from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { brlCents } from '../../lib';
import { downloadWithAuth, fileErrorKey } from '../../download';
import { Button, Field, LoadingState, Modal, onRowClick, Pager, Panel } from '../../ui';
import { P, usePermissions } from '../../permissions';
import { fmtDateTime, fmtDate } from '../../datetime';
import { RemittanceLineStatusBadge, RemittanceCategoryBadge } from './badges';
import { GenerateDialog } from './generate-dialog';

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
  const nav = useNavigate();
  // Import return / confirm paid / return line are all backed by treasury:write.
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [returnLineId, setReturnLineId] = useState<string | null>(null);

  // Clicking a line opens its Payable. Origin is the deep link back into THIS remittance's modal
  // (?remittance=), so the Payable's "Voltar" reopens the títulos modal on the same remittance —
  // not just the tab (mirrors the ?remittance= link the payment queue's remittance chips use).
  const openPayable = (payableId: string) => {
    void nav(`/app/payables/${payableId}`, {
      state: { from: `/app/treasury?tab=remittances&remittance=${remittance.id}` },
    });
  };

  const markReturned = useMarkRemittanceLineReturned({
    onSuccess: () => {
      setReturnLineId(null);
      toast.success(t('remittances.return.returnLineSuccess'));
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  return (
    <div style={{ marginTop: 16 }}>
      {/* Forma de pagamento desta remessa (AEL-933) — um arquivo por forma. */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12, fontSize: 13 }}>
        <span style={{ color: 'var(--muted-foreground, #6b7280)' }}>{t('remittances.columns.category')}:</span>
        <RemittanceCategoryBadge category={remittance.category} />
      </div>
      {/* File conciliation now lives in the "Retorno CNAB" tab; only the dev simulator stays here. */}
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 12, flexWrap: 'wrap' }}>
        {isDev && (
          <button
            type='button'
            className='btn btn-ghost'
            style={{ display: 'flex', alignItems: 'center', gap: 6 }}
            onClick={() =>
              void downloadWithAuth(
                api.remittanceSimulateReturnUrl(remittance.id),
                `retorno-${remittance.sequenceNumber}.ret`,
              ).catch(() => toast.error(t('remittances.downloadError')))
            }
          >
            <ExternalLink size={14} />
            {t('remittances.return.simulateReturn')}
          </button>
        )}
      </div>

      {/* Lines table */}
      <div style={{ fontSize: 13 }}>
        <strong>{t('remittances.return.linesTitle')}</strong>
        <div style={{ overflowX: 'auto', marginTop: 8 }}>
          <table>
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
                  <tr key={line.id} className='row-click' onClick={onRowClick(() => openPayable(line.payableId))}>
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
                          {/* Payment baixa only happens via return-file conciliation ("Retorno CNAB" tab);
                            the only action here is removing the line from the remittance. */}
                          <Button
                            variant='danger-outline'
                            style={{ padding: '2px 8px', fontSize: 12 }}
                            disabled={markReturned.isPending}
                            onClick={() => setReturnLineId(line.id)}
                          >
                            {t('remittances.return.removeFromRemittance')}
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
      size='wide'
      onClose={onClose}
      footer={
        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <Button onClick={onClose}>{t('remittances.cancel')}</Button>
        </div>
      }
    >
      {query.isLoading && <LoadingState />}
      {query.data && <RemittanceDetail remittance={query.data} isDev={isDev} />}
    </Modal>
  );
}

/* ============ List row ============ */
function RemittanceRow({ item, onDetail }: { item: RemittanceListItem; onDetail: (id: string) => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  // The remittance download endpoint requires the Bearer token, which a plain `<a href download>` navigation
  // cannot send (→ 401). Fetch it with auth and trigger a blob download instead; the file name comes from the
  // server's Content-Disposition (the backend's canonical SANTANDER_REMESSA_<timestamp>.txt).
  const handleDownload = async () => {
    try {
      await downloadWithAuth(api.remittanceFileUrl(item.id));
    } catch (e) {
      toast.error(t(fileErrorKey(e, 'remittances.downloadError')));
    }
  };
  return (
    <tr>
      <td>{item.sequenceNumber}</td>
      <td>{fmtDateTime(item.generatedAt)}</td>
      <td>
        <RemittanceCategoryBadge category={item.category} />
      </td>
      <td>{item.lineCount}</td>
      <td className='amount-cell' style={{ textAlign: 'right' }}>
        {brlCents(item.totalAmount)}
      </td>
      <td>
        <RemittanceStatusBadge status={item.status} />
      </td>
      <td>
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
          <Button style={{ padding: '4px 8px' }} onClick={() => onDetail(item.id)}>
            {t('remittances.return.linesButton')}
          </Button>
          {item.status === 'Generated' && (
            // Icon-only download (right) keeps the actions column narrow enough to fit the panel; the label
            // survives as the tooltip / accessible name.
            <button
              type='button'
              className='btn btn-ghost'
              style={{ padding: '4px 8px' }}
              onClick={() => void handleDownload()}
              title={t('remittances.download')}
              aria-label={t('remittances.download')}
            >
              <Download size={14} />
            </button>
          )}
        </div>
      </td>
    </tr>
  );
}

/* ============ Tab (embedded in the treasury home) ============ */
/**
 * Body of the former standalone Remessas page, now a tab of the Gestão Financeira home.
 * `focusRemittanceId` implements the `?remittance=` deep link (used by the payment queue's
 * remittance chips): it opens the detail modal on mount; closing calls `onClearFocus`.
 */
export function RemittancesTab({
  companyId,
  focusRemittanceId,
  onClearFocus,
}: {
  companyId: string;
  focusRemittanceId?: string | null;
  onClearFocus?: () => void;
}) {
  const { t } = useTranslation();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [page, setPage] = useState(1);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);

  // Detect development mode to show the simulate-return link
  const isDev = import.meta.env.DEV;

  const list = useRemittances({ companyId, page }, { enabled: !!companyId });
  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  const openDetailId = detailId ?? focusRemittanceId ?? null;
  const closeDetail = () => {
    setDetailId(null);
    onClearFocus?.();
  };

  return (
    <>
      {canWrite && (
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
          <Button variant='brand' icon={<FileText size={16} />} onClick={() => setDialogOpen(true)}>
            {t('remittances.generate')}
          </Button>
        </div>
      )}

      <Panel title={t('remittances.title')}>
        {list.isLoading ? (
          <LoadingState />
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
                <th>{t('remittances.columns.category')}</th>
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

      {dialogOpen && <GenerateDialog companyId={companyId} onClose={() => setDialogOpen(false)} />}
      {openDetailId && <RemittanceDetailModal remittanceId={openDetailId} onClose={closeDetail} isDev={isDev} />}
    </>
  );
}
