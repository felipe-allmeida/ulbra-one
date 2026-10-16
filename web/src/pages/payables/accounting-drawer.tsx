import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator, FileText, Eye, Paperclip } from 'lucide-react';
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
import { P, usePermissions } from '../../permissions';
import { Drawer, Button, Field, Combobox, FilePicker, LoadingState, type ComboboxItem } from '../../ui';
import { WithholdingSection } from './withholding-section';
import { fileErrorKey } from '../../download';
import { previewAttachment } from './download';
import { VendorTaxCard } from './vendor-tax-card';
import { DocumentNumberField } from './document-number-field';

/**
 * "Registro de Despesa" — the accountant reviews vendor tax data, retentions and the ledger account, attaches
 * the Nota Fiscal, then completes accounting (→ Scheduled). Gated: cannot complete accounting without a NF.
 * Flat sections (title + content, separated by a divider), matching the app's other drawers.
 */
export function AccountingDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = usePermissions();
  // Contabilizar/reclassificar/retenções/anexar NF são backed por payables:write no backend.
  const canWrite = can(P.payablesWrite);
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
  // Adiantamento (Advance) is exempt from the NF gate — it is paid before the NF exists.
  const nfExempt = v?.origin === 'Advance';
  const confirmBlocked = !hasNf && !nfExempt;
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
        canWrite ? (
          <Button
            variant='brand'
            icon={<Calculator />}
            disabled={confirmBlocked || confirm.isPending}
            title={confirmBlocked ? t('payables.accounting.confirmBlockedNoNf') : undefined}
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
        ) : undefined
      }
    >
      {!p || !v ? (
        <LoadingState />
      ) : (
        <>
          {/* Vendor tax data — shared card, so "Registro de Despesa" and "Classificar título" show it the same way. */}
          <VendorTaxCard vendor={v.vendor} vendorId={p.vendorId} />

          {/* Número do documento — editável; contas de requisição costumam chegar sem, avisa quando vazio. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.documentNumber.sectionTitle')}</div>
            <DocumentNumberField payableId={payableId} value={p.documentNumber} editable={canWrite} />
          </div>

          {/* Withholdings — read-only here: editing moved to the tax stage (payables:tax). bare = no card wrapper. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.withholding.sectionTitle')}</div>
            <WithholdingSection payable={p} editable={false} bare />
          </div>

          {/* Ledger account (conta contábil) — reclassify. Ação escondida sem payables:write. */}
          {canWrite && (
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
          )}

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
            {/* Saved NF document(s) — view inline without downloading. */}
            {p.attachments.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 12 }}>
                {p.attachments.map((att) => (
                  <div
                    key={att.id}
                    style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}
                  >
                    <span style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
                      <Paperclip style={{ width: 14, height: 14, flexShrink: 0 }} />
                      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {att.fileName}
                      </span>
                      <span
                        className='badge-status badge-doc'
                        style={{ fontSize: 10, flexShrink: 0 }}
                        title={t('attachments.kindLabel')}
                      >
                        {t(`attachments.kinds.${att.kind}`)}
                      </span>
                    </span>
                    <Button
                      className='btn-icon'
                      icon={<Eye />}
                      aria-label={t('attachments.view')}
                      title={t('attachments.view')}
                      onClick={() =>
                        void previewAttachment(p.id, att).catch((e) =>
                          toast.error(t(fileErrorKey(e, 'attachments.viewError'))),
                        )
                      }
                    />
                  </div>
                ))}
              </div>
            )}
            {canWrite && (
              <FilePicker
                accept='.xml,application/xml,text/xml,.pdf,application/pdf'
                fileName={file?.name}
                placeholder={t('common.noFileSelected')}
                onSelect={(f) => {
                  setFile(f);
                  if (!f) return;
                  // This picker is the Nota Fiscal itself (NF section) — mark FiscalDocument to clear the gate.
                  uploadAttachment.mutate(
                    { id: payableId, file: f, kind: 'FiscalDocument' },
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
            )}
          </div>
        </>
      )}
    </Drawer>
  );
}
