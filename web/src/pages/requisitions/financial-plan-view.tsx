import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import { type Requisition } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';
import { useVendor } from '../../queries';
import { isConvenioBoleto } from './boleto';

/**
 * Read-only view of a front-loaded financial plan: emission date + the authored installments (due date,
 * value, payment method and boleto linha digitável). Rendered during triage/approval so the reviewer sees what
 * the requester already informed, without the editable subform. Returns null when there is no authored plan.
 */
export function FinancialPlanView({ req }: { req: Requisition }) {
  const { t } = useTranslation();
  // The requisition only carries the vendorId; the effective PIX key of a PIX installment is the per-payment
  // override (AEL-938) when set, else the vendor's registered key (fallback) — mirrors the CNAB resolution.
  // Only fetch the vendor when a PIX installment actually needs the fallback (no override), so boleto/TED-only
  // and empty plans don't trigger a needless GET.
  // A copia-e-cola installment carries its own destination (the payload), so it needs neither the override
  // nor the vendor's registered key — exclude it so an all-copia-e-cola plan doesn't trigger a needless GET.
  const needsVendorPix = req.installments.some(
    (i) => i.paymentMethod === 'Pix' && !i.pixKeyOverride?.trim() && !i.pixPayloadEmv?.trim(),
  );
  const vendorPixKey = useVendor(req.vendorId, needsVendorPix).data?.bankInfo?.pixKey ?? '';
  if (req.installments.length === 0) return null;
  const total = req.installments.reduce((s, i) => s + i.amount, 0);

  return (
    <Panel title={t('requisitions.financialPlan.title')}>
      <div className='fc-body'>
        <p className='fc-hint'>{t('requisitions.financialPlan.hint')}</p>

        <div className='rq-kv'>
          <div className='rq-kv-cell'>
            <div className='info-label'>{t('requisitions.financialComplement.issueDate')}</div>
            <div className='rq-kv-value'>{req.issueDate ? fmtDate(req.issueDate) : '—'}</div>
          </div>
          <div className='rq-kv-cell'>
            <div className='info-label'>{t('requisitions.financialPlan.total')}</div>
            <div className='rq-kv-value'>{brlCents(total)}</div>
          </div>
        </div>

        <div className='fc-rows'>
          <div className='fc-head'>
            <span>#</span>
            <span>{t('requisitions.financialComplement.dueDate')}</span>
            <span>{t('requisitions.financialComplement.amount')}</span>
            <span>{t('requisitions.financialComplement.method')}</span>
            <span />
          </div>
          {req.installments.map((i) => {
            // PIX: override wins over the vendor's registered key (an empty/blank override falls back to the vendor,
            // hence a ternary — `??` would keep the empty string). Only shown for a PIX installment that has a key.
            const override = i.pixKeyOverride?.trim();
            const effectivePixKey = override ? override : vendorPixKey;
            return (
              <Fragment key={i.sequence}>
                <div className='fc-row'>
                  <span className='fc-seq'>{i.sequence}</span>
                  <span>{fmtDate(i.dueDate)}</span>
                  <span>{brlCents(i.amount)}</span>
                  <span>{t(`payments.methods.${i.paymentMethod}`)}</span>
                  <span />
                </div>
                {i.boletoDigitableLine && (
                  <div className='fc-boleto'>
                    <span>
                      {t('requisitions.financialComplement.boleto')}: {i.boletoDigitableLine}
                    </span>
                    {isConvenioBoleto(i.boletoDigitableLine) && (
                      <span className='fc-boleto-badge'>{t('requisitions.financialComplement.convenioBadge')}</span>
                    )}
                  </div>
                )}
                {i.paymentMethod === 'Pix' && i.pixPayloadEmv?.trim() ? (
                  // Copia e cola takes precedence over the key: the payload is the payment destination.
                  <div className='fc-boleto'>
                    <span style={{ overflowWrap: 'anywhere' }}>
                      {t('requisitions.financialPlan.pixPayloadEmv')}: {i.pixPayloadEmv}
                    </span>
                    <span className='fc-boleto-badge'>{t('requisitions.financialPlan.pixPayloadEmvBadge')}</span>
                  </div>
                ) : i.paymentMethod === 'Pix' && effectivePixKey ? (
                  <div className='fc-boleto'>
                    <span>
                      {t('requisitions.financialPlan.pixKey')}: {effectivePixKey}
                    </span>
                    {override && (
                      <span className='fc-boleto-badge'>{t('requisitions.financialPlan.pixOverriddenBadge')}</span>
                    )}
                  </div>
                ) : null}
              </Fragment>
            );
          })}
        </div>
      </div>
    </Panel>
  );
}
