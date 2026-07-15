import { useState, type ReactNode } from 'react';
import { Routes, Route, Navigate, Outlet, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Search, Menu } from 'lucide-react';
import { AppDataProvider } from './store';
import { auth } from './api';
import { SideNav } from './components/SideNav';
import { domainForPath, itemForPath } from './nav.config';
import { DashboardPage } from './pages/dashboard';
import { CentrosListPage, CentroDetailPage } from './pages/centros';
import { EstruturaPage } from './pages/estrutura';
import { LancamentosPage, NovoLancamentoPage, LancamentoDetailPage } from './pages/lancamentos';
import { ExpenseCategoriesPage } from './pages/expense-categories';
import { RegrasRetencaoPage } from './pages/regras-retencao';
import { BankAccountsPage } from './pages/bank-accounts';
import { ContabilPlanoPage } from './pages/contabil-plano';
import { ContabilLancamentosPage } from './pages/contabil-lancamentos';
import { ContabilBalancetePage } from './pages/contabil-balancete';
import { ContabilDrePage } from './pages/contabil-dre';
import { ContabilBalancoPage } from './pages/contabil-balanco';
import { LivroDiarioPage } from './pages/livro-diario';
import { RetencoesPage } from './pages/retencoes';
import { ObrigacoesPage } from './pages/fiscal/obrigacoes';
import { ContabilPendentesPage } from './pages/contabil-pendentes';
import { ContabilDespesasPage } from './pages/contabil-despesas';
import { ClassificacaoPage } from './pages/classificacao';
import { ConciliacaoPage } from './pages/conciliacao';
import { AgingPage } from './pages/aging';
import { CashFlowPage } from './pages/cash-flow';
import { FornecedoresPage, VendorDetailPage } from './pages/fornecedores';
import { ClientesPage } from './pages/clientes';
import { ColaboradoresPage } from './pages/colaboradores';
import { LandingPage } from './pages/landing';
import { LoginPage } from './pages/login';
import { RemessasPage } from './pages/remessas/list';
import { PayablesLayout, PayableDetailPage, NewPayablePage, EditPayablePage } from './pages/payables';
import { ReceivablesLayout, ReceivableDetailPage, NewReceivablePage, EditReceivablePage } from './pages/receivables';
import { PendenciesPage } from './pages/pendencies';

export default function App() {
  return (
    <Routes>
      <Route path='/' element={<LandingPage />} />
      <Route path='/login' element={<LoginPage />} />
      <Route
        path='/app'
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to='home' replace />} />
        <Route path='home' element={<DashboardPage />} />
        <Route path='payables' element={<PayablesLayout />} />
        <Route path='payables/new' element={<NewPayablePage />} />
        <Route path='payables/:id' element={<PayableDetailPage />} />
        <Route path='payables/:id/edit' element={<EditPayablePage />} />
        <Route path='receivables' element={<ReceivablesLayout />} />
        <Route path='receivables/new' element={<NewReceivablePage />} />
        <Route path='receivables/:id' element={<ReceivableDetailPage />} />
        <Route path='receivables/:id/edit' element={<EditReceivablePage />} />
        <Route path='expense-categories' element={<ExpenseCategoriesPage />} />
        <Route path='retention-rules' element={<RegrasRetencaoPage />} />
        <Route path='bank-accounts' element={<BankAccountsPage />} />
        <Route path='remittances' element={<RemessasPage />} />
        <Route path='accounting/chart-of-accounts' element={<ContabilPlanoPage />} />
        <Route path='accounting/journal-entries' element={<ContabilLancamentosPage />} />
        <Route path='accounting/journal-book' element={<LivroDiarioPage />} />
        <Route path='accounting/trial-balance' element={<ContabilBalancetePage />} />
        <Route path='accounting/income-statement' element={<ContabilDrePage />} />
        <Route path='accounting/balance-sheet' element={<ContabilBalancoPage />} />
        <Route path='accounting/withholdings' element={<RetencoesPage />} />
        <Route path='fiscal/obrigacoes' element={<ObrigacoesPage />} />
        <Route path='accounting/pending' element={<ContabilPendentesPage />} />
        <Route path='accounting/expense-recording' element={<ContabilDespesasPage />} />
        <Route path='accounting/classification' element={<ClassificacaoPage />} />
        <Route path='reconciliation' element={<ConciliacaoPage />} />
        <Route path='aging' element={<AgingPage />} />
        <Route path='cash-flow' element={<CashFlowPage />} />
        <Route path='accounts-payable' element={<LancamentosPage tipo='Payable' />} />
        <Route path='accounts-receivable' element={<LancamentosPage tipo='Receivable' />} />
        <Route path='pendencies' element={<PendenciesPage />} />
        {/* Old Aprovações screen removed — folded into Pendências. */}
        <Route path='approvals' element={<Navigate to='/app/pendencies' replace />} />
        <Route path='entries/new' element={<NovoLancamentoPage />} />
        <Route path='entries/:id' element={<LancamentoDetailPage />} />
        <Route path='accounting/cost-centers' element={<CentrosListPage />} />
        <Route path='accounting/cost-centers/new' element={<CentroDetailPage modo='novo' />} />
        <Route path='accounting/cost-centers/:id' element={<CentroDetailPage modo='detalhe' />} />
        <Route path='structure' element={<EstruturaPage />} />
        <Route path='vendors' element={<FornecedoresPage />} />
        <Route path='vendors/new' element={<VendorDetailPage mode='new' />} />
        <Route path='vendors/:id' element={<VendorDetailPage mode='detail' />} />
        <Route path='customers' element={<ClientesPage />} />
        <Route path='employees' element={<ColaboradoresPage />} />
        <Route path='*' element={<Navigate to='/app/home' replace />} />
      </Route>
      <Route path='*' element={<Navigate to='/' replace />} />
    </Routes>
  );
}

function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation();
  if (!auth.token) {
    return <Navigate to='/login' replace state={{ from: location.pathname + location.search }} />;
  }
  return <>{children}</>;
}

function AppLayout() {
  return (
    <AppDataProvider>
      <Shell>
        <Outlet />
      </Shell>
    </AppDataProvider>
  );
}

/* ===== SHELL (sidebar + topbar) ===== */
function Shell({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const [navOpen, setNavOpen] = useState(false);
  const { pathname } = useLocation();

  const domain = domainForPath(pathname);
  const item = itemForPath(pathname);
  // A directly-routable domain (e.g. home) has no sub-item — show a single crumb
  // instead of repeating the domain label ("Início / Início").
  const crumb = item ? (
    <>
      {t(domain.titleKey ?? domain.labelKey)} <b>/ {t(item.labelKey)}</b>
    </>
  ) : (
    <b>{t(domain.titleKey ?? domain.labelKey)}</b>
  );

  return (
    <>
      <SideNav open={navOpen} onClose={() => setNavOpen(false)} />

      <main className='main'>
        <header className='topbar'>
          <div className='topbar-menu'>
            <button className='icon-btn hamburger' onClick={() => setNavOpen(true)} aria-label='Menu'>
              <Menu />
            </button>
            <div className='crumb'>{crumb}</div>
          </div>
          <div className='top-search'>
            <Search />
            <input placeholder={t('nav.search')} />
          </div>
        </header>
        <div className='content'>{children}</div>
      </main>
    </>
  );
}
