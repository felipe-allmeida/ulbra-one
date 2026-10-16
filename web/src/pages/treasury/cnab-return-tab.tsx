import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, Download, FileText } from 'lucide-react';
import {
  api,
  type RemittanceCriticism,
  type RemittanceReturnPreview,
  type RemittanceReturnPreviewEntry,
} from '../../api';
import {
  useBankAccounts,
  useConfirmRemittanceLine,
  useMarkRemittanceLineReturned,
  useRemittanceReturnAutoConfirm,
  useRemittanceReturnCriticism,
  useRemittanceReturnHistory,
  useRemittanceReturnPreview,
} from '../../queries';
import { getErrorMessage } from '../../apiError';
import { downloadWithAuth, fileErrorKey } from '../../download';
import { useToast } from '../../toast';
import { Button, cx, EmptyState, FilePicker, onRowClick, Panel, Select, ToolbarField } from '../../ui';
import { useOriginNav } from '../../nav';
import { P, usePermissions } from '../../permissions';

const brl = (v: number | null | undefined) => (v ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });

/** Date + time — two returns for the same account often land on the same day. */
const dmyhm = (iso: string) =>
  new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

const dmy = (iso: string | null) => {
  if (!iso) return '—';
  const [y, m, d] = iso.split('-');
  return y && m && d ? `${d}/${m}/${y}` : iso;
};

/**
 * Badge por bucket de classificação — as mesmas classes do resto do app (`.badge-status`), não uma
 * paleta paralela. As cores hex que viviam aqui ignoravam os tokens de tema e quebravam no escuro.
 * `badge-neutral` em Unmatched é deliberado: nada é pedido ao operador, então nem quente nem calmo.
 */
const CLASS_BADGE: Record<string, string> = {
  Paid: 'badge-pago',
  Rejected: 'badge-rejeitado',
  Divergent: 'badge-pendente',
  Ambiguous: 'badge-pendente',
  Unmatched: 'badge-neutral',
  AlreadyProcessed: 'badge-aprovado',
};

type SubTab = 'reconciliation' | 'criticism';

/**
 * "Retorno CNAB" tab of Gestão Financeira. Two sub-tabs mirroring the ORCAMENTO legado:
 * - Conciliação de Retorno (MOVIMENTO): upload → auto-baixa the paid lines, then confirm/return the rest.
 * - Análise de Crítica (CRÍTICA): upload the bank's validation file → report accepted/rejected, NO baixa;
 *   the operator removes rejected titles from the batch (releases the installment back to the queue).
 */
export function CnabReturnTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const accounts = useBankAccounts(companyId).data ?? [];
  const [bankAccountId, setBankAccountId] = useState('');
  const [subTab, setSubTab] = useState<SubTab>('reconciliation');

  useEffect(() => {
    setBankAccountId((cur) => (accounts.find((a) => a.id === cur) ? cur : (accounts[0]?.id ?? '')));
  }, [accounts]);

  return (
    <div style={{ marginTop: 12 }}>
      <div className='toolbar' style={{ marginBottom: 12 }}>
        <ToolbarField label={t('remittances.return.cnabTab.payingAccount')}>
          <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)}>
            <option value=''>{t('remittances.return.cnabTab.payingAccountSelect')}</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.alias}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>

      <div className='seg' style={{ marginBottom: 12 }}>
        {(['reconciliation', 'criticism'] as SubTab[]).map((tab) => (
          <button
            key={tab}
            type='button'
            className={cx('segbtn', subTab === tab && 'on')}
            onClick={() => setSubTab(tab)}
          >
            {t(`remittances.return.cnabTab.subTab.${tab}`)}
          </button>
        ))}
      </div>

      {!bankAccountId ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.noAccountTitle')}
          sub={t('remittances.return.cnabTab.noAccountSub')}
        />
      ) : subTab === 'reconciliation' ? (
        // key resets the sub-tab's file/report state when the paying account changes.
        <ReconciliationSubTab key={bankAccountId} bankAccountId={bankAccountId} />
      ) : (
        <CriticismSubTab key={bankAccountId} bankAccountId={bankAccountId} />
      )}
    </div>
  );
}

