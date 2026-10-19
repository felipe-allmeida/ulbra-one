import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ExternalLink } from 'lucide-react';
import {
  usePayable,
  usePayableAccountingView,
  useLedgerAccounts,
  useClassifyPayable,
  useRequisition,
} from '../../queries';
import { Drawer, Button, Field, Combobox, ReadonlyField, LoadingState, type ComboboxItem } from '../../ui';
import { dash } from '../../lib';
import { RequestFiscalDocumentButton } from './payable-stage-list';
import { P, usePermissions } from '../../permissions';
import { useOriginNav } from '../../nav';
import { fmtDate } from '../../datetime';
import { useNames } from './use-names';
import { WithholdingSection } from './withholding-section';
import { InstallmentsTable } from './installments-table';
import { VendorTaxCard } from './vendor-tax-card';
import { PayableAttachmentsCard } from './payable-attachments-card';
import { DocumentNumberField } from './document-number-field';

/**
 * "Classificar título" — shows the same read-only review context as the Registro de Despesa drawer (vendor,
 * título, retentions, installments, attachments) before the accountant chooses the ledger account. The account
 * is prefilled from the AI suggestion; confirming routes through classify/reclassify.
 */
export function ClassificationDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.payablesWrite);
  const openDetail = useOriginNav();
  const { data: p } = usePayable(payableId);
  const view = usePayableAccountingView(payableId);
  // Only analytical, active accounts can receive a payable — the classify endpoint rejects synthetic
  // (parent) accounts with LedgerAccount.NotPostable, so don't offer them in the picker.
  const accounting = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data ?? [];
  const classify = useClassifyPayable();
  const sourceReq = useRequisition(p?.originRequisitionId ?? undefined).data;
  const { centroNome } = useNames(p?.companyId ?? '');

  const [accountingId, setAccountingId] = useState<string | null>(null);
  // Pre-fill from the applied account, else the AI suggestion, once the payable loads.
  useEffect(() => {
    if (p) setAccountingId(p.accountingAccountId ?? p.suggestedAccountingAccountId ?? null);
  }, [p]);

  const toItems = (xs: { id: string; code: string; name: string }[]): ComboboxItem[] =>
    xs.map((a) => ({ id: a.id, label: a.name, sub: a.code }));

  return (
    <Drawer
      open
      onClose={onClose}
      width={720}
      title={t('payables.classify.title')}
      sub={p?.counterpartyName}
      footer={
        // "Solicitar NF" moved here from the list row: with the row entering on click there is no expanded body
        // to host it, and this drawer already owns the fiscal side of the título (documento, aviso de NF).
        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          {p?.originRequisitionId && <RequestFiscalDocumentButton payableId={payableId} />}
          <Button
            variant='brand'
            disabled={classify.isPending || !accountingId}
            onClick={() =>
              classify.mutate({ id: payableId, body: { accountingAccountId: accountingId } }, { onSuccess: onClose })
            }
          >
            {t('payables.classify.confirm')}
          </Button>
        </div>
      }
    >
      {!p ? (
        <LoadingState />
      ) : (
        <>
          <VendorTaxCard vendor={view.data?.vendor} vendorId={p.vendorId} />

          {/* Dados do título criado (+ link para a requisição de origem). */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.classify.tituloTitle')}</div>
            <DocumentNumberField payableId={payableId} value={p.documentNumber} editable={canWrite} />
            <div className='form-grid' style={{ marginTop: 12 }}>
              <ReadonlyField
                label={t('payables.classify.fields.issueDate')}
                value={p.issueDate ? fmtDate(p.issueDate) : '—'}
              />
              <ReadonlyField
                label={t('payables.classify.fields.costCenter')}
                value={dash(centroNome[p.costCenterId])}
              />
              <ReadonlyField label={t('payables.classify.fields.description')} value={dash(p.description)} />
            </div>
            {p.originRequisitionId && (
              <div style={{ marginTop: 8 }}>
                <Button
                  icon={<ExternalLink />}
                  onClick={() => void openDetail(`/app/requisitions/${p.originRequisitionId}`)}
                >
                  {sourceReq
                    ? t('payables.classify.originRequisition', { code: sourceReq.code })
                    : t('payables.classify.originRequisitionGeneric')}
                </Button>
              </div>
            )}
          </div>

          {/* Valores e retenções (bruto/retido/líquido + impostos), somente leitura. */}
          <div className='drawer-section'>
            <div className='section-title'>{t('payables.withholding.sectionTitle')}</div>
            <WithholdingSection payable={p} editable={false} bare />
          </div>

          <InstallmentsTable payable={p} bare />

          {/* Todos os anexos, não só a NF. */}
          <PayableAttachmentsCard
            payableId={payableId}
            attachments={p.attachments}
            title={t('payables.classify.attachmentsTitle')}
            emptyLabel={t('payables.classify.attachmentsEmpty')}
          />

          <div className='drawer-section'>
            <div className='section-title'>{t('payables.classify.title')}</div>
            <Field label={t('payables.classify.accounting')}>
              <Combobox value={accountingId} onChange={setAccountingId} items={toItems(accounting)} />
            </Field>
            {p.classificationRationale && (
              <Field label={t('payables.classify.rationale')}>
                <div className='help'>{p.classificationRationale}</div>
              </Field>
            )}
          </div>
        </>
      )}
    </Drawer>
  );
}
