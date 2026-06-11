import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowLeftRight, Check, Ban, Link2, Undo2, Upload } from 'lucide-react';
import { type BankTransaction, type MatchStatus } from '../api';
import { useApp } from '../store';
import {
  useBankAccounts,
  useBankTransactions,
  useConfirmMatch,
  useIgnoreTransaction,
  useImportStatement,
  useMatchTransaction,
  usePayables,
  useUnmatchTransaction,
} from '../queries';
import { brl } from '../lib';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { Button, Combobox, Drawer, EmptyState, Field, Pager, Panel, Select, ToolbarField } from '../ui';
import { EmpresaPicker } from './shared';

const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

const STATUS_FILTERS: MatchStatus[] = ['Suggested', 'Unmatched', 'Reconciled', 'Ignored'];

function StatusBadge({ s }: { s: MatchStatus }) {
  const { t } = useTranslation();
  const cls: Record<MatchStatus, string> = {
    Unmatched: 'badge-pendente',
    Suggested: 'badge-aprovado',
    Reconciled: 'badge-pago',
    Ignored: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`conciliacao.status.${s}`)}</span>;
}

/* ============ Manual installment picker (Drawer) ============ */
function ManualMatchDrawer({
  companyId,
  transaction,
  onClose,
}: {
  companyId: string;
  transaction: BankTransaction;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [installmentId, setInstallmentId] = useState<string | null>(null);

  // Open installments come from the company's Approved payables, flattened.
  const payables = usePayables({ companyId, status: 'Approved', pageSize: 100 }, { enabled: !!companyId });
  const items = useMemo(() => {
    const list = payables.data?.items ?? [];
    return list
      .filter((p) => p.installmentStatus === 'Open' || p.installmentStatus === 'PartiallyPaid')
      .map((p) => ({
        id: p.installmentId,
        label: `${p.counterpartyName || '—'} · ${fmtData(p.dueDate)}`,
        sub: `${p.sequence}/${p.installmentCount} · ${brl(p.amount)}`,
      }));
  }, [payables.data]);

  const match = useMatchTransaction({
    onSuccess: () => {
      toast.success(t('conciliacao.actions.matchManual'));
      onClose();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  return (
    <Drawer open title={t('conciliacao.matchDialog.title')} sub={t('conciliacao.matchDialog.sub')} onClose={onClose}>
      <Field label={t('conciliacao.matchDialog.installment')}>
        <Combobox
          value={installmentId}
          onChange={setInstallmentId}
          items={items}
          loading={payables.isLoading}
          placeholder={t('conciliacao.matchDialog.select')}
          searchPlaceholder={t('conciliacao.matchDialog.search')}
          emptyText={t('conciliacao.matchDialog.empty')}
        />
      </Field>
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button
          variant='brand'
          disabled={!installmentId || match.isPending}
          onClick={() => installmentId && match.mutate({ id: transaction.id, installmentId })}
        >
          {match.isPending ? t('conciliacao.matchDialog.matching') : t('conciliacao.matchDialog.confirm')}
        </Button>
        <Button onClick={onClose}>{t('forms.actions.cancel')}</Button>
      </div>
    </Drawer>
  );
}

/* ============ Per-row actions ============ */
function RowActions({ tx, onManual }: { tx: BankTransaction; onManual: (tx: BankTransaction) => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const confirm = useConfirmMatch({ onError: (e) => toast.error(getErrorMessage(e)) });
  const ignore = useIgnoreTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const unmatch = useUnmatchTransaction({ onError: (e) => toast.error(getErrorMessage(e)) });
  const busy = confirm.isPending || ignore.isPending || unmatch.isPending;

  return (
    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
      {tx.matchStatus === 'Suggested' && (
        <Button className='btn-approve' icon={<Check />} disabled={busy} onClick={() => confirm.mutate({ id: tx.id })}>
          {t('conciliacao.actions.confirm')}
        </Button>
      )}
      {(tx.matchStatus === 'Suggested' || tx.matchStatus === 'Unmatched') && (
        <Button icon={<Link2 />} disabled={busy} onClick={() => onManual(tx)}>
          {t('conciliacao.actions.matchManual')}
        </Button>
      )}
      {(tx.matchStatus === 'Suggested' || tx.matchStatus === 'Unmatched') && (
        <Button icon={<Ban />} disabled={busy} onClick={() => ignore.mutate({ id: tx.id })}>
          {t('conciliacao.actions.ignore')}
        </Button>
      )}
      {tx.matchStatus === 'Reconciled' && (
        <Button icon={<Undo2 />} disabled={busy} onClick={() => unmatch.mutate({ id: tx.id })}>
          {t('conciliacao.actions.unmatch')}
        </Button>
      )}
    </div>
  );
}

/* ============ Page ============ */
export function ConciliacaoPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const toast = useToast();

  const accounts = useBankAccounts(empresaId).data ?? [];
  const [bankAccountId, setBankAccountId] = useState('');
  const [status, setStatus] = useState<MatchStatus | ''>('');
  const [page, setPage] = useState(1);
  const [manualTx, setManualTx] = useState<BankTransaction | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  // Reset selection when the company changes (keep a valid bank account).
  useEffect(() => {
    setBankAccountId((cur) => (accounts.find((a) => a.id === cur) ? cur : (accounts[0]?.id ?? '')));
  }, [accounts]);
  useEffect(() => setPage(1), [bankAccountId, status]);

  const importStatement = useImportStatement({
    onSuccess: (imp) => {
      toast.success(
        t('conciliacao.summary.counts', {
          total: imp.transactionCount,
          newCount: imp.newCount,
          duplicateCount: imp.duplicateCount,
        }),
      );
      if (fileRef.current) fileRef.current.value = '';
    },
    onError: (e) => {
      toast.error(getErrorMessage(e));
      if (fileRef.current) fileRef.current.value = '';
    },
  });

  const list = useBankTransactions(
    { companyId: empresaId, bankAccountId, matchStatus: status || undefined, page },
    { enabled: !!empresaId && !!bankAccountId },
  );
  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  const onFile = (file: File | undefined) => {
    if (!file || !bankAccountId) return;
    importStatement.mutate({ bankAccountId, file });
  };

  return (
    <>
      <PageHeaderBar bankAccountId={bankAccountId} setBankAccountId={setBankAccountId} accounts={accounts} />

      {!empresa ? (
        <EmptyState
          icon={<ArrowLeftRight />}
          title={t('conciliacao.empty.noCompanyTitle')}
          sub={t('conciliacao.empty.noCompanySub')}
        />
      ) : (
        <>
          <Panel title={t('conciliacao.upload.label')}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
              <input
                ref={fileRef}
                type='file'
                accept='.ofx'
                className='input'
                disabled={!bankAccountId || importStatement.isPending}
                onChange={(e) => onFile(e.target.files?.[0])}
              />
              <span className='help' style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                <Upload style={{ width: 14, height: 14 }} />
                {bankAccountId ? t('conciliacao.upload.help') : t('conciliacao.upload.needBankAccount')}
              </span>
              {importStatement.isPending && <span className='help'>{t('conciliacao.upload.importing')}</span>}
            </div>
          </Panel>

          {!bankAccountId ? (
            <EmptyState
              icon={<ArrowLeftRight />}
              title={t('conciliacao.empty.noBankAccountTitle')}
              sub={t('conciliacao.empty.noBankAccountSub')}
            />
          ) : (
            <Panel
              title={t('conciliacao.title')}
              action={
                <ToolbarField label={t('conciliacao.filter.label')}>
                  <Select value={status} onChange={(e) => setStatus(e.target.value as MatchStatus | '')}>
                    <option value=''>{t('conciliacao.filter.all')}</option>
                    {STATUS_FILTERS.map((s) => (
                      <option key={s} value={s}>
                        {t(`conciliacao.status.${s}`)}
                      </option>
                    ))}
                  </Select>
                </ToolbarField>
              }
            >
              {items.length === 0 ? (
                <div className='empty-state' style={{ padding: 24 }}>
                  <div className='t'>{t('conciliacao.empty.noItemsTitle')}</div>
                  <div className='s'>{t('conciliacao.empty.noItemsSub')}</div>
                </div>
              ) : (
                <>
                  <table>
                    <thead>
                      <tr>
                        <th>{t('conciliacao.columns.date')}</th>
                        <th>{t('conciliacao.columns.description')}</th>
                        <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.amount')}</th>
                        <th>{t('conciliacao.columns.status')}</th>
                        <th style={{ textAlign: 'right' }}>{t('conciliacao.columns.actions')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {items.map((tx) => (
                        <tr key={tx.id}>
                          <td className='t-mono'>{fmtData(tx.date)}</td>
                          <td>
                            {tx.description}
                            {tx.matchStatus === 'Suggested' && <div className='help'>{t('conciliacao.suggested')}</div>}
                          </td>
                          <td className='amount-cell' style={{ textAlign: 'right' }}>
                            {brl(tx.amount)}
                          </td>
                          <td>
                            <StatusBadge s={tx.matchStatus} />
                          </td>
                          <td className='td-action'>
                            <RowActions tx={tx} onManual={setManualTx} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <Pager page={page} totalPages={totalPages} onPage={setPage} />
                </>
              )}
            </Panel>
          )}
        </>
      )}

      {manualTx && <ManualMatchDrawer companyId={empresaId} transaction={manualTx} onClose={() => setManualTx(null)} />}
    </>
  );
}

/* ============ Header (company + bank account pickers) ============ */
function PageHeaderBar({
  bankAccountId,
  setBankAccountId,
  accounts,
}: {
  bankAccountId: string;
  setBankAccountId: (id: string) => void;
  accounts: { id: string; alias: string }[];
}) {
  const { t } = useTranslation();
  return (
    <div className='page-head'>
      <div>
        <h1 className='page-title'>{t('conciliacao.title')}</h1>
        <p className='page-sub'>{t('conciliacao.subtitle')}</p>
      </div>
      <div className='toolbar'>
        <EmpresaPicker />
        <ToolbarField label={t('conciliacao.bankAccount')}>
          <Select value={bankAccountId} onChange={(e) => setBankAccountId(e.target.value)}>
            <option value=''>{t('conciliacao.bankAccountSelect')}</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.alias}
              </option>
            ))}
          </Select>
        </ToolbarField>
      </div>
    </div>
  );
}
