import { type ReactNode } from 'react';
import { Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AppLayout } from './app-layout';
import { auth } from './api';
import { CentrosListPage, CentroDetailPage } from './pages/centros';
import { ProjectsListPage, ProjectDetailPage, ProjectFormPage } from './pages/projects';
import { EstruturaPage } from './pages/estrutura';
import { LancamentosPage, NovoLancamentoPage, LancamentoDetailPage } from './pages/lancamentos';
import { ExpenseCategoriesPage } from './pages/expense-categories';
import { RegrasRetencaoPage } from './pages/regras-retencao';
import { BankAccountsPage } from './pages/bank-accounts';
import { ContabilPlanoPage } from './pages/contabil-plano';
import { ContabilLancamentosPage } from './pages/contabil-lancamentos';
import { BalancetePage } from './pages/balancete';
import { ContabilDrePage } from './pages/contabil-dre';
import { ContabilBalancoPage } from './pages/contabil-balanco';
import { LivroDiarioPage } from './pages/livro-diario';
import { LivroRazaoPage } from './pages/livro-razao';
import { RetencoesPage } from './pages/retencoes';
import { ObrigacoesPage } from './pages/fiscal/obrigacoes';
import { ContabilRevisaoPage } from './pages/contabil-revisao';
import { TributacaoPage } from './pages/tributacao';
import { TreasuryHomePage } from './pages/treasury/home';
import { ConciliacaoPage } from './pages/conciliacao';
import { AgingPage } from './pages/aging';
import { CashFlowPage } from './pages/cash-flow';
import { PaymentsReportPage } from './pages/payments-report/page';
import { FornecedoresPage, VendorDetailPage } from './pages/fornecedores';
import { ClientesPage } from './pages/clientes';
import { ColaboradoresPage } from './pages/colaboradores';
import { LoginPage } from './pages/login';
import { PayablesLayout, PayableDetailPage, NewPayablePage, EditPayablePage } from './pages/payables';
import {
  RequisitionsLayout,
  RequisitionDetailPage,
  NewRequisitionPage,
  EditRequisitionPage,
  RequesterDashboard,
  RequisitionTriageInbox,
  RequisitionTriageDecisionPage,
  RequisitionApprovalInbox,
  RequisitionApprovalDecisionPage,
  RequisitionOverviewPage,
  RequisitionBudgetsPage,
  RequisitionSlaReportPage,
} from './pages/requisitions';
import {
  BudgetAdjustmentsLayout,
  BudgetAdjustmentDetailPage,
  NewBudgetAdjustmentPage,
  EditBudgetAdjustmentPage,
} from './pages/budgetAdjustments';
import { NotificacoesPage } from './pages/notificacoes';
import { ReceivablesLayout, ReceivableDetailPage, NewReceivablePage, EditReceivablePage } from './pages/receivables';
import { PendenciesPage } from './pages/pendencies';
import { DocumentationPage } from './pages/documentation';
import { RolesPage } from './pages/roles';
import { UsersPage } from './pages/users';
import { AuditPage } from './pages/audit';
import { FinalApproverRedirect, RoleGate, RequirePermission, RoleLanding } from './auth-guards';
import { P, type Permission } from './permissions';

/**
 * Redirects a pre-redesign treasury route (/app/remittances, /app/reconciliation) into the
 * corresponding Gestão Financeira tab, PRESERVING query params — `?remittance=<id>` deep links
 * from the payment queue's chips must keep working.
 */
function TreasuryTabRedirect({ tab }: { tab: string }) {
  const { search } = useLocation();
  const params = new URLSearchParams(search);
  params.set('tab', tab);
  return <Navigate to={`/app/treasury?${params.toString()}`} replace />;
}

