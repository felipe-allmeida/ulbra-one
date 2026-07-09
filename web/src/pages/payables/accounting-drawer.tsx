import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Calculator, FileText, ExternalLink } from 'lucide-react';
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
import { Drawer, Button, Field, Input, Combobox, FilePicker, type ComboboxItem } from '../../ui';
import { fmtDoc } from '../../format';
import { WithholdingSection } from './withholding-section';

/**
 * "Registro de Despesa" — the accountant reviews vendor tax data, retentions and the ledger account, attaches
 * the Nota Fiscal, then completes accounting (→ Scheduled). Gated: cannot complete accounting without a NF.
 * Flat sections (title + content, separated by a divider), matching the app's other drawers.
 */
export function AccountingDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const nav = useNavigate();
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
    xs.map((a) => ({ id: a.id, label: `${a.code} — ${a.name}` }));

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
          {/* Vendor tax data — read-only labeled fields; full record via "Ver fornecedor". */}
          <div className='drawer-section'>
            <div className='drawer-section-head'>
              <span className='section-title'>{t('payables.accounting.vendorTaxTitle')}</span>
              <Button icon={<ExternalLink />} onClick={() => void nav('/app/vendors')}>
                {t('payables.accounting.viewVendor')}
              </Button>
            </div>
            <div className='form-grid'>
              <ReadonlyField
                label={t('payables.accounting.fields.document')}
                value={v.vendor?.document ? fmtDoc(v.vendor.document) : '—'}
              />
              <ReadonlyField label={t('payables.accounting.fields.tradeName')} value={dash(v.vendor?.tradeName)} />
              <ReadonlyField
                label={t('payables.accounting.fields.taxRegime')}
                value={v.vendor ? t(`payables.accounting.taxRegime.${v.vendor.taxRegime}`) : '—'}
              />
              <ReadonlyField label={t('payables.accounting.fields.email')} value={dash(v.vendor?.email)} />
              <ReadonlyField label={t('payables.accounting.fields.phone')} value={dash(v.vendor?.phone)} />
            </div>
          </div>

          {/* Retentions — editable, same behavior as the other screens (bare = no Panel card in the drawer). */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.withholding.sectionTitle')}</div>
            <WithholdingSection payable={p} editable bare />
          </div>

          {/* Ledger account (conta contábil) — reclassify. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.accounting.reclassifyTitle')}</div>
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
          </div>

          {/* Nota Fiscal — attached as a document (no parse); required before completing accounting. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.accounting.fiscalTitle')}</div>
            {!hasNf && (
              <div className='empty-state' style={{ padding: '20px 16px', marginBottom: 12 }}>
                <FileText />
                <div className='t'>{t('payables.accounting.fiscalEmptyTitle')}</div>
                <div className='s'>{t('payables.accounting.fiscalEmptySub')}</div>
              </div>
            )}
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
          </div>
        </>
      )}
    </Drawer>
  );
}

/** Non-empty string or an em-dash placeholder (handles null/undefined/blank). */
const dash = (s?: string | null) => (s && s.trim() !== '' ? s : '—');

function ReadonlyField({ label, value }: { label: string; value: string }) {
  return (
    <Field label={label}>
      <Input value={value} readOnly />
    </Field>
  );
}
