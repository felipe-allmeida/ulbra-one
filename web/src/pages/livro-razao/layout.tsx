import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { NotebookText } from 'lucide-react';
import { useApp } from '../../store';
import { useGeneralLedger, useCostCenters } from '../../queries';
import { startOfCurrentMonth, endOfCurrentMonth } from '../../datetime';
import { EmptyState, Pager, LoadingState } from '../../ui';
import { LedgerFilters } from './filters';
import { SpreadsheetView } from './spreadsheet-view';
import { downloadGeneralLedgerExport } from './download';

/** Livro Razão — read-only, per account: opening balance + movements with a running balance. Rendered as
 *  a single flat spreadsheet grouped by account. Paginated by account; filters by account group (root 1–5)
 *  OR a specific account, competence range, unidade and cost center. The empresa/consolidado scope comes
 *  from the topbar selector (`useApp`), not from a page filter. */
export function LivroRazaoPage() {
  const { t } = useTranslation();
  const { scopeCompanyId, scopeReady } = useApp();
  // Seed the filters from the URL once on mount (e.g. drill-down from the Balancete: ?account=&from=&to=…).
  // The period falls back to the current month (default for every statement); the account combobox reflects
  // the seeded id automatically. Subsequent edits are plain local state — we don't write back to the URL.
  const [params] = useSearchParams();
  const [root, setRoot] = useState('');
  const [ledgerAccountId, setLedgerAccountId] = useState(() => params.get('account') ?? '');
  const [from, setFrom] = useState(() => params.get('from') ?? startOfCurrentMonth());
  const [to, setTo] = useState(() => params.get('to') ?? endOfCurrentMonth());
  const [costCenterId, setCostCenterId] = useState(() => params.get('costCenterId') ?? '');
  const [unitId, setUnitId] = useState(() => params.get('unitId') ?? '');
  const [page, setPage] = useState(1);

  // Cost-center labels (code — name) for the rows; only populated when a company is selected.
  const costCenters = useCostCenters(scopeCompanyId ?? '');
  const ccLabels = new Map<string, string>((costCenters.data ?? []).map((c) => [c.id, `${c.code} — ${c.name}`]));

  const filters = {
    companyId: scopeCompanyId,
    root: root || undefined,
    ledgerAccountId: ledgerAccountId || undefined,
    from: from || undefined,
    to: to || undefined,
    costCenterId: costCenterId || undefined,
    unitId: unitId || undefined,
  };
  const query = useGeneralLedger({ ...filters, page, pageSize: 20 }, { enabled: scopeReady });
  const data = query.data;
  const accounts = data?.items ?? [];

  return (
    <>
      <div className='razao-head'>
        <div>
          <h1 className='page-title'>{t('accounting.generalLedger.title')}</h1>
          <p className='page-sub'>{t('accounting.generalLedger.subtitle')}</p>
        </div>
        <LedgerFilters
          root={root}
          ledgerAccountId={ledgerAccountId}
          from={from}
          to={to}
          costCenterId={costCenterId}
          unitId={unitId}
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
          onUnitChange={(v) => {
            setUnitId(v);
            setPage(1);
          }}
          onExport={() => downloadGeneralLedgerExport(filters)}
        />
      </div>

      {!scopeReady || query.isLoading ? (
        <LoadingState />
      ) : accounts.length === 0 ? (
        <EmptyState
          icon={<NotebookText />}
          title={t('accounting.generalLedger.empty.title')}
          sub={t('accounting.generalLedger.empty.sub')}
        />
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <SpreadsheetView accounts={accounts} ccLabels={ccLabels} />
          {data && <Pager page={data.page} totalPages={data.totalPages} onPage={setPage} />}
        </div>
      )}
    </>
  );
}
