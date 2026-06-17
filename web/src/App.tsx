import { useState, type ReactNode } from 'react';
import { Routes, Route, Navigate, Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  LayoutDashboard,
  ArrowUpRight,
  ArrowDownLeft,
  Activity,
  Bell,
  ClipboardCheck,
  Target,
  FileText,
  FileBox,
  LineChart,
  ShieldCheck,
  Building2,
  Plug,
  Settings,
  Users,
  Calculator,
  Package,
  Search,
  Menu,
  Truck,
  UserSquare,
  Contact,
  Tag,
  Landmark,
  ArrowLeftRight,
  ListTree,
  Tags,
  CalendarClock,
  LogOut,
} from 'lucide-react';
import { AppDataProvider } from './store';
import { auth } from './api';
import { useMe } from './queries';
import { iniciais } from './lib';
import { DashboardPage } from './pages/dashboard';
import { CentrosListPage, CentroDetailPage } from './pages/centros';
import { EstruturaPage } from './pages/estrutura';
import { LancamentosPage, NovoLancamentoPage, LancamentoDetailPage, AprovacoesPage } from './pages/lancamentos';
import {
  ContasPagarPage,
  NovaContaPagarPage,
  ContaPagarDetailPage,
  EditarContaPagarPage,
} from './pages/contas-a-pagar';
import {
  ContasReceberPage,
  NovaContaReceberPage,
  ContaReceberDetailPage,
  EditarContaReceberPage,
} from './pages/contas-a-receber';
import { ExpenseCategoriesPage } from './pages/expense-categories';
import { BankAccountsPage } from './pages/bank-accounts';
import { PlanoDeContasPage } from './pages/plano-de-contas';
import { ClassificacaoPage } from './pages/classificacao';
import { ConciliacaoPage } from './pages/conciliacao';
import { AgingPage } from './pages/aging';
import { FornecedoresPage } from './pages/fornecedores';
import { ClientesPage } from './pages/clientes';
import { ColaboradoresPage } from './pages/colaboradores';
import { LandingPage } from './pages/landing';
import { LoginPage } from './pages/login';
import { NotificacoesPage } from './pages/notificacoes';

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
        <Route path='contas-a-pagar' element={<ContasPagarPage />} />
        <Route path='contas-a-pagar/novo' element={<NovaContaPagarPage />} />
        <Route path='contas-a-pagar/:id' element={<ContaPagarDetailPage />} />
        <Route path='contas-a-pagar/:id/editar' element={<EditarContaPagarPage />} />
        <Route path='contas-a-receber' element={<ContasReceberPage />} />
        <Route path='contas-a-receber/novo' element={<NovaContaReceberPage />} />
        <Route path='contas-a-receber/:id' element={<ContaReceberDetailPage />} />
        <Route path='contas-a-receber/:id/editar' element={<EditarContaReceberPage />} />
        <Route path='categorias-despesa' element={<ExpenseCategoriesPage />} />
        <Route path='contas-bancarias' element={<BankAccountsPage />} />
        <Route path='plano-de-contas' element={<PlanoDeContasPage />} />
        <Route path='classificacao' element={<ClassificacaoPage />} />
        <Route path='conciliacao' element={<ConciliacaoPage />} />
        <Route path='aging' element={<AgingPage />} />
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
        <Route path='notificacoes' element={<NotificacoesPage />} />
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
  const me = useMe().data ?? null;
  const nav = useNavigate();
  const { pathname } = useLocation();
  const go = (to: string) => {
    nav(to);
    setNavOpen(false);
  };
  const at = (p: string) => pathname.startsWith(`/app/${p}`);

  const logout = () => {
    auth.clear();
    nav('/');
  };

  const crumb = at('estrutura') ? (
    <>
      {t('nav.sections.structure')} <b>/ {t('nav.companiesUnits')}</b>
    </>
  ) : at('fornecedores') ? (
    <>
      {t('nav.sections.registry')} <b>/ {t('nav.vendors')}</b>
    </>
  ) : at('clientes') ? (
    <>
      {t('nav.sections.registry')} <b>/ {t('nav.customers')}</b>
    </>
  ) : at('colaboradores') ? (
    <>
      {t('nav.sections.registry')} <b>/ {t('nav.employees')}</b>
    </>
  ) : at('contas-a-pagar') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.accountsPayable')}</b>
    </>
  ) : at('categorias-despesa') ? (
    <>
      {t('nav.sections.registry')} <b>/ {t('nav.expenseCategories')}</b>
    </>
  ) : at('contas-bancarias') ? (
    <>
      {t('nav.sections.registry')} <b>/ {t('nav.bankAccounts')}</b>
    </>
  ) : at('plano-de-contas') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.chartAccounts')}</b>
    </>
  ) : at('classificacao') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.classification')}</b>
    </>
  ) : at('conciliacao') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.conciliacao')}</b>
    </>
  ) : at('aging') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.aging')}</b>
    </>
  ) : at('contas-pagar') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.accountsPayable')}</b>
    </>
  ) : at('contas-a-receber') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.accountsReceivable')}</b>
    </>
  ) : at('aprovacoes') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.approvals')}</b>
    </>
  ) : at('centros') ? (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.costCenters')}</b>
    </>
  ) : at('notificacoes') ? (
    <>
      {t('nav.sections.system')} <b>/ {t('nav.notifications')}</b>
    </>
  ) : (
    <>
      {t('nav.sections.finance')} <b>/ {t('nav.home')}</b>
    </>
  );

  return (
    <>
      {navOpen && <div className='sb-backdrop show' onClick={() => setNavOpen(false)} />}
      <aside className={`sidebar ${navOpen ? 'open' : ''}`}>
        <div className='user-header'>
          <div className='user-avatar-big'>{me ? iniciais(me.name ?? '') : 'U1'}</div>
          <div className='user-header-info'>
            <div className='user-header-name'>{me?.name ?? t('common.appName')}</div>
            <div className='user-header-role'>{me?.role ?? ''}</div>
          </div>
          <button className='icon-btn sb-logout' onClick={logout} aria-label={t('nav.logout')} title={t('nav.logout')}>
            <LogOut />
          </button>
        </div>
        <nav className='sb-nav'>
          <Section>{t('nav.sections.finance')}</Section>
          <NavItem
            icon={<LayoutDashboard />}
            label={t('nav.home')}
            active={at('inicio')}
            onClick={() => go('/app/inicio')}
          />
          <NavItem
            icon={<ArrowUpRight />}
            label={t('nav.accountsPayable')}
            active={at('contas-a-pagar')}
            onClick={() => go('/app/contas-a-pagar')}
          />
          <NavItem
            icon={<ArrowDownLeft />}
            label={t('nav.accountsReceivable')}
            active={at('contas-a-receber')}
            onClick={() => go('/app/contas-a-receber')}
          />
          <NavItem icon={<Activity />} label={t('nav.cashFlow')} />
          <NavItem
            icon={<ClipboardCheck />}
            label={t('nav.approvals')}
            active={at('aprovacoes')}
            onClick={() => go('/app/aprovacoes')}
          />
          <NavItem
            icon={<ArrowLeftRight />}
            label={t('nav.conciliacao')}
            active={at('conciliacao')}
            onClick={() => go('/app/conciliacao')}
          />
          <NavItem
            icon={<Target />}
            label={t('nav.costCenters')}
            active={at('centros')}
            onClick={() => go('/app/centros')}
          />
          <NavItem
            icon={<ListTree />}
            label={t('nav.chartAccounts')}
            active={at('plano-de-contas')}
            onClick={() => go('/app/plano-de-contas')}
          />
          <NavItem
            icon={<Tags />}
            label={t('nav.classification')}
            active={at('classificacao')}
            onClick={() => go('/app/classificacao')}
          />
          <NavItem
            icon={<CalendarClock />}
            label={t('nav.aging')}
            active={at('aging')}
            onClick={() => go('/app/aging')}
          />
          <NavItem icon={<FileText />} label={t('nav.reports')} />
          <NavItem icon={<FileBox />} label={t('nav.documents')} />
          <NavItem icon={<LineChart />} label={t('nav.projections')} />
          <NavItem icon={<ShieldCheck />} label={t('nav.audit')} />
          <Section>{t('nav.sections.registry')}</Section>
          <NavItem
            icon={<Truck />}
            label={t('nav.vendors')}
            active={at('fornecedores')}
            onClick={() => go('/app/fornecedores')}
          />
          <NavItem
            icon={<UserSquare />}
            label={t('nav.customers')}
            active={at('clientes')}
            onClick={() => go('/app/clientes')}
          />
          <NavItem
            icon={<Contact />}
            label={t('nav.employees')}
            active={at('colaboradores')}
            onClick={() => go('/app/colaboradores')}
          />
          <NavItem
            icon={<Tag />}
            label={t('nav.expenseCategories')}
            active={at('categorias-despesa')}
            onClick={() => go('/app/categorias-despesa')}
          />
          <NavItem
            icon={<Landmark />}
            label={t('nav.bankAccounts')}
            active={at('contas-bancarias')}
            onClick={() => go('/app/contas-bancarias')}
          />
          <Section>{t('nav.sections.structure')}</Section>
          <NavItem
            icon={<Building2 />}
            label={t('nav.companiesUnits')}
            active={at('estrutura')}
            onClick={() => go('/app/estrutura')}
          />
          <Section>{t('nav.sections.system')}</Section>
          <NavItem
            icon={<Bell />}
            label={t('nav.notifications')}
            active={at('notificacoes')}
            onClick={() => go('/app/notificacoes')}
          />
          <NavItem icon={<Plug />} label={t('nav.integrations')} />
          <NavItem icon={<Settings />} label={t('nav.settings')} />
          <Section>{t('nav.sections.modules')}</Section>
          <NavItem icon={<Users />} label={t('nav.hr')} soon />
          <NavItem icon={<Calculator />} label={t('nav.accounting')} soon />
          <NavItem icon={<Package />} label={t('nav.supplies')} soon />
        </nav>
      </aside>

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

const Section = ({ children }: { children: ReactNode }) => <div className='sb-section'>{children}</div>;
function NavItem({
  icon,
  label,
  active,
  soon,
  onClick,
}: {
  icon: ReactNode;
  label: string;
  active?: boolean;
  soon?: boolean;
  onClick?: () => void;
}) {
  const { t } = useTranslation();
  const disabled = !active && !onClick;
  return (
    <button
      className={`sb-item ${active ? 'active' : ''} ${disabled ? 'disabled' : ''}`}
      disabled={disabled}
      onClick={onClick}
    >
      {icon} {label} {soon && <span className='soon'>{t('common.soon')}</span>}
    </button>
  );
}
