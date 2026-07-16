import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { NotebookText, Download } from 'lucide-react';
import { useGeneralLedger, useCostCenters } from '../../queries';
import { Button, EmptyState, Pager } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { LedgerFilters } from './filters';
import { AccountSection } from './account-section';
import { SpreadsheetView } from './spreadsheet-view';
import { downloadGeneralLedgerExport } from './download';
import { DEFAULT_TAB, isTabKey, type LedgerTabKey } from './tabs';

/** Livro Razão — read-only, per account: opening balance + movements with a running balance. Two tabs
 *  over the same data: "Planilha" (a single flat table, the default) and "Por conta" (one panel per
 *  account). Paginated by account; filters by company (or consolidated), account group (root 1–5) OR a
 *  specific account, competence range and cost center. Active tab lives in the URL (`?tab=`). */
export function LivroRazaoPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [root, setRoot] = useState('');
  const [ledgerAccountId, setLedgerAccountId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [costCenterId, setCostCenterId] = useState('');
  const [page, setPage] = useState(1);
  const [params, setParams] = useSearchParams();

  const tabParam = params.get('tab');
  const tab: LedgerTabKey = isTabKey(tabParam) ? tabParam : DEFAULT_TAB;
  const setTab = (key: string) =>
    setParams((prev) => {
      prev.set('tab', key);
      return prev;
    });

  // Cost-center labels (code — name) for the rows; only populated when a company is selected.
  const costCenters = useCostCenters(companyId);
  const ccLabels = new Map<string, string>((costCenters.data ?? []).map((c) => [c.id, `${c.code} — ${c.name}`]));

  const filters = {
    companyId: companyId || undefined,
    root: root || undefined,
    ledgerAccountId: ledgerAccountId || undefined,
    from: from || undefined,
    to: to || undefined,
    costCenterId: costCenterId || undefined,
  };
  const query = useGeneralLedger({ ...filters, page, pageSize: 20 });
  const data = query.data;
  const accounts = data?.items ?? [];

  const tabs: StageTab[] = [
    { key: 'spreadsheet', label: t('accounting.generalLedger.tabs.spreadsheet') },
    { key: 'accounts', label: t('accounting.generalLedger.tabs.accounts') },
  ];

  return (
    <>
      <div className='razao-head'>
        <div>
          <h1 className='page-title'>{t('accounting.generalLedger.title')}</h1>
          <p className='page-sub'>{t('accounting.generalLedger.subtitle')}</p>
        </div>
        <div className='razao-toolbar'>
          <LedgerFilters
            companyId={companyId}
            root={root}
            ledgerAccountId={ledgerAccountId}
            from={from}
            to={to}
            costCenterId={costCenterId}
            onCompanyChange={(v) => {
              setCompanyId(v);
              setCostCenterId('');
              setPage(1);
            }}
            onRootChange={(v) => {
              setRoot(v);
              setLedgerAccountId('');
              setPage(1);
            }}
            onAccountChange={(v) => {
              setLedgerAccountId(v);
              if (v) setRoot('');
              setPage(1);
            }}
            onFromChange={(v) => {
              setFrom(v);
              setPage(1);
            }}
            onToChange={(v) => {
              setTo(v);
              setPage(1);
            }}
            onCostCenterChange={(v) => {
              setCostCenterId(v);
              setPage(1);
            }}
          />
          <Button icon={<Download />} onClick={() => downloadGeneralLedgerExport(filters)}>
            {t('accounting.generalLedger.export')}
          </Button>
        </div>
      </div>

      <StageTabBar tabs={tabs} active={tab} onChange={setTab} />

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : accounts.length === 0 ? (
        <EmptyState
          icon={<NotebookText />}
          title={t('accounting.generalLedger.empty.title')}
          sub={t('accounting.generalLedger.empty.sub')}
        />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {tab === 'spreadsheet' ? (
            <SpreadsheetView accounts={accounts} ccLabels={ccLabels} />
          ) : (
            accounts.map((a) => <AccountSection key={a.ledgerAccountId} account={a} ccLabels={ccLabels} />)
          )}
          {data && <Pager page={data.page} totalPages={data.totalPages} onPage={setPage} />}
        </div>
      )}
    </>
  );
}
