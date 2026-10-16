import { useState } from 'react';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { WithholdingRule } from '../api';
import { getErrorMessage } from '../apiError';
import { useCompanies, useDeleteWithholdingRule, useWithholdingRules } from '../queries';
import { Button, ConfirmDialog, Drawer, onRowClick, PageHeader, Panel, SkeletonRows } from '../ui';
import { useToast } from '../toast';
import { fractionToPercent } from '../lib';
import { WithholdingRuleForm } from './withholding-rule-form';

/** Percent display for a stored fraction (0.11 → "11%"). */
const ratePercent = (fraction: number) => `${fractionToPercent(fraction)}%`;

/**
 * Regras de Retenção (Cadastros) — CRUD of the WithholdingRule set the auto-retention engine resolves against.
 * Group-wide by default; each rule pins optional dimensions (regime / CPRB / anexo / subitem / município / empresa).
 * Delete is a soft-delete; edit reuses the row object from the list cache (there is no GET-by-id endpoint).
 */
export function RegrasRetencaoPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const [drawer, setDrawer] = useState<null | { edit?: WithholdingRule }>(null);
  const [confirm, setConfirm] = useState<null | WithholdingRule>(null);

  const list = useWithholdingRules();
  const rules = list.data ?? [];
  const companies = useCompanies();
  const companyName = (id: string | null) =>
    id ? (companies.data?.find((c) => c.id === id)?.legalName ?? id) : t('retentionRules.allCompanies');

  const del = useDeleteWithholdingRule({
    onSuccess: () => {
      toast.success(t('retentionRules.deleted'));
      setConfirm(null);
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const fmtRate = (r: WithholdingRule) =>
    r.rateSource === 'FromNota' ? t('rateSource.FromNota') : r.rate != null ? ratePercent(r.rate) : '—';
  const yesNo = (v: boolean | null) => (v == null ? '—' : v ? t('retentionRules.yes') : t('retentionRules.no'));

  return (
    <>
      <PageHeader title={t('retentionRules.title')} sub={t('retentionRules.subtitle')}>
        <Button variant='brand' icon={<Plus />} onClick={() => setDrawer({})}>
          {t('retentionRules.new')}
        </Button>
      </PageHeader>

      <Panel title={t('retentionRules.count', { count: rules.length })}>
        <table>
          <thead>
            <tr>
              <th>{t('retentionRules.fields.taxType')}</th>
              <th>{t('retentionRules.fields.retains')}</th>
              <th>{t('retentionRules.fields.rate')}</th>
              <th>{t('retentionRules.fields.vendorRegime')}</th>
              <th>{t('retentionRules.fields.simplesAnexo')}</th>
              <th>{t('retentionRules.fields.cprb')}</th>
              <th>{t('retentionRules.fields.lc116Subitem')}</th>
              <th>{t('retentionRules.fields.company')}</th>
              <th className='td-action'></th>
            </tr>
          </thead>
          <tbody>
            {list.isLoading ? (
              <SkeletonRows rows={5} cols={9} />
            ) : list.error ? (
              <tr>
                <td colSpan={9} className='t-empty'>
                  {getErrorMessage(list.error)}{' '}
                  <button className='backlink' onClick={() => void list.refetch()}>
                    {t('retentionRules.retry')}
                  </button>
                </td>
              </tr>
            ) : rules.length === 0 ? (
              <tr>
                <td colSpan={9} className='t-empty'>
                  {t('retentionRules.empty.sub')}
                </td>
              </tr>
            ) : (
              rules.map((r) => (
                <tr key={r.id} className='row-click' onClick={onRowClick(() => setDrawer({ edit: r }))}>
                  <td>{t(`withholdingTaxType.${r.taxType}`)}</td>
                  <td>{r.retains ? t('retentionRules.yes') : t('retentionRules.no')}</td>
                  <td className='t-mono'>{fmtRate(r)}</td>
                  <td>{r.vendorRegime ? t(`taxRegime.${r.vendorRegime}`) : '—'}</td>
                  <td>{r.simplesAnexo ? `Anexo ${r.simplesAnexo}` : '—'}</td>
                  <td>{yesNo(r.cprb)}</td>
                  <td className='t-mono'>{r.lc116Subitem?.trim() ? r.lc116Subitem : '—'}</td>
                  <td>{companyName(r.companyId)}</td>
                  <td className='td-action'>
                    <button
                      className='icon-btn'
                      onClick={() => setDrawer({ edit: r })}
                      aria-label={t('retentionRules.edit')}
                    >
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

      <Drawer
        open={!!drawer}
        title={drawer?.edit ? t('retentionRules.editTitle') : t('retentionRules.newTitle')}
        onClose={() => setDrawer(null)}
      >
        {drawer && (
          <WithholdingRuleForm initial={drawer.edit} onSaved={() => setDrawer(null)} onCancel={() => setDrawer(null)} />
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
