import { useTranslation } from 'react-i18next';
import { type RemittanceLineStatus } from '../../api';

export function RemittanceLineStatusBadge({ s }: { s: RemittanceLineStatus }) {
  const { t } = useTranslation();
  const cls: Record<RemittanceLineStatus, string> = {
    Pending: 'badge-pendente',
    Paid: 'badge-pago',
    Returned: 'badge-rejeitado',
  };
  return <span className={`badge-status ${cls[s]}`}>{t(`remittances.lineStatus.${s}`)}</span>;
}

/**
 * Label for a remittance's payment-form category (backend `RemittanceCategory` string, AEL-933). PIX/TED
 * are proper nouns; Boleto / Boleto Convênio come from i18n. `PixQrCode` is the PIX copia-e-cola EMV/BR Code
 * layout (forma 47), a distinct file from the plain-key PIX (forma 45) — labeled "PIX EMV" (AEL-963). Null
 * (pre-Onda-6 mixed files) shows a dash.
 */
export function remittanceCategoryLabel(category: string | null | undefined, t: (key: string) => string): string {
  switch (category) {
    case 'Pix':
      return 'PIX';
    case 'PixQrCode':
      return 'PIX EMV';
    case 'Ted':
      return 'TED';
    case 'BoletoBancario':
      return t('remittances.category.boleto');
    case 'Convenio':
      return t('remittances.category.convenio');
    default:
      return '—';
  }
}

/** Small badge naming the remittance's payment form. */
export function RemittanceCategoryBadge({ category }: { category: string | null | undefined }) {
  const { t } = useTranslation();
  if (!category) return <>—</>;
  return <span className='badge-status badge-remessa'>{remittanceCategoryLabel(category, t)}</span>;
}
