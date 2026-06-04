import { useState, type ReactNode } from 'react';
import { Routes, Route, Navigate, useNavigate, useLocation } from 'react-router-dom';
import {
  LayoutDashboard, ArrowUpRight, ArrowDownLeft, Activity, ClipboardCheck, Target,
  FileText, FileBox, LineChart, ShieldCheck, Building2, Plug, Settings,
  Users, Calculator, Package, Search, Menu, Truck, UserSquare, Contact,
} from 'lucide-react';
import { AppDataProvider } from './store';
import { DashboardPage } from './pages/dashboard';
import { CentrosListPage, CentroDetailPage } from './pages/centros';
import { EstruturaPage } from './pages/estrutura';
import { LancamentosPage, NovoLancamentoPage, LancamentoDetailPage, AprovacoesPage } from './pages/lancamentos';
import { FornecedoresPage } from './pages/fornecedores';
import { ClientesPage } from './pages/clientes';
import { ColaboradoresPage } from './pages/colaboradores';

export default function App() {
  return (
    <AppDataProvider>
      <Shell>
        <Routes>
          <Route path="/" element={<Navigate to="/inicio" replace />} />
          <Route path="/inicio" element={<DashboardPage />} />
          <Route path="/contas-pagar" element={<LancamentosPage tipo="Payable" />} />
          <Route path="/contas-receber" element={<LancamentosPage tipo="Receivable" />} />
          <Route path="/aprovacoes" element={<AprovacoesPage />} />
          <Route path="/lancamentos/novo" element={<NovoLancamentoPage />} />
          <Route path="/lancamentos/:id" element={<LancamentoDetailPage />} />
          <Route path="/centros" element={<CentrosListPage />} />
          <Route path="/centros/novo" element={<CentroDetailPage modo="novo" />} />
          <Route path="/centros/:id" element={<CentroDetailPage modo="detalhe" />} />
          <Route path="/estrutura" element={<EstruturaPage />} />
          <Route path="/fornecedores" element={<FornecedoresPage />} />
          <Route path="/clientes" element={<ClientesPage />} />
          <Route path="/colaboradores" element={<ColaboradoresPage />} />
          <Route path="*" element={<Navigate to="/inicio" replace />} />
        </Routes>
      </Shell>
    </AppDataProvider>
  );
}

/* ===== SHELL (sidebar + topbar) ===== */
function Shell({ children }: { children: ReactNode }) {
  const [navOpen, setNavOpen] = useState(false);
  const nav = useNavigate();
  const { pathname } = useLocation();
  const go = (to: string) => { nav(to); setNavOpen(false); };

  const crumb = pathname.startsWith('/estrutura')
    ? <>Estrutura <b>/ Empresas e unidades</b></>
    : pathname.startsWith('/fornecedores')
      ? <>Cadastros <b>/ Fornecedores</b></>
      : pathname.startsWith('/clientes')
        ? <>Cadastros <b>/ Clientes</b></>
        : pathname.startsWith('/colaboradores')
          ? <>Cadastros <b>/ Colaboradores</b></>
          : pathname.startsWith('/inicio')
            ? <>Financeiro <b>/ Início</b></>
            : <>Financeiro <b>/ Centros de custo</b></>;

  return (
    <>
      {navOpen && <div className="sb-backdrop show" onClick={() => setNavOpen(false)} />}
      <aside className={`sidebar ${navOpen ? 'open' : ''}`}>
        <div className="user-header">
          <div className="user-avatar-big">FA</div>
          <div className="user-header-info">
            <div className="user-header-name">Felipe de Almeida</div>
            <div className="user-header-role">Tecnologia &amp; Inovação</div>
          </div>
        </div>
        <nav className="sb-nav">
          <Section>Financeiro</Section>
          <NavItem icon={<LayoutDashboard />} label="Início" active={pathname.startsWith('/inicio')} onClick={() => go('/inicio')} />
          <NavItem icon={<ArrowUpRight />} label="Contas a pagar" active={pathname.startsWith('/contas-pagar')} onClick={() => go('/contas-pagar')} />
          <NavItem icon={<ArrowDownLeft />} label="Contas a receber" active={pathname.startsWith('/contas-receber')} onClick={() => go('/contas-receber')} />
          <NavItem icon={<Activity />} label="Fluxo de caixa" />
          <NavItem icon={<ClipboardCheck />} label="Aprovações" active={pathname.startsWith('/aprovacoes')} onClick={() => go('/aprovacoes')} />
          {/* lançamentos detail/novo herdam o crumb de Financeiro */}
          <NavItem icon={<Target />} label="Centros de custo" active={pathname.startsWith('/centros')} onClick={() => go('/centros')} />
          <NavItem icon={<FileText />} label="Relatórios" />
          <NavItem icon={<FileBox />} label="Documentos" />
          <NavItem icon={<LineChart />} label="Projeções" />
          <NavItem icon={<ShieldCheck />} label="Auditoria" />
          <Section>Cadastros</Section>
          <NavItem icon={<Truck />} label="Fornecedores" active={pathname.startsWith('/fornecedores')} onClick={() => go('/fornecedores')} />
          <NavItem icon={<UserSquare />} label="Clientes" active={pathname.startsWith('/clientes')} onClick={() => go('/clientes')} />
          <NavItem icon={<Contact />} label="Colaboradores" active={pathname.startsWith('/colaboradores')} onClick={() => go('/colaboradores')} />
          <Section>Estrutura</Section>
          <NavItem icon={<Building2 />} label="Empresas e unidades" active={pathname.startsWith('/estrutura')} onClick={() => go('/estrutura')} />
          <Section>Sistema</Section>
          <NavItem icon={<Plug />} label="Integrações" />
          <NavItem icon={<Settings />} label="Configurações" />
          <Section>Módulos</Section>
          <NavItem icon={<Users />} label="RH" soon />
          <NavItem icon={<Calculator />} label="Contábil" soon />
          <NavItem icon={<Package />} label="Suprimentos" soon />
        </nav>
      </aside>

      <main className="main">
        <header className="topbar">
          <div className="topbar-menu">
            <button className="icon-btn hamburger" onClick={() => setNavOpen(true)} aria-label="Menu"><Menu /></button>
            <div className="crumb">{crumb}</div>
          </div>
          <div className="top-search"><Search /><input placeholder="Buscar…" /></div>
        </header>
        <div className="content">{children}</div>
      </main>
    </>
  );
}

const Section = ({ children }: { children: ReactNode }) => <div className="sb-section">{children}</div>;
function NavItem({ icon, label, active, soon, onClick }: { icon: ReactNode; label: string; active?: boolean; soon?: boolean; onClick?: () => void }) {
  const disabled = !active && !onClick;
  return (
    <button className={`sb-item ${active ? 'active' : ''} ${disabled ? 'disabled' : ''}`} disabled={disabled} onClick={onClick}>
      {icon} {label} {soon && <span className="soon">Em breve</span>}
    </button>
  );
}
