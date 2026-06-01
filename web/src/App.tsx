import { useState, type ReactNode } from 'react';
import { Routes, Route, Navigate, useNavigate, useLocation } from 'react-router-dom';
import {
  LayoutDashboard, ArrowUpRight, ArrowDownLeft, Activity, ClipboardCheck, Target,
  FileText, FileBox, LineChart, ShieldCheck, Building2, Plug, Settings,
  Users, Calculator, Package, Search, Menu,
} from 'lucide-react';
import { AppDataProvider } from './store';
import { DashboardPage } from './pages/dashboard';
import { CentrosListPage, CentroDetailPage } from './pages/centros';
import { EstruturaPage } from './pages/estrutura';

export default function App() {
  return (
    <AppDataProvider>
      <Shell>
        <Routes>
          <Route path="/" element={<Navigate to="/inicio" replace />} />
          <Route path="/inicio" element={<DashboardPage />} />
          <Route path="/centros" element={<CentrosListPage />} />
          <Route path="/centros/novo" element={<CentroDetailPage modo="novo" />} />
          <Route path="/centros/:id" element={<CentroDetailPage modo="detalhe" />} />
          <Route path="/estrutura" element={<EstruturaPage />} />
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
          <NavItem icon={<ArrowUpRight />} label="Contas a pagar" />
          <NavItem icon={<ArrowDownLeft />} label="Contas a receber" />
          <NavItem icon={<Activity />} label="Fluxo de caixa" />
          <NavItem icon={<ClipboardCheck />} label="Aprovações" />
          <NavItem icon={<Target />} label="Centros de custo" active={pathname.startsWith('/centros')} onClick={() => go('/centros')} />
          <NavItem icon={<FileText />} label="Relatórios" />
          <NavItem icon={<FileBox />} label="Documentos" />
          <NavItem icon={<LineChart />} label="Projeções" />
          <NavItem icon={<ShieldCheck />} label="Auditoria" />
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
