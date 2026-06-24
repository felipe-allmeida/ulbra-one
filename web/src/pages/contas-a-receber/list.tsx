import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Plus, HandCoins } from 'lucide-react';
import { useReceivables } from '../../queries';
import { useApp } from '../../store';
import { brl } from '../../lib';
import { Button, PageHeader, Pager, Panel, EmptyState } from '../../ui';
import { EmpresaPicker } from '../shared';
import { fmtData } from '../../datetime';
import { type TabKey, tabToParams, TABS } from './tabs';
import { useNames } from './use-names';
import { ReceivableStatusBadge, ReceivableInstallmentStatusBadge, OverdueBadge } from './badges';

export function ContasReceberPage() {
  const { empresa, empresaId } = useApp();
  const nav = useNavigate();
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [tab, setTab] = useState<TabKey>('all');
  const { centroNome } = useNames(empresaId);
  const filters = useMemo(() => tabToParams(tab), [tab]);
  const list = useReceivables({ companyId: empresaId, page, ...filters }, { enabled: !!empresaId });
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
      <PageHeader title={t('receivables.title')} sub={t('receivables.subtitle')}>
        <EmpresaPicker />
        <Button
          variant='brand'
          icon={<Plus />}
          disabled={!empresa}
          style={{ alignSelf: 'flex-end' }}
          onClick={() => nav('/app/contas-a-receber/novo')}
        >
          {t('receivables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<HandCoins />}
          title={t('receivables.empty.noCompanyTitle')}
          sub={t('receivables.empty.noCompanySub')}
        />
      ) : (
        <>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
            {TABS.map((k) => (
              <Button key={k} variant={tab === k ? 'brand' : 'default'} onClick={() => changeTab(k)}>
                {t(`receivables.tabs.${k}`)}
              </Button>
            ))}
          </div>
          {itens.length === 0 ? (
            <EmptyState
              icon={<HandCoins />}
              title={t('receivables.empty.noItemsTitle')}
              sub={t('receivables.empty.noItemsSub')}
            />
          ) : (
            <Panel title={t('receivables.count', { count: list.data?.total ?? itens.length })}>
              <table>
                <thead>
                  <tr>
                    <th>{t('receivables.columns.dueDate')}</th>
                    <th>{t('receivables.columns.description')}</th>
                    <th>{t('receivables.columns.payer')}</th>
                    <th>{t('receivables.columns.installment')}</th>
                    <th>{t('receivables.columns.costCenter')}</th>
                    <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
                    <th>{t('receivables.columns.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {itens.map((l) => (
                    <tr
                      key={l.installmentId}
                      className='row-click'
                      onClick={() => nav(`/app/contas-a-receber/${l.receivableId}`)}
                    >
                      <td className='t-mono' style={l.isOverdue ? { color: 'var(--danger, #dc2626)' } : undefined}>
                        {fmtData(l.dueDate)}
                      </td>
                      <td>{l.description}</td>
                      <td>{l.payerName || '—'}</td>
                      <td className='t-mono'>
                        {l.sequence}/{l.installmentCount}
                      </td>
                      <td>{centroNome[l.costCenterId] ?? '—'}</td>
                      <td className='amount-cell' style={{ textAlign: 'right' }}>
                        {brl(l.amount)}
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
                          <ReceivableStatusBadge s={l.receivableStatus} />
                          {l.receivableStatus === 'AwaitingReceipt' && (
                            <ReceivableInstallmentStatusBadge s={l.installmentStatus} />
                          )}
                          {l.isOverdue && <OverdueBadge />}
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
