import { type ReactNode, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import {
  usePayable,
  usePayableAccountingView,
  useConfirmAccounting,
  useLedgerAccounts,
  useClassifyPayable,
  useUploadPayableAttachment,
} from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Drawer, Button, Field, Combobox, Panel, FilePicker, type ComboboxItem } from '../../ui';
import { fmtDoc } from '../../format';
import { WithholdingSection } from './withholding-section';

/** "Registro de Despesa" — the accountant reviews vendor tax data, retentions and ledger account, registers
 * the Nota Fiscal, then completes accounting (→ Scheduled). Gated: cannot complete accounting without a registered NF. */
export function AccountingDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const { data: p } = usePayable(payableId);
  const view = usePayableAccountingView(payableId);
  const v = view.data;

  // Only analytical, active accounts can receive a payable (synthetic accounts are rejected server-side).
  const accounts = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data ?? [];
  const classify = useClassifyPayable();
  const confirm = useConfirmAccounting();
  const uploadAttachment = useUploadPayableAttachment();

  const [accountingId, setAccountingId] = useState<string | null>(null);
  const [file, setFile] = useState<File | null>(null);
  useEffect(() => {
    if (p) setAccountingId(p.accountingAccountId ?? null);
  }, [p]);

  const hasNf = v?.hasFiscalDocument ?? false;
  const toItems = (xs: { id: string; code: string; name: string }[]): ComboboxItem[] =>
    xs.map((a) => ({ id: a.id, label: a.name, sub: a.code }));

  return (
    <Drawer
      open
      onClose={onClose}
      width={720}
      title={t('payables.accounting.title')}
      sub={v?.counterpartyName ?? p?.counterpartyName}
      footer={
        <Button
          variant='brand'
          icon={<Calculator />}
          disabled={!hasNf || confirm.isPending}
          title={!hasNf ? t('payables.accounting.confirmBlockedNoNf') : undefined}
          onClick={() =>
            confirm.mutate(
              { id: payableId },
              {
                onSuccess: () => {
                  toast.success(t('payables.actions.confirmAccountingSuccess'));
                  onClose();
                },
                onError: (e) => toast.error(getErrorMessage(e)),
              },
            )
          }
        >
          {t('payables.actions.confirmAccounting')}
        </Button>
      }
    >
      {!p || !v ? (
        <div className='help'>{t('payables.loading')}</div>
      ) : (
        <>
          {/* Vendor tax data */}
          {v.vendor && (
            <Panel title={t('payables.accounting.vendorTaxTitle')}>
              <div className='info-grid'>
                <InfoField
                  label={t('payables.accounting.fields.document')}
                  value={v.vendor.document ? fmtDoc(v.vendor.document) : '—'}
                />
                {v.vendor.tradeName && (
                  <InfoField label={t('payables.accounting.fields.tradeName')} value={v.vendor.tradeName} />
                )}
                <InfoField
                  label={t('payables.accounting.fields.taxRegime')}
                  value={t(`payables.accounting.taxRegime.${v.vendor.taxRegime}`)}
                />
                {v.vendor.email && <InfoField label={t('payables.accounting.fields.email')} value={v.vendor.email} />}
                {v.vendor.phone && <InfoField label={t('payables.accounting.fields.phone')} value={v.vendor.phone} />}
                {v.documentNumber && (
                  <InfoField label={t('payables.accounting.fields.documentNumber')} value={v.documentNumber} />
                )}
              </div>
            </Panel>
          )}

          {/* Values (bruto/retido/líquido) + editable retentions — corrected here, in the accounting stage. */}
          <WithholdingSection payable={p} editable />

          {/* Reclassify the ledger account (conta contábil). */}
          <Panel title={t('payables.accounting.reclassifyTitle')}>
            <Field label={t('payables.accounting.fields.ledgerAccount')}>
              <Combobox value={accountingId} onChange={setAccountingId} items={toItems(accounts)} />
            </Field>
            <div style={{ marginTop: 12 }}>
              <Button
                disabled={classify.isPending || !accountingId}
                onClick={() =>
                  classify.mutate(
                    { id: payableId, body: { accountingAccountId: accountingId } },
                    {
                      onSuccess: () => {
                        toast.success(t('payables.accounting.reclassifySuccess'));
                        void view.refetch();
                      },
                      onError: (e) => toast.error(getErrorMessage(e)),
                    },
                  )
                }
              >
                {t('payables.accounting.reclassify')}
              </Button>
            </div>
          </Panel>

          {/* Nota Fiscal — required before completing accounting. Attached as a document (no parsing/OCR);
              shows up on the payable detail. */}
          <Panel title={t('payables.accounting.fiscalTitle')}>
            {!hasNf && <div className='create-err'>{t('payables.accounting.fiscalMissing')}</div>}
            <FilePicker
              accept='.xml,application/xml,text/xml,.pdf,application/pdf'
              fileName={file?.name}
              placeholder={t('common.noFileSelected')}
              onSelect={(f) => {
                setFile(f);
                if (!f) return;
                uploadAttachment.mutate(
                  { id: payableId, file: f },
                  {
                    onSuccess: () => {
                      toast.success(t('pendencies.fiscal.attachSuccess'));
                      void view.refetch();
                    },
                    onError: (e) => toast.error(getErrorMessage(e)),
                  },
                );
              }}
            />
          </Panel>
        </>
      )}
    </Drawer>
  );
}

function InfoField({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className='info-card'>
      <div className='info-label'>{label}</div>
      <div className='info-value' style={{ fontSize: 15 }}>
        {value}
      </div>
    </div>
  );
}
