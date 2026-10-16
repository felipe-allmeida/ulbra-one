import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Landmark, FileText, Eye, Paperclip } from 'lucide-react';
import { usePayable, usePayableAccountingView, useConfirmTaxAnalysis } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { P, usePermissions } from '../../permissions';
import { Drawer, Button, LoadingState } from '../../ui';
import { WithholdingSection } from './withholding-section';
import { fileErrorKey } from '../../download';
import { previewAttachment } from './download';
import { VendorTaxCard } from './vendor-tax-card';
import { DocumentNumberField } from './document-number-field';
import { RequestFiscalDocumentButton } from './payable-stage-list';

/**
 * "Análise Tributária" — the tax reviewer validates the payable's withholdings (edits/adds/removes the withholding
 * types + rates), then releases it for payment (→ Scheduled + accounting incurral). Hard-gated on a registered NF.
 * The ledger account (accounting) and NF upload/document number (payables:write) are NOT here — the tax reviewer
 * reads the vendor tax profile and requests the NF from the requester ("Solicitar NF"), always available.
 */
export function TaxAnalysisDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = usePermissions();
  const canEditTax = can(P.payablesTax);
  const { data: p } = usePayable(payableId);
  const view = usePayableAccountingView(payableId);
  const v = view.data;
  const confirm = useConfirmTaxAnalysis();

  // Unsaved withholding edits (from WithholdingSection) block the release — otherwise "Liberar" would schedule
  // against the stale, still-saved withholdings while the edited values sit only in the form.
  const [withholdingsDirty, setWithholdingsDirty] = useState(false);

  const hasNf = v?.hasFiscalDocument ?? false;
  // A tax title always requires its NF before release — a requisition-sourced service/fixed-expense title, never an
  // Advance (Advance never gets RequireTaxAnalysis, so it never reaches PendingTaxAnalysis; its NF exemption lives
  // in the backend, where it actually applies).
  const confirmBlocked = !hasNf;
  // "Solicitar NF" targets the requester — tax titles are always requisition-sourced.
  const canRequestNf = !!p?.originRequisitionId;

  return (
    <Drawer
      open
      onClose={onClose}
      width={720}
      title={t('payables.taxAnalysis.title')}
      sub={v?.counterpartyName ?? p?.counterpartyName}
      footer={
        canEditTax ? (
          <Button
            variant='brand'
            icon={<Landmark />}
            disabled={confirmBlocked || withholdingsDirty || confirm.isPending}
            title={
              withholdingsDirty
                ? t('payables.taxAnalysis.releaseBlockedUnsaved')
                : confirmBlocked
                  ? t('payables.taxAnalysis.releaseBlockedNoNf')
                  : undefined
            }
            onClick={() =>
              confirm.mutate(
                { id: payableId },
                {
                  onSuccess: () => {
                    toast.success(t('payables.taxAnalysis.releaseSuccess'));
                    onClose();
                  },
                  onError: (e) => toast.error(getErrorMessage(e)),
                },
              )
            }
          >
            {t('payables.taxAnalysis.release')}
          </Button>
        ) : undefined
      }
    >
      {!p || !v ? (
        <LoadingState />
      ) : (
        <>
          {/* Vendor tax data — the profile that explains which withholdings applied (regime, CPRB, annex). */}
          <VendorTaxCard vendor={v.vendor} vendorId={p.vendorId} />

          {/* Document number — read-only here (filled by accounting/ingestion; payables:write). */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.documentNumber.sectionTitle')}</div>
            <DocumentNumberField payableId={payableId} value={p.documentNumber} editable={false} />
          </div>

          {/* Withholdings — the tax reviewer edits types + rates here (payables:tax). bare = no card wrapper. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.withholding.sectionTitle')}</div>
            <WithholdingSection payable={p} editable={canEditTax} bare onDirtyChange={setWithholdingsDirty} />
          </div>

          {/* NF — view attached NF + "Solicitar NF" (always available, even with attachments). No direct upload
              here: the requester provides it after being pinged. Required before releasing to payment. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.accounting.fiscalTitle')}</div>
            {!hasNf && (
              <div className='empty-state' style={{ padding: '20px 16px', marginBottom: 12 }}>
                <FileText />
                <div className='t'>{t('payables.accounting.fiscalEmptyTitle')}</div>
                <div className='s'>{t('payables.accounting.fiscalEmptySub')}</div>
              </div>
            )}
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
            {canRequestNf && <RequestFiscalDocumentButton payableId={p.id} />}
          </div>
        </>
      )}
    </Drawer>
  );
}
