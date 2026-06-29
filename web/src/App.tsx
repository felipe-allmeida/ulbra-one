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
import { LancamentosPage, NovoLancamentoPage, LancamentoDetailPage, AprovacoesPage } from './pages/lancamentos';
import { ExpenseCategoriesPage } from './pages/expense-categories';
import { BankAccountsPage } from './pages/bank-accounts';
import { PlanoDeContasPage } from './pages/plano-de-contas';
import { ContabilPlanoPage } from './pages/contabil-plano';
import { ContabilLancamentosPage } from './pages/contabil-lancamentos';
import { ContabilBalancetePage } from './pages/contabil-balancete';
import { ContabilDrePage } from './pages/contabil-dre';
import { ContabilBalancoPage } from './pages/contabil-balanco';
import { ContabilPendentesPage } from './pages/contabil-pendentes';
import { ClassificacaoPage } from './pages/classificacao';
import { ConciliacaoPage } from './pages/conciliacao';
import { AgingPage } from './pages/aging';
import { CashFlowPage } from './pages/cash-flow';
import { FornecedoresPage } from './pages/fornecedores';
import { ClientesPage } from './pages/clientes';
import { ColaboradoresPage } from './pages/colaboradores';
import { LandingPage } from './pages/landing';
import { LoginPage } from './pages/login';
import { RemessasPage } from './pages/remessas/list';
import { PayablesLayout, PayableDetailPage, NewPayablePage, EditPayablePage } from './pages/payables';
import { ReceivablesLayout, ReceivableDetailPage, NewReceivablePage, EditReceivablePage } from './pages/receivables';

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
        <Route index element={<Navigate to='inicio' replace />} />
        <Route path='inicio' element={<DashboardPage />} />
        <Route path='payables' element={<PayablesLayout />} />
        <Route path='payables/novo' element={<NewPayablePage />} />
        <Route path='payables/:id' element={<PayableDetailPage />} />
        <Route path='payables/:id/editar' element={<EditPayablePage />} />
        <Route path='receivables' element={<ReceivablesLayout />} />
        <Route path='receivables/novo' element={<NewReceivablePage />} />
        <Route path='receivables/:id' element={<ReceivableDetailPage />} />
        <Route path='receivables/:id/editar' element={<EditReceivablePage />} />
        <Route path='categorias-despesa' element={<ExpenseCategoriesPage />} />
        <Route path='contas-bancarias' element={<BankAccountsPage />} />
        <Route path='remessas' element={<RemessasPage />} />
        <Route path='plano-de-contas' element={<PlanoDeContasPage />} />
        <Route path='contabil/plano-de-contas' element={<ContabilPlanoPage />} />
        <Route path='contabil/lancamentos' element={<ContabilLancamentosPage />} />
        <Route path='contabil/balancete' element={<ContabilBalancetePage />} />
        <Route path='contabil/dre' element={<ContabilDrePage />} />
        <Route path='contabil/balanco' element={<ContabilBalancoPage />} />
        <Route path='contabil/pendentes' element={<ContabilPendentesPage />} />
        <Route path='classificacao' element={<ClassificacaoPage />} />
        <Route path='conciliacao' element={<ConciliacaoPage />} />
        <Route path='aging' element={<AgingPage />} />
        <Route path='cash-flow' element={<CashFlowPage />} />
        <Route path='contas-pagar' element={<LancamentosPage tipo='Payable' />} />
        <Route path='contas-receber' element={<LancamentosPage tipo='Receivable' />} />
        <Route path='aprovacoes' element={<AprovacoesPage />} />
        <Route path='lancamentos/novo' element={<NovoLancamentoPage />} />
        <Route path='lancamentos/:id' element={<LancamentoDetailPage />} />
        <Route path='centros' element={<CentrosListPage />} />
        <Route path='centros/novo' element={<CentroDetailPage modo='novo' />} />
        <Route path='centros/:id' element={<CentroDetailPage modo='detalhe' />} />
        <Route path='estrutura' element={<EstruturaPage />} />
        <Route path='fornecedores' element={<FornecedoresPage />} />
        <Route path='clientes' element={<ClientesPage />} />
        <Route path='colaboradores' element={<ColaboradoresPage />} />
        <Route path='*' element={<Navigate to='inicio' replace />} />
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
  const crumb = (
    <>
      {t(domain.titleKey ?? domain.labelKey)} <b>/ {t(item?.labelKey ?? 'nav.home')}</b>
    </>
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
