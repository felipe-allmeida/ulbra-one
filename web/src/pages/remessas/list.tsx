import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Download, FileText } from 'lucide-react';
import { api } from '../../api';
import { useRemittances, useCreateRemittance, useBankAccounts, type RemittanceListItem } from '../../queries';
import { useApp } from '../../store';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { brlCents } from '../../lib';
import { Button, EmptyState, Field, Modal, PageHeader, Pager, Panel, Select } from '../../ui';
import { EmpresaPicker } from '../shared';
import { fmtDateTime } from '../../datetime';

/* ============ Status badge ============ */
function RemittanceStatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  const cls: Record<string, string> = {
    Generated: 'badge-aprovado',
    Cancelled: 'badge-rejeitado',
  };
  const label = t(`remittances.status.${status}`, { defaultValue: status });
  return <span className={`badge-status ${cls[status] ?? 'badge-pendente'}`}>{label}</span>;
}

/* ============ Generate remittance dialog ============ */
function GenerateDialog({ companyId, onClose }: { companyId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const bankAccounts = useBankAccounts(companyId);
  const accounts = (bankAccounts.data ?? []).filter((a) => a.active);
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
        </Field>
        <Field label={t('remittances.generate_dialog.dueCutoff')}>
          <input type='date' className='input' value={dueCutoff} onChange={(e) => setDueCutoff(e.target.value)} />
        </Field>
      </form>
    </Modal>
  );
}

/* ============ List row ============ */
function RemittanceRow({ item }: { item: RemittanceListItem }) {
  const { t } = useTranslation();
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
        {item.status === 'Generated' && (
          <a href={api.remittanceFileUrl(item.id)} download className='btn btn-ghost' style={{ padding: '4px 8px' }}>
            <Download size={14} style={{ marginRight: 4 }} />
            {t('remittances.download')}
          </a>
        )}
      </td>
    </tr>
  );
}

/* ============ Page ============ */
export function RemessasPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const [page, setPage] = useState(1);
  const [dialogOpen, setDialogOpen] = useState(false);

  const list = useRemittances({ companyId: empresaId || undefined, page }, { enabled: !!empresaId });
  const items = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  return (
    <>
      <PageHeader title={t('remittances.title')} sub={t('remittances.subtitle')}>
        <EmpresaPicker />
        <Button variant='brand' icon={<FileText size={16} />} disabled={!empresa} onClick={() => setDialogOpen(true)}>
          {t('remittances.generate')}
        </Button>
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
                  <RemittanceRow key={item.id} item={item} />
                ))}
              </tbody>
            </table>
          )}
          <Pager page={page} totalPages={totalPages} onPage={setPage} />
        </Panel>
      )}

      {dialogOpen && empresaId && <GenerateDialog companyId={empresaId} onClose={() => setDialogOpen(false)} />}
    </>
  );
}
