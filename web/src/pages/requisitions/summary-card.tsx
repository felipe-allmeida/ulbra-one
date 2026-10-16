import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { Check, Pencil, Sparkles } from 'lucide-react';
import { type Requisition, type VendorWithholdingRate } from '../../api';
import { useCostCenters, useProjects, useUnits, useVendor, useWithholdingPreview } from '../../queries';
import { brl, fmtPct } from '../../lib';
import { fmtDate, fmtDateTime } from '../../datetime';
import { P, usePermissions } from '../../permissions';
import { Button, Chip } from '../../ui';
import { RequisitionSlaChip, RequisitionStatusBadge } from './badges';
import { installmentValueText, paymentPlanBaseText } from './payment-plan';
import { competencyWindowText } from './competency';

/**
 * Unified requisition Data and Vendor card (redesign): header + Classificação + Financeiro +
 * budget Impact summary + (optional) Vendor block. Replaces the old `RequisitionDataPanel` and
 * `VendorCheckPanel`. `showVendor=false` for the Approver, who decides only with the requisition data.
 */
export function RequisitionSummaryCard({
  req,
  showVendor = true,
  headerExtra,
}: {
  req: Requisition;
  showVendor?: boolean;
  /** Conteúdo extra no cabeçalho, junto aos badges de status/SLA (ex.: avatares de presença) — só a
   *  tela de detalhe passa isto, as outras telas que reusam este card (decisão de triagem/aprovação)
   *  ficam como estavam. */
  headerExtra?: ReactNode;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const units = useUnits(req.companyId).data ?? [];
  const centros = useCostCenters(req.companyId).data ?? [];
  const projects = useProjects(req.companyId).data ?? [];

  const unitName = units.find((u) => u.id === req.unitId)?.name ?? req.unitId;
  const centroName = centros.find((c) => c.id === req.costCenterId)?.name ?? req.costCenterId;
  const projectName = req.projectId ? (projects.find((p) => p.id === req.projectId)?.name ?? req.projectId) : null;
  const competencyText = competencyWindowText(req, t('requisitions.competencyWindow.recurring'));
  const planBase = paymentPlanBaseText(req, t);
  const installmentValue = installmentValueText(req, t);

  return (
    <div className='rq-summary'>
      <div className='rq-summary-head'>
        <div className='rq-summary-heading'>
          <h2 className='rq-summary-title'>{t('requisitions.summary.cardTitle', { code: req.code })}</h2>
          <div className='rq-summary-meta'>
            <RequisitionStatusBadge s={req.status} />
            <RequisitionSlaChip req={req} />
            <span className='rq-summary-created'>
              {t('requisitions.detail.createdAt', { date: fmtDateTime(req.createdAt) })}
            </span>
            {headerExtra}
          </div>
        </div>
        <div className='rq-summary-amount'>
          <div className='rq-summary-amount-value'>{brl(req.estimateValue)}</div>
          <div className='rq-summary-amount-label'>{t('requisitions.summary.estimated')}</div>
        </div>
      </div>

      {/* Description as its own field (not only in the title) — we cannot omit this information. */}
      <div className='rq-summary-sec'>
        <div className='info-label'>{t('requisitions.detail.description')}</div>
        <p className='detail-desc-text'>{req.description}</p>
      </div>

      <div className='rq-summary-sec'>
        <div className='rq-seclabel rq-seclabel-brand'>{t('requisitions.summary.classification')}</div>
        <div className='rq-kv'>
          <KvCell label={t('requisitions.fields.unit')} value={unitName} />
          <KvCell label={t('requisitions.summary.costCenter')} value={centroName} />
          <KvCell label={t('requisitions.fields.type')} value={t(`requisitions.type.${req.type}`)} />
          <KvCell label={t('requisitions.fields.competency')} value={competencyText} />
          {projectName && <KvCell label={t('requisitions.fields.project')} value={projectName} />}
          {req.realizedValue > 0 && <KvCell label={t('requisitions.fields.realized')} value={brl(req.realizedValue)} />}
        </div>
      </div>

      <div className='rq-summary-sec'>
        <div className='rq-seclabel rq-seclabel-brand'>{t('requisitions.summary.financial')}</div>
        <div className='rq-kv'>
          <KvCell label={t('requisitions.detail.paymentPlan')} value={planBase} />
          {req.firstDueDate && <KvCell label={t('requisitions.summary.firstDue')} value={fmtDate(req.firstDueDate)} />}
          {installmentValue && <KvCell label={t('requisitions.detail.installmentValue')} value={installmentValue} />}
        </div>
      </div>

      {req.observation && (
        <div className='rq-summary-sec'>
          <div className='info-label'>{t('requisitions.fields.observation')}</div>
          <p className='detail-desc-text'>{req.observation}</p>
        </div>
      )}

      {showVendor && can(P.registryRead) && <VendorSection req={req} />}
    </div>
  );
}

/** label→value cell (muted label on top, bold value below) of the 2-column grid. */
function KvCell({ label, value }: { label: string; value: string }) {
  return (
    <div className='rq-kv-cell'>
      <div className='info-label'>{label}</div>
      <div className='rq-kv-value'>{value}</div>
    </div>
  );
}

/** A vendor is "new" if registered ≤ 30 days ago (createdAt comes from the backend after the OpenAPI regen). */
const NEW_VENDOR_DAYS = 30;
function isNewVendor(createdAt?: string | null): boolean {
  if (!createdAt) return false;
  const created = new Date(createdAt).getTime();
  if (Number.isNaN(created)) return false;
  return (Date.now() - created) / 86_400_000 <= NEW_VENDOR_DAYS;
}

/** Vendor block: name, CNPJ, contact, bank details and registered withholdings (chips). */
function VendorSection({ req }: { req: Requisition }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { can } = usePermissions();
  const vendor = useVendor(req.vendorId).data;
  const preview = useWithholdingPreview(
    {
      regime: vendor?.taxRegime ?? 'Unknown',
      companyId: req.companyId,
      cprb: vendor?.cprbForeseen,
      simplesAnexo: vendor?.simplesAnexo,
    },
    { enabled: !!vendor && vendor.taxRegime !== 'Unknown' },
  );
  if (!vendor) return null;

  const isNew = isNewVendor(vendor.createdAt);
  const bank = vendor.bankInfo;
  const bankLine = bank
    ? [
        bank.bankCode && t('requisitions.vendorCheck.bankLabel', { code: bank.bankCode }),
        bank.branch && t('requisitions.vendorCheck.branchLabel', { branch: bank.branch }),
        bank.account &&
          t('requisitions.vendorCheck.accountLabel', {
            account: `${bank.account}${bank.accountDigit ? `-${bank.accountDigit}` : ''}`,
          }),
      ]
        .filter(Boolean)
        .join(' · ')
    : '';
  const bankValue = bankLine
    ? bankLine
    : bank?.pixKey
      ? `${t('requisitions.vendorCheck.pixKey')}: ${bank.pixKey}`
      : t('requisitions.vendorCheck.noBank');

  const chips = buildRetentionChips(preview.data ?? [], vendor.withholdingRates ?? [], t);

  return (
    <div className='rq-summary-sec rq-vendor'>
      <div className='rq-vendor-head'>
        <div className='rq-seclabel rq-seclabel-brand'>{t('requisitions.summary.vendorTitle')}</div>
        {isNew && (
          <span className='badge-status badge-pendente rq-newvendor'>
            <Sparkles size={12} />
            {t('requisitions.summary.newVendor')}
          </span>
        )}
      </div>

      <div className='rq-vendor-name'>{vendor.name}</div>
      <div className='rq-vendor-doc'>{vendor.document}</div>

      <div className='rq-kv'>
        <KvCell label={t('requisitions.summary.contact')} value={vendor.email?.trim() || '—'} />
        <KvCell label={t('requisitions.summary.bank')} value={bankValue} />
      </div>

      <div className='rq-retentions'>
        <div className='info-label'>{t('requisitions.summary.withholdingsTitle')}</div>
        {chips.length === 0 ? (
          <p className='rq-impact-sub'>{t('requisitions.vendorCheck.noWithholdings')}</p>
        ) : (
          <div className='rq-chips'>
            {chips.map((c) =>
              c.ok ? (
                <Chip key={c.key} tone='auto' icon={<Check />}>
                  {c.label}
                </Chip>
              ) : (
                <Chip key={c.key} tone='warn'>
                  {c.label}
                </Chip>
              ),
            )}
          </div>
        )}
      </div>

      {/* Controllership can fix the vendor's registration during triage (requires registry:write). */}
      {can(P.registryWrite) && (
        <div className='rq-vendor-actions'>
          <Button icon={<Pencil />} onClick={() => nav(`/app/vendors/${req.vendorId}?fromRequisition=${req.id}`)}>
            {t('requisitions.vendorCheck.editVendor')}
          </Button>
        </div>
      )}
    </div>
  );
}

interface RetentionChip {
  key: string;
  label: string;
  ok: boolean;
}
interface PreviewItem {
  taxType: string;
  retains: boolean;
  rate: number | null;
  invoiceDependent: boolean;
}

/**
 * "Retenções cadastradas" chips: green with rate + ✓ when the rate is known and does not depend on the invoice;
 * amber "· conferir" when it depends on the invoice or there is no rate. Groups PIS+COFINS into a single chip when both.
 */
function buildRetentionChips(preview: PreviewItem[], ownRates: VendorWithholdingRate[], t: TFunction): RetentionChip[] {
  const ownRate = (tax: string) => ownRates.find((r) => r.taxType === tax && r.rate > 0)?.rate;
  const toChip = (item: PreviewItem): RetentionChip => {
    const rate = ownRate(item.taxType) ?? item.rate ?? undefined;
    const ok = !item.invoiceDependent && rate != null && rate > 0;
    const taxLabel = t(`withholdingTaxType.${item.taxType}`);
    return {
      key: item.taxType,
      label: ok ? `${taxLabel} ${fmtPct(rate)}` : `${taxLabel} · ${t('requisitions.summary.check')}`,
      ok,
    };
  };

  const retained = preview.filter((i) => i.retains);
  const byType = new Map(retained.map((i) => [i.taxType, i]));
  const chips: RetentionChip[] = [];
  const pis = byType.get('Pis');
  const cofins = byType.get('Cofins');
  // Priority order matching the reference: IRRF, ISS, PIS/COFINS, INSS, CSLL, others.
  const order = ['Irrf', 'Iss', 'Pis', 'Cofins', 'Inss', 'Csll'];
  const rank = (tax: string) => {
    const i = order.indexOf(tax);
    return i === -1 ? order.length : i;
  };

  for (const item of [...retained].sort((a, b) => rank(a.taxType) - rank(b.taxType))) {
    if ((item.taxType === 'Pis' || item.taxType === 'Cofins') && pis && cofins) {
      if (item.taxType === 'Cofins') continue; // grouped once, on the Pis entry
      const ok = toChip(pis).ok && toChip(cofins).ok;
      chips.push({
        key: 'PisCofins',
        label: ok
          ? t('requisitions.summary.pisCofins')
          : `${t('requisitions.summary.pisCofins')} · ${t('requisitions.summary.check')}`,
        ok,
      });
      continue;
    }
    chips.push(toChip(item));
  }
  return chips;
}
