import { useState } from 'react';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { SimplesAnexoValue, TaxRegime, WithholdingRule } from '../api';
import { getErrorMessage } from '../apiError';
import { useDeleteWithholdingRule, useWithholdingPreview, useWithholdingRules } from '../queries';
import { Button, ConfirmDialog, Drawer, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { fractionToPercent } from '../lib';
import { WithholdingRuleForm } from './withholding-rule-form';

const ratePercent = (fraction: number) => `${fractionToPercent(fraction)}%`;

/**
 * "Retenções" section on the vendor page: the WithholdingRules pinned to THIS vendor (which win over the system
 * defaults), plus a read-only preview of what the system would apply for the vendor's tax profile — so the user
 * sees exactly what an override replaces. The tax profile comes from the persisted vendor (not the live form),
 * so the preview reflects the saved cadastro and refreshes after each save.
 */
export function VendorWithholdingSection({
  vendorId,
  companyId,
  regime,
  cprb,
  simplesAnexo,
}: {
  vendorId: string;
  companyId: string;
  regime: TaxRegime;
  cprb: boolean | null;
  simplesAnexo: SimplesAnexoValue | null;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const [drawer, setDrawer] = useState<null | { edit?: WithholdingRule }>(null);
  const [confirm, setConfirm] = useState<null | WithholdingRule>(null);

  const list = useWithholdingRules(false, vendorId);
  const rules = list.data ?? [];
  const overridden = new Set(rules.map((r) => r.taxType));

  const preview = useWithholdingPreview(
    { regime, companyId: companyId || null, cprb, simplesAnexo },
    { enabled: !!regime },
  );

  const del = useDeleteWithholdingRule({
    onSuccess: () => {
      toast.success(t('retentionRules.deleted'));
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const fmtRate = (r: WithholdingRule) =>
    r.rateSource === 'FromNota' ? t('rateSource.FromNota') : r.rate != null ? ratePercent(r.rate) : '—';

  return (
    <>
      <div className='detail-head' style={{ marginTop: 24 }}>
        <div>
          <h2 className='section-title'>{t('registry.vendor.withholding.section')}</h2>
          <div className='detail-sub'>{t('registry.vendor.withholding.hint')}</div>
        </div>
        <Button variant='brand' icon={<Plus />} onClick={() => setDrawer({})}>
          {t('registry.vendor.withholding.new')}
        </Button>
      </div>

      <Panel title={t('registry.vendor.withholding.rulesCount', { count: rules.length })}>
        <table>
          <thead>
            <tr>
              <th>{t('retentionRules.fields.taxType')}</th>
              <th>{t('retentionRules.fields.retains')}</th>
              <th>{t('retentionRules.fields.rate')}</th>
              <th>{t('retentionRules.fields.lc116Subitem')}</th>
              <th>{t('retentionRules.fields.municipalityIbge')}</th>
              <th className='td-action'></th>
            </tr>
          </thead>
          <tbody>
            {list.isLoading ? (
              <SkeletonRows rows={3} cols={6} />
            ) : rules.length === 0 ? (
              <tr>
                <td colSpan={6} className='t-empty'>
                  {t('registry.vendor.withholding.empty')}
                </td>
              </tr>
            ) : (
              rules.map((r) => (
                <tr key={r.id} className='row-click' onClick={() => setDrawer({ edit: r })}>
                  <td>{t(`withholdingTaxType.${r.taxType}`)}</td>
                  <td>{r.retains ? t('retentionRules.yes') : t('retentionRules.no')}</td>
                  <td className='t-mono'>{fmtRate(r)}</td>
                  <td className='t-mono'>{r.lc116Subitem?.trim() ? r.lc116Subitem : '—'}</td>
                  <td className='t-mono'>{r.municipalityIbge?.trim() ? r.municipalityIbge : '—'}</td>
                  <td className='td-action' onClick={(e) => e.stopPropagation()}>
                    <button className='icon-btn' onClick={() => setDrawer({ edit: r })} aria-label={t('retentionRules.edit')}>
                      <Pencil />
                    </button>
                    <button className='icon-btn' onClick={() => setConfirm(r)} aria-label={t('retentionRules.delete')}>
                      <Trash2 />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Panel>

      <Panel title={t('registry.vendor.withholding.previewTitle')}>
        <table>
          <thead>
            <tr>
              <th>{t('retentionRules.fields.taxType')}</th>
              <th>{t('registry.vendor.withholding.systemDefault')}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {preview.isLoading ? (
              <SkeletonRows rows={6} cols={3} />
            ) : (preview.data ?? []).length === 0 ? (
              <tr>
                <td colSpan={3} className='t-empty'>
                  {t('registry.vendor.withholding.previewEmpty')}
                </td>
              </tr>
            ) : (
              (preview.data ?? []).map((item) => (
                <tr key={item.taxType}>
                  <td>{t(`withholdingTaxType.${item.taxType}`)}</td>
                  <td className='t-mono'>
                    {item.invoiceDependent
                      ? t('registry.vendor.withholding.issNota')
                      : !item.retains
                        ? t('retentionRules.no')
                        : item.rate != null
                          ? ratePercent(item.rate)
                          : '—'}
                  </td>
                  <td>
                    {overridden.has(item.taxType) && (
                      <span className='badge-status badge-auto'>{t('registry.vendor.withholding.overridden')}</span>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Panel>

      <Drawer
        open={!!drawer}
        title={drawer?.edit ? t('retentionRules.editTitle') : t('registry.vendor.withholding.new')}
        onClose={() => setDrawer(null)}
      >
        {drawer && (
          <WithholdingRuleForm
            initial={drawer.edit}
            vendorId={vendorId}
            onSaved={() => setDrawer(null)}
            onCancel={() => setDrawer(null)}
          />
        )}
      </Drawer>

      {confirm && (
        <ConfirmDialog
          title={t('retentionRules.deleteTitle')}
          message={t('retentionRules.deleteMessage')}
          danger
          busy={del.isPending}
          confirmLabel={t('retentionRules.delete')}
          onConfirm={() => del.mutate({ id: confirm.id })}
          onCancel={() => setConfirm(null)}
        />
      )}
    </>
  );
}
