import { useTranslation } from 'react-i18next';
import { ExternalLink } from 'lucide-react';
import { type PayableAccountingView } from '../../api';
import { Button, ReadonlyField } from '../../ui';
import { dash } from '../../lib';
import { useOriginNav } from '../../nav';
import { fmtDoc } from '../../format';

type VendorTaxBlock = PayableAccountingView['vendor'];

/** Read-only vendor tax data (fornecedor), shared by the accounting review drawers so the accountant validates
 * razão social / CNPJ / regime before acting. Sourced from the payable accounting-view. The vendor tax block
 * carries no id, so the "Ver fornecedor" target comes from the payable's `vendorId` (passed by the drawer). */
export function VendorTaxCard({ vendor, vendorId }: { vendor?: VendorTaxBlock; vendorId?: string | null }) {
  const { t } = useTranslation();
  const openVendor = useOriginNav();
  const cprbLabel = (b?: boolean | null) =>
    b == null ? '—' : b ? t('payables.accounting.cprbYes') : t('payables.accounting.cprbNo');

  return (
    <div className='drawer-section'>
      <div className='drawer-section-head'>
        <span className='section-title'>{t('payables.accounting.vendorTaxTitle')}</span>
        {vendorId && (
          <Button icon={<ExternalLink />} onClick={() => void openVendor(`/app/vendors/${vendorId}`)}>
            {t('payables.accounting.viewVendor')}
          </Button>
        )}
      </div>
      <div className='form-grid'>
        <ReadonlyField label={t('payables.accounting.fields.legalName')} value={dash(vendor?.name)} />
        <ReadonlyField
          label={t('payables.accounting.fields.document')}
          value={vendor?.document ? fmtDoc(vendor.document) : '—'}
        />
        <ReadonlyField label={t('payables.accounting.fields.tradeName')} value={dash(vendor?.tradeName)} />
        <ReadonlyField label={t('payables.accounting.fields.email')} value={dash(vendor?.email)} />
        <ReadonlyField
          label={t('payables.accounting.fields.taxRegime')}
          value={vendor ? t(`payables.accounting.taxRegime.${vendor.taxRegime}`) : '—'}
        />
        {vendor?.taxRegime === 'SimplesNacional' && (
          <ReadonlyField label={t('payables.accounting.fields.simplesAnexo')} value={dash(vendor?.simplesAnexo)} />
        )}
        <ReadonlyField label={t('payables.accounting.fields.cprb')} value={cprbLabel(vendor?.cprbForeseen)} />
        <ReadonlyField label={t('payables.accounting.fields.city')} value={dash(vendor?.city)} />
        <ReadonlyField label={t('payables.accounting.fields.uf')} value={dash(vendor?.state)} />
      </div>
    </div>
  );
}
