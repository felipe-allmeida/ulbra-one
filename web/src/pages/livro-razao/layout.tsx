import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { NotebookText, Download } from 'lucide-react';
import { useGeneralLedger, useCostCenters } from '../../queries';
import { Button, EmptyState, Pager } from '../../ui';
import { LedgerFilters } from './filters';
import { AccountSection } from './account-section';
import { downloadGeneralLedgerExport } from './download';

/** Livro Razão — read-only, per account: opening balance + movements with a running balance. Paginated by
 *  account; filters by company (or consolidated), account root (1–5), competence range and cost center. */
export function LivroRazaoPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [root, setRoot] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [costCenterId, setCostCenterId] = useState('');
  const [page, setPage] = useState(1);

  // Cost-center labels (code — name) for the rows; only populated when a company is selected.
  const costCenters = useCostCenters(companyId);
  const ccLabels = new Map<string, string>((costCenters.data ?? []).map((c) => [c.id, `${c.code} — ${c.name}`]));

  const filters = {
    companyId: companyId || undefined,
    root: root || undefined,
    from: from || undefined,
    to: to || undefined,
    costCenterId: costCenterId || undefined,
  };
  const query = useGeneralLedger({ ...filters, page, pageSize: 20 });
  const data = query.data;
  const accounts = data?.items ?? [];

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
          {accounts.map((a) => (
            <AccountSection key={a.ledgerAccountId} account={a} ccLabels={ccLabels} />
          ))}
          {data && <Pager page={data.page} totalPages={data.totalPages} onPage={setPage} />}
        </div>
      )}
    </>
  );
}