export default function App() {
  // Route guard: renders the page only if the role has any of the permissions (Admin passes).
  const g = (el: ReactNode, anyOf: Permission[]) => <RequirePermission anyOf={anyOf}>{el}</RequirePermission>;
  // Routes outside the final approver's surface → Inbox (their read permissions don't segregate).
  const fa = (el: ReactNode) => <FinalApproverRedirect>{el}</FinalApproverRedirect>;
  return (
    <Routes>
      {/* No landing page: the login IS the front door. `/login` stays as an alias because
          `RequireAuth` and the session-error screen already redirect there. */}
      <Route path='/' element={<LoginPage />} />
      <Route path='/login' element={<LoginPage />} />
      <Route
        path='/app'
        element={
          <RequireAuth>
            <RoleGate>
              <AppLayout />
            </RoleGate>
          </RequireAuth>
        }
      >
        <Route index element={<RoleLanding />} />
        <Route path='payables' element={<PayablesLayout />} />
        <Route path='payables/new' element={g(<NewPayablePage />, [P.payablesWrite])} />
        <Route path='payables/:id' element={<PayableDetailPage />} />
        <Route path='payables/:id/edit' element={g(<EditPayablePage />, [P.payablesWrite])} />
        <Route path='receivables' element={<ReceivablesLayout />} />
        <Route path='receivables/new' element={g(<NewReceivablePage />, [P.receivablesWrite])} />
        <Route path='receivables/:id' element={<ReceivableDetailPage />} />
        <Route path='receivables/:id/edit' element={g(<EditReceivablePage />, [P.receivablesWrite])} />
        <Route path='expense-categories' element={<ExpenseCategoriesPage />} />
        <Route path='retention-rules' element={<RegrasRetencaoPage />} />
        <Route path='bank-accounts' element={g(<BankAccountsPage />, [P.treasuryRead])} />
        {/* Gestão Financeira (Financeiro home): queue + remittances + reconciliation + history. */}
        <Route path='treasury' element={g(<TreasuryHomePage />, [P.payablesPay, P.treasuryRead])} />
        <Route path='remittances' element={<TreasuryTabRedirect tab='remittances' />} />
        <Route path='accounting/chart-of-accounts' element={g(<ContabilPlanoPage />, [P.accountingRead])} />
        <Route path='accounting/journal-entries' element={g(<ContabilLancamentosPage />, [P.accountingRead])} />
        <Route path='accounting/journal-book' element={g(<LivroDiarioPage />, [P.accountingRead])} />
        <Route path='accounting/ledger' element={g(<LivroRazaoPage />, [P.accountingRead])} />
        <Route path='accounting/trial-balance' element={<BalancetePage />} />
        <Route path='accounting/income-statement' element={<ContabilDrePage />} />
        <Route path='accounting/balance-sheet' element={<ContabilBalancoPage />} />
        <Route path='accounting/withholdings' element={<RetencoesPage />} />
        <Route path='accounting/obrigacoes' element={g(<ObrigacoesPage />, [P.fiscalRead])} />
        {/* Fiscal was folded into Contábil — redirect the old route (bookmarks/links). */}
        <Route path='fiscal/obrigacoes' element={<Navigate to='/app/accounting/obrigacoes' replace />} />
        <Route path='accounting/review' element={g(<ContabilRevisaoPage />, [P.accountingRead])} />
        {/* Tributação (tax review) — the former Contábil "Despesas" tab, now its own role/page. */}
        <Route path='tax/review' element={g(<TributacaoPage />, [P.payablesTax])} />
        {/* Contábil unified (redesign): the 3 old screens collapsed into /accounting/review — redirect the old routes. */}
        <Route path='accounting/pending' element={<Navigate to='/app/accounting/review' replace />} />
        <Route path='accounting/expense-recording' element={<Navigate to='/app/accounting/review' replace />} />
        <Route path='accounting/classification' element={<Navigate to='/app/accounting/review' replace />} />
        <Route path='reconciliation' element={g(<ConciliacaoPage />, [P.treasuryRead])} />
        <Route path='aging' element={<AgingPage />} />
        <Route path='cash-flow' element={g(<CashFlowPage />, [P.treasuryRead])} />
        <Route path='payments-report' element={g(<PaymentsReportPage />, [P.payablesRead, P.treasuryRead])} />
        <Route path='accounts-payable' element={<LancamentosPage tipo='Payable' />} />
        <Route path='accounts-receivable' element={<LancamentosPage tipo='Receivable' />} />
        <Route path='pendencies' element={<PendenciesPage />} />
        {/* Old Aprovações screen removed — folded into Pendências. */}
        <Route path='approvals' element={<Navigate to='/app/pendencies' replace />} />
        <Route path='entries/new' element={<NovoLancamentoPage />} />
        <Route path='entries/:id' element={<LancamentoDetailPage />} />
        <Route path='cost-centers' element={fa(<CentrosListPage />)} />
        <Route path='cost-centers/new' element={fa(<CentroDetailPage modo='novo' />)} />
        <Route path='cost-centers/:id' element={fa(<CentroDetailPage modo='detalhe' />)} />
        <Route path='projects' element={fa(<ProjectsListPage />)} />
        <Route path='projects/new' element={fa(<ProjectFormPage />)} />
        <Route path='projects/:id' element={fa(<ProjectDetailPage />)} />
        <Route path='projects/:id/edit' element={fa(<ProjectFormPage />)} />
        <Route path='requisitions' element={fa(g(<RequisitionsLayout />, [P.requisitionsRead, P.requisitionsWrite]))} />
        <Route path='requisitions/mine' element={g(<RequesterDashboard />, [P.requisitionsWrite])} />
        <Route path='requisitions/triage' element={g(<RequisitionTriageInbox />, [P.requisitionsTriage])} />
        <Route path='requisitions/triage/:id' element={g(<RequisitionTriageDecisionPage />, [P.requisitionsTriage])} />
        <Route
          path='requisitions/overview'
          element={g(<RequisitionOverviewPage />, [P.requisitionsApprove, P.requisitionsTriage])}
        />
        <Route
          path='requisitions/budgets'
          element={g(<RequisitionBudgetsPage />, [P.requisitionsApprove, P.requisitionsTriage])}
        />
        <Route path='requisitions/sla-report' element={g(<RequisitionSlaReportPage />, [P.requisitionsSlaReport])} />
        <Route path='requisitions/approval' element={g(<RequisitionApprovalInbox />, [P.requisitionsApprove])} />
        <Route
          path='requisitions/approval/:id'
          element={g(<RequisitionApprovalDecisionPage />, [P.requisitionsApprove])}
        />
        <Route path='requisitions/new' element={g(<NewRequisitionPage />, [P.requisitionsWrite])} />
        <Route
          path='requisitions/:id'
          element={g(<RequisitionDetailPage />, [P.requisitionsRead, P.requisitionsWrite])}
        />
        <Route path='requisitions/:id/edit' element={g(<EditRequisitionPage />, [P.requisitionsWrite])} />
        <Route path='budget-adjustments' element={g(<BudgetAdjustmentsLayout />, [P.budgetAdjustmentsRead])} />
        <Route path='budget-adjustments/new' element={g(<NewBudgetAdjustmentPage />, [P.budgetAdjustmentsWrite])} />
        <Route path='budget-adjustments/:id' element={g(<BudgetAdjustmentDetailPage />, [P.budgetAdjustmentsRead])} />
        <Route
          path='budget-adjustments/:id/edit'
          element={g(<EditBudgetAdjustmentPage />, [P.budgetAdjustmentsWrite])}
        />
        <Route path='notificacoes' element={<NotificacoesPage />} />
        <Route path='structure' element={fa(<EstruturaPage />)} />
        <Route path='vendors' element={fa(<FornecedoresPage />)} />
        <Route path='vendors/new' element={fa(<VendorDetailPage mode='new' />)} />
        <Route path='vendors/:id' element={fa(<VendorDetailPage mode='detail' />)} />
        <Route path='customers' element={fa(<ClientesPage />)} />
        <Route path='employees' element={fa(<ColaboradoresPage />)} />
        {/* User manual — no permission gate and no `fa` wrapper, so every cargo (incl. the Aprovador) can open it.
            The base "Usuário" never reaches /app (RoleGate → NoRoleScreen); the nav hides it via 'notPlainUser'.
            `:sectionId` deep-links a section (e.g. the finance-only CNAB manual); the page filters restricted
            sections by role, so an unauthorized deep link just falls back to the first visible section. */}
        <Route path='documentation' element={<DocumentationPage />} />
        <Route path='documentation/:sectionId' element={<DocumentationPage />} />
        <Route path='settings/roles' element={g(<RolesPage />, [P.adminUsers])} />
        <Route path='settings/users' element={g(<UsersPage />, [P.adminUsers])} />
        <Route path='settings/audit' element={g(<AuditPage />, [P.auditRead])} />
        <Route path='*' element={<Navigate to='/app' replace />} />
      </Route>
      <Route path='*' element={<Navigate to='/' replace />} />
    </Routes>
  );
}

function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation();
  // Validity, not mere presence: a stale/expired token must redirect to /login (and be
  // cleared) instead of admitting the user into /app, where the app would white-screen.
  if (!auth.hasValidToken()) {
    auth.clear();
    return <Navigate to='/login' replace state={{ from: location.pathname + location.search }} />;
  }
  return <>{children}</>;
}
