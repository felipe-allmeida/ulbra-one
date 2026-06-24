import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Plus, Wallet } from 'lucide-react';
import { usePayables } from '../../queries';
import { useApp } from '../../store';
import { brl } from '../../lib';
import { Button, PageHeader, Pager, Panel, EmptyState } from '../../ui';
import { EmpresaPicker } from '../shared';
import { fmtData } from '../../datetime';
import { type TabKey, tabToParams, TABS } from './tabs';
import { useNames } from './use-names';
import { PayableStatusBadge, InstallmentStatusBadge } from './badges';

export function ContasPagarPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [tab, setTab] = useState<TabKey>('all');
  const { centroNome, categoriaNome } = useNames(empresaId);
  const filters = useMemo(() => tabToParams(tab), [tab]);
  const list = usePayables({ companyId: empresaId, page, ...filters }, { enabled: !!empresaId });
  const itens = list.data?.items ?? [];
  const totalPages = list.data?.totalPages ?? 1;

  useEffect(() => setPage(1), [empresaId]);
  // Synchronous reset: clear the page alongside the tab switch to avoid a request with an out-of-range page.
  const changeTab = (k: TabKey) => {
    setTab(k);
    setPage(1);
  };

  return (
    <>
      <PageHeader title={t('payables.title')} sub={t('payables.subtitle')}>
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          style={{ alignSelf: 'flex-end' }}
          onClick={() => nav('/app/contas-a-pagar/novo')}
        >
          {t('payables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Wallet />}
          title={t('payables.empty.noCompanyTitle')}
          sub={t('payables.empty.noCompanySub')}
        />
      ) : (
        <>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
            {TABS.map((k) => (
              <Button key={k} variant={tab === k ? 'brand' : 'default'} onClick={() => changeTab(k)}>
                {t(`payables.tabs.${k}`)}
              </Button>
            ))}
          </div>
          {itens.length === 0 ? (
            <EmptyState
              icon={<Wallet />}
              title={t('payables.empty.noItemsTitle')}
              sub={t('payables.empty.noItemsSub')}
            />
          ) : (
            <Panel title={t('payables.count', { count: list.data?.total ?? itens.length })}>
              <table>
                <thead>
                  <tr>
                    <th>{t('payables.columns.dueDate')}</th>
                    <th>{t('payables.columns.description')}</th>
                    <th>{t('payables.columns.vendor')}</th>
                    <th>{t('payables.columns.installment')}</th>
                    <th>{t('payables.columns.costCenter')}</th>
                    <th>{t('payables.columns.category')}</th>
                    <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
                    <th>{t('payables.columns.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {itens.map((l) => (
                    <tr
                      key={l.installmentId}
                      className='row-click'
                      onClick={() => nav(`/app/contas-a-pagar/${l.payableId}`)}
                    >
                      <td className='t-mono'>{fmtData(l.dueDate)}</td>
                      <td>{l.description}</td>
                      <td>{l.counterpartyName || '—'}</td>
                      <td className='t-mono'>
                        {l.sequence}/{l.installmentCount}
                      </td>
                      <td>{centroNome[l.costCenterId] ?? '—'}</td>
                      <td>{(l.categoryId ? categoriaNome[l.categoryId] : null) ?? '—'}</td>
                      <td className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(l.amount)}
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                          <PayableStatusBadge s={l.payableStatus} />
                          {l.payableStatus === 'Approved' && <InstallmentStatusBadge s={l.installmentStatus} />}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <Pager page={page} totalPages={totalPages} onPage={setPage} />
            </Panel>
          )}
        </>
      )}
    </>
  );
}