/** MOVIMENTO: upload auto-baixa + manual confirm/return per line. */
function ReconciliationSubTab({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const openPayable = useOriginNav();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<RemittanceReturnPreview | null>(null);

  // Dry-run preview — used only to REFRESH the view after a manual per-line action (no re-baixa).
  const previewMut = useRemittanceReturnPreview({
    onSuccess: (data) => setPreview(data),
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  // Upload — auto-confirms the "Pago" lines (baixa) so the operator only handles the rest manually.
  const autoConfirmMut = useRemittanceReturnAutoConfirm({
    onSuccess: (data) => {
      setPreview(data);
      if (data.autoConfirmed > 0)
        toast.success(t('remittances.return.cnabTab.autoConfirmed', { count: data.autoConfirmed }));
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const runUpload = (f: File | null) => {
    if (!f || !bankAccountId) return;
    setFile(f);
    autoConfirmMut.mutate({ payingBankAccountId: bankAccountId, file: f });
  };
  // After a per-line action, re-run the (dry-run) preview so the row reflects its new state.
  const refresh = () => {
    if (file && bankAccountId) previewMut.mutate({ payingBankAccountId: bankAccountId, file });
  };

  const confirmPaid = useConfirmRemittanceLine({
    onSuccess: () => {
      toast.success(t('remittances.return.confirmPaidSuccess'));
      refresh();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  const markReturned = useMarkRemittanceLineReturned({
    onSuccess: () => {
      toast.success(t('remittances.return.returnLineSuccess'));
      refresh();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const busy = confirmPaid.isPending || markReturned.isPending || previewMut.isPending || autoConfirmMut.isPending;
  const actionsBlocked = !canWrite || !preview?.trailerOk || busy;

  const onConfirm = (e: RemittanceReturnPreviewEntry) => {
    if (!e.remittanceId || !e.lineId) return;
    confirmPaid.mutate({
      id: e.remittanceId,
      lineId: e.lineId,
      ret: { effectiveDate: e.effectiveDate, bankReference: e.bankReference, authCode: e.authCode },
    });
  };
  const onReturn = (e: RemittanceReturnPreviewEntry) => {
    if (!e.remittanceId || !e.lineId) return;
    markReturned.mutate({
      id: e.remittanceId,
      lineId: e.lineId,
      reason: e.motivo ?? e.note ?? t('remittances.return.cnabTab.defaultReturnReason'),
    });
  };

  const c = preview?.counters;

  return (
    <>
      {canWrite && (
        <div className='toolbar' style={{ marginBottom: 12 }}>
          <ToolbarField label={t('remittances.return.cnabTab.fileLabel')} className='cnab-return-file'>
            <FilePicker
              accept='.ret,.RET,.txt,.TXT,.csv,.CSV,text/plain,text/csv'
              placeholder={
                autoConfirmMut.isPending ? t('remittances.loading') : t('remittances.return.cnabTab.uploadPlaceholder')
              }
              fileName={file?.name}
              onSelect={runUpload}
            />
          </ToolbarField>
        </div>
      )}

      {!preview ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.emptyTitle')}
          sub={t('remittances.return.cnabTab.emptySub')}
        />
      ) : (
        <>
          {!preview.trailerOk && (
            <div
              className='form-notice warn'
              style={{ marginBottom: 8, display: 'flex', gap: 8, alignItems: 'center' }}
            >
              <AlertTriangle size={16} />
              <span>
                <strong>{t('remittances.return.cnabTab.trailerErrorTitle')}</strong> {preview.trailerMessages.join(' ')}
              </span>
            </div>
          )}
          {preview.payer && (
            <div className='form-notice' style={{ marginBottom: 8 }}>
              {t('remittances.return.cnabTab.payerLine', {
                document: preview.payer.document,
                agency: preview.payer.agency,
                account: preview.payer.account,
              })}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
            <Tile label={t('remittances.return.cnabTab.tiles.paid')} value={c?.paid} tone='var(--success)' />
            <Tile label={t('remittances.return.cnabTab.tiles.rejected')} value={c?.rejected} tone='var(--danger)' />
            <Tile label={t('remittances.return.cnabTab.tiles.divergent')} value={c?.divergent} tone='var(--warning)' />
            <Tile label={t('remittances.return.cnabTab.tiles.ambiguous')} value={c?.ambiguous} tone='var(--warning)' />
            <Tile
              label={t('remittances.return.cnabTab.tiles.unmatched')}
              value={c?.unmatched}
              tone='var(--muted-foreground)'
            />
          </div>

          <Panel title={t('remittances.return.cnabTab.entriesTitle', { total: c?.total ?? 0 })}>
            <div style={{ overflowX: 'auto' }}>
              <table className='rq-table'>
                <thead>
                  <tr>
                    <th>{t('remittances.return.cnabTab.col.title')}</th>
                    <th>{t('remittances.return.cnabTab.col.bank')}</th>
                    <th>{t('remittances.return.cnabTab.col.classification')}</th>
                    <th style={{ textAlign: 'right' }}>{t('remittances.return.cnabTab.col.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.entries.map((e, i) => {
                    const badge = CLASS_BADGE[e.classification] ?? CLASS_BADGE.Unmatched;
                    const matched = !!e.remittanceId && !!e.lineId;
                    const actionable = matched && e.classification !== 'AlreadyProcessed';
                    // A linha tem a anatomia das outras tabelas do app (favorecido, valor, ref), então
                    // clicar nela abre o título — quando existe um. Sem casamento não há para onde ir.
                    const open = e.payableId ? `/app/payables/${e.payableId}` : null;
                    return (
                      <tr
                        key={`${e.reference}-${i}`}
                        className={cx(open && 'row-click')}
                        onClick={open ? onRowClick(() => openPayable(open)) : undefined}
                      >
                        <td>
                          <div style={{ fontWeight: 600 }}>{e.favoredName ?? '—'}</div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                            {t('remittances.return.cnabTab.refLabel')}: {e.reference}
                            {e.favoredDocument ? ` · ${e.favoredDocument}` : ''}
                          </div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                            {t('remittances.return.cnabTab.nominal')}: {brl(e.valorNominal)}
                          </div>
                        </td>
                        <td>
                          <div style={{ fontWeight: 600 }}>{brl(e.valorEfetivado)}</div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>{dmy(e.effectiveDate)}</div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                            {e.motivo ?? e.occurrenceCodes.join(', ')}
                          </div>
                          {e.authCode && (
                            <div style={{ fontSize: 10, color: 'var(--muted-foreground)', fontFamily: 'monospace' }}>
                              {e.authCode}
                            </div>
                          )}
                        </td>
                        <td>
                          <span className={`badge-status ${badge}`}>
                            {t(`remittances.return.cnabTab.class.${e.classification}`)}
                          </span>
                          <div style={{ fontSize: 10, color: 'var(--muted-foreground)', marginTop: 2 }}>
                            {t(`remittances.return.cnabTab.method.${e.matchMethod}`)}
                          </div>
                        </td>
                        <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                          {!matched && <span className='help'>{t('remittances.return.cnabTab.noMatchNote')}</span>}
                          {actionable && (
                            <>
                              <Button variant='brand' onClick={() => onConfirm(e)} disabled={actionsBlocked}>
                                {t('remittances.return.confirmPaid')}
                              </Button>{' '}
                              <Button variant='danger-outline' onClick={() => onReturn(e)} disabled={actionsBlocked}>
                                {t('remittances.return.returnLine')}
                              </Button>
                            </>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </Panel>
        </>
      )}

      <ReturnHistoryPanel bankAccountId={bankAccountId} />
    </>
  );
}

/** CRÍTICA: read the bank's validation file → report accepted/rejected. Never applies baixa. */
function CriticismSubTab({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const canWrite = usePermissions().can(P.treasuryWrite);
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<RemittanceCriticism | null>(null);

  const criticismMut = useRemittanceReturnCriticism({
    onSuccess: (data) => setReport(data),
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  // "Remover do Lote" reuses mark-returned (releases the installment InRemittance → Open).
  const removeFromBatch = useMarkRemittanceLineReturned({
    onSuccess: () => {
      toast.success(t('remittances.return.cnabTab.criticism.removeSuccess'));
      if (file && bankAccountId) criticismMut.mutate({ payingBankAccountId: bankAccountId, file });
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const runUpload = (f: File | null) => {
    if (!f || !bankAccountId) return;
    setFile(f);
    criticismMut.mutate({ payingBankAccountId: bankAccountId, file: f });
  };

  const busy = criticismMut.isPending || removeFromBatch.isPending;

  const onRemove = (rec: RemittanceCriticism['records'][number]) => {
    if (!rec.remittanceId || !rec.lineId) return;
    removeFromBatch.mutate({
      id: rec.remittanceId,
      lineId: rec.lineId,
      reason: t('remittances.return.cnabTab.criticism.removeReason', {
        motivo: rec.motivo ?? rec.occurrenceCodes.join(', '),
      }),
    });
  };

  return (
    <>
      {canWrite && (
        <div className='toolbar' style={{ marginBottom: 12 }}>
          <ToolbarField label={t('remittances.return.cnabTab.criticism.fileLabel')} className='cnab-return-file'>
            <FilePicker
              accept='.ret,.RET,.txt,.TXT,text/plain'
              placeholder={
                criticismMut.isPending
                  ? t('remittances.loading')
                  : t('remittances.return.cnabTab.criticism.uploadPlaceholder')
              }
              fileName={file?.name}
              onSelect={runUpload}
            />
          </ToolbarField>
        </div>
      )}

      {!report ? (
        <EmptyState
          icon={<FileText />}
          title={t('remittances.return.cnabTab.criticism.emptyTitle')}
          sub={t('remittances.return.cnabTab.criticism.emptySub')}
        />
      ) : (
        <>
          {report.payer && (
            <div className='form-notice' style={{ marginBottom: 8 }}>
              {t('remittances.return.cnabTab.payerLine', {
                document: report.payer.document,
                agency: report.payer.agency,
                account: report.payer.account,
              })}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.total')}
              value={report.total}
              tone='var(--muted-foreground)'
            />
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.success')}
              value={report.success}
              tone='var(--success)'
            />
            <Tile
              label={t('remittances.return.cnabTab.criticism.tiles.rejection')}
              value={report.rejection}
              tone='var(--danger)'
            />
          </div>

          <Panel title={t('remittances.return.cnabTab.criticism.entriesTitle', { total: report.total })}>
            <div style={{ overflowX: 'auto' }}>
              <table className='rq-table'>
                <thead>
                  <tr>
                    <th>{t('remittances.return.cnabTab.criticism.col.record')}</th>
                    <th>{t('remittances.return.cnabTab.criticism.col.status')}</th>
                    <th style={{ textAlign: 'right' }}>{t('remittances.return.cnabTab.criticism.col.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.records.map((rec, i) => {
                    const badge = rec.isSuccess ? 'badge-pago' : 'badge-rejeitado';
                    const codes = rec.occurrenceCodes.join(', ');
                    const inBatch = !!rec.remittanceId && !!rec.lineId && rec.lineStatus === 'Pending';
                    const removed = rec.lineStatus === 'Returned';
                    return (
                      <tr key={`${rec.reference}-${i}`}>
                        <td>
                          <div style={{ fontWeight: 600 }}>{rec.favoredName ?? '—'}</div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                            {t('remittances.return.cnabTab.refLabel')}: {rec.reference}
                          </div>
                          <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                            {t('remittances.return.cnabTab.nominal')}: {brl(rec.amount)}
                          </div>
                        </td>
                        <td>
                          <span className={`badge-status ${badge}`}>
                            {rec.isSuccess
                              ? t('remittances.return.cnabTab.criticism.accepted')
                              : t('remittances.return.cnabTab.criticism.rejected')}
                            {codes ? ` (${codes})` : ''}
                          </span>
                          {!rec.isSuccess && rec.motivo && (
                            <div style={{ fontSize: 11, color: 'var(--danger)', marginTop: 2 }}>{rec.motivo}</div>
                          )}
                        </td>
                        <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                          {!rec.isSuccess &&
                            (removed ? (
                              <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--muted-foreground)' }}>
                                {t('remittances.return.cnabTab.criticism.removed')}
                              </span>
                            ) : (
                              inBatch && (
                                <Button
                                  variant='danger-outline'
                                  onClick={() => onRemove(rec)}
                                  disabled={!canWrite || busy}
                                >
                                  {t('remittances.return.cnabTab.criticism.removeFromBatch')}
                                </Button>
                              )
                            ))}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </Panel>
        </>
      )}
    </>
  );
}

/**
 * The .RET files already processed for this paying account. The upload stores the bank's own document, and
 * this is where it is read back — so it renders even before anything is uploaded in this session, and a file
 * that applied nothing (divergent trailer) shows up flagged rather than being hidden.
 */
function ReturnHistoryPanel({ bankAccountId }: { bankAccountId: string }) {
  const { t } = useTranslation();
  const [err, setErr] = useState<string | null>(null);
  const items = useRemittanceReturnHistory(bankAccountId).data?.items ?? [];

  return (
    <div style={{ marginTop: 12 }}>
      <Panel title={t('remittances.return.cnabTab.history.title')}>
        {items.length === 0 ? (
          <div className='empty-state' style={{ padding: 24 }}>
            <div className='s'>{t('remittances.return.cnabTab.history.empty')}</div>
          </div>
        ) : (
          <table className='rq-table'>
            <tbody>
              {items.map((r) => (
                <tr key={r.id}>
                  <td>
                    <div style={{ fontWeight: 600 }}>
                      {r.fileName}
                      {!r.applied && (
                        <span
                          style={{
                            marginLeft: 8,
                            background: 'var(--danger-subtle)',
                            color: 'var(--danger)',
                            padding: '2px 8px',
                            borderRadius: 999,
                            fontSize: 11,
                            fontWeight: 700,
                          }}
                        >
                          {t('remittances.return.cnabTab.history.notApplied')}
                        </span>
                      )}
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                      {t('remittances.return.cnabTab.history.processedBy', {
                        date: dmyhm(r.processedAt),
                        name: r.processedBy,
                      })}
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--muted-foreground)' }}>
                      {t('remittances.return.cnabTab.history.counters', {
                        paid: r.paid,
                        returned: r.returned,
                        divergent: r.divergent,
                        unmatched: r.unmatched,
                      })}
                    </div>
                  </td>
                  <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                    <Button
                      icon={<Download />}
                      onClick={() => {
                        setErr(null);
                        downloadWithAuth(api.remittanceReturnFileUrl(r.id), r.fileName).catch((e) =>
                          setErr(t(fileErrorKey(e, 'remittances.return.cnabTab.history.downloadError'))),
                        );
                      }}
                    >
                      {t('remittances.return.cnabTab.history.download')}
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {err && <p className='create-err'>{err}</p>}
      </Panel>
    </div>
  );
}

function Tile({ label, value, tone }: { label: string; value: number | undefined; tone: string }) {
  return (
    <div
      style={{
        minWidth: 96,
        border: '1px solid var(--border)',
        borderRadius: 12,
        padding: '8px 12px',
        background: 'var(--card)',
        textAlign: 'center',
      }}
    >
      <div style={{ fontSize: 20, fontWeight: 800, color: tone, lineHeight: 1 }}>{value ?? 0}</div>
      <div style={{ fontSize: 10, fontWeight: 700, color: 'var(--muted-foreground)', textTransform: 'uppercase' }}>
        {label}
      </div>
    </div>
  );
}
