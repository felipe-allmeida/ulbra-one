import type { TFunction } from 'i18next';
import { type Requisition } from '../../api';
import { brlCents } from '../../lib';
import { previewInstallments } from '../schemas';

/** Plano de pagamento (sem a data): periodicidade · Nº parcelas, ou "Parcela única" quando não há plano.
 * O 1º vencimento é mostrado em campo próprio no card (não concatenado aqui). */
export function paymentPlanBaseText(req: Requisition, t: TFunction): string {
  // O plano autorado (front-load / complemento financeiro) é a fonte da verdade quando existe: o Nº de parcelas
  // pode ter sido informado direto nas parcelas (sem passar pelo campo paymentInstallmentsCount), então contar
  // req.installments evita o "Parcela única" contraditório com "N× de X" (que installmentValueText mostra).
  const authoredCount = req.installments.length;
  if (authoredCount >= 1) {
    if (authoredCount === 1) return t('requisitions.detail.singlePayment');
    const parts = [
      req.paymentFrequency ? t(`requisitions.paymentFrequency.${req.paymentFrequency}`) : null,
      t('requisitions.detail.installments', { count: authoredCount }),
    ].filter(Boolean);
    return parts.join(' · ');
  }

  const planParts = [
    req.paymentFrequency ? t(`requisitions.paymentFrequency.${req.paymentFrequency}`) : null,
    req.paymentInstallmentsCount && req.paymentInstallmentsCount > 1
      ? t('requisitions.detail.installments', { count: req.paymentInstallmentsCount })
      : null,
  ].filter(Boolean);
  return planParts.length ? planParts.join(' · ') : t('requisitions.detail.singlePayment');
}

/** Valor de cada parcela (quando parcelado): usa o plano autorado se houver, senão o rateio exato
 * resto-na-última (previewInstallments). Resume "N× de X (última Y)" ou lista os valores se forem variados. */
export function installmentValueText(req: Requisition, t: TFunction): string | null {
  const installmentCount = req.installments.length > 0 ? req.installments.length : (req.paymentInstallmentsCount ?? 1);
  const installmentAmounts =
    req.installments.length > 0
      ? req.installments.map((i) => i.amount)
      : installmentCount > 1
        ? previewInstallments(req.estimateValue, installmentCount, req.firstDueDate, 1).map((p) => p.amount)
        : [];
  if (installmentAmounts.length <= 1) return null;
  const base = installmentAmounts[0];
  const last = installmentAmounts[installmentAmounts.length - 1];
  const uniformExceptLast = installmentAmounts.slice(0, -1).every((a) => a === base);
  if (!uniformExceptLast) return installmentAmounts.map((a) => brlCents(a)).join(' · ');
  return base === last
    ? t('requisitions.detail.installmentValueEqual', { count: installmentAmounts.length, value: brlCents(base) })
    : t('requisitions.detail.installmentValueLast', {
        count: installmentAmounts.length,
        value: brlCents(base),
        last: brlCents(last),
      });
}
