import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useBankAccounts, useCreateRemittance } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, Field, Modal, Select } from '../../ui';
import { remittanceCategoryLabel } from './badges';

// A paying account can ride the CNAB240 rail only if it is Santander (COMPE 033, in any
// entered form) and has a convênio code — mirrors the backend eligibility. Filtering the
// dropdown to eligible accounts keeps the operator out of the "banco não suportado" dead-end.
export function isSantander(bankCode?: string | null): boolean {
  return (bankCode ?? '').replace(/\D/g, '').replace(/^0+/, '') === '33';
}
export function isCnabEligible(a: { bankCode?: string | null; cnabAgreementCode?: string | null }): boolean {
  return isSantander(a.bankCode) && !!a.cnabAgreementCode?.trim();
}

/**
 * Generate-remittance dialog, shared by the Remessas tab (cutoff mode) and the payment queue's
 * bulk bar (selection mode). With `installmentIds` the server ignores the cutoff — the selection
 * is the intent — so the cutoff field is hidden.
 */
export function GenerateDialog({
  companyId,
  installmentIds,
  onClose,
  onGenerated,
}: {
  companyId: string;
  installmentIds?: string[];
  onClose: () => void;
  /** Fired only on success (before onClose) — lets the queue clear its checkbox selection. */
  onGenerated?: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const bankAccounts = useBankAccounts(companyId);
  const accounts = (bankAccounts.data ?? []).filter((a) => a.active && isCnabEligible(a));
  const [bankAccountId, setBankAccountId] = useState('');
  const [dueCutoff, setDueCutoff] = useState('');
  // Date the bank executes the batch (CNAB data_pagamento). Empty = today, server-side.
  const [paymentDate, setPaymentDate] = useState('');
  const today = new Date().toISOString().slice(0, 10);
  const selectionMode = !!installmentIds?.length;

  const create = useCreateRemittance({
    onSuccess: (data) => {
      // One file per payment method (AEL-933): name the formats so the operator sees exactly what was
      // generated (e.g. "1 PIX, 1 Boleto Convênio").
      const files = data.remittances.filter((r) => r.lines.length > 0);
      const included = files.reduce((sum, r) => sum + r.lines.length, 0);
      const formats = files.map((r) => remittanceCategoryLabel(r.category, t)).join(', ');
      const skipped = data.skipped.length;
      toast.success(t('remittances.success', { count: included, files: files.length, formats }));
      if (skipped > 0) {
        toast.success(t('remittances.skipped', { count: skipped }));
      }
      onGenerated?.();
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
      dueCutoff: selectionMode || !dueCutoff ? null : dueCutoff,
      installmentIds: selectionMode ? installmentIds : null,
      paymentDate: paymentDate || null,
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
            <p style={{ marginTop: 6, fontSize: 13, color: 'var(--muted-foreground, #6b7280)' }}>
              {t('remittances.generate_dialog.noEligibleAccount')}
            </p>
          )}
        </Field>
        {selectionMode ? (
          <p className='help'>
            {t('remittances.generate_dialog.selectedInfo', { count: installmentIds?.length ?? 0 })}
          </p>
        ) : (
          <Field label={t('remittances.generate_dialog.dueCutoff')}>
            <input type='date' className='input' value={dueCutoff} onChange={(e) => setDueCutoff(e.target.value)} />
          </Field>
        )}
        {/* Unlike the cutoff (a selection filter), the payment date is what the bank executes — it applies
            to both modes and to every file the run produces. */}
        <Field
          label={t('remittances.generate_dialog.paymentDate')}
          help={t('remittances.generate_dialog.paymentDateHelp')}
        >
          <input
            type='date'
            className='input'
            min={today}
            value={paymentDate}
            onChange={(e) => setPaymentDate(e.target.value)}
          />
        </Field>
      </form>
    </Modal>
  );
}
