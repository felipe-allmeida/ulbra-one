import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationOptions,
} from '@tanstack/react-query';
import {
  api,
  type AdminRole,
  type AdminUser,
  type NewUser,
  type UnitScopeAssignment,
  type CostCenterScopeAssignment,
  type AccountGroup,
  type AgingBucket,
  type AiClassificationSuggestion,
  type AuditTrailEvent,
  type JournalEntry,
  type JournalEntryInput,
  type JournalStatus,
  type LedgerAccount,
  type LedgerAccountInput,
  type LedgerAccountKind,
  type LedgerAccountUpdateInput,
  type BankAccount,
  type RemittanceReturnPreview,
  type RemittanceCriticism,
  type RemittanceReturnLineData,
  type BankAccountInput,
  type BankAccountUpdateInput,
  type BankTransaction,
  type CashFlowGranularity,
  type ClassificationPolicy,
  type Company,
  type CostCenter,
  type Project,
  type ProjectInput,
  type CostCenterUnitBudget,
  type CostCenterUnitBudgetInput,
  type CostCenterUnitBudgetUpdateInput,
  type CostCenterUnitBudgetPeriod,
  type Requisition,
  type RequisitionInput,
  type RequisitionAttachment,
  type RequisitionAttachmentKind,
  type BoletoBlobRef,
  type FinancialComplementInstallmentInput,
  type AddRecurrenceCycleInput,
  type RequisitionStatus,
  type RequisitionType,
  type BudgetAdjustment,
  type BudgetAdjustmentInput,
  type BudgetAdjustmentStatus,
  type BudgetAdjustmentType,
  type Customer,
  type CustomerInput,
  type Employee,
  type EmployeeInput,
  type ExpenseCategory,
  type FiscalDocument,
  type WithholdingRule,
  type WithholdingRuleInput,
  type WithholdingPreviewParams,
  type MatchStatus,
  type TransactionDirection,
  type ReconciliationStage,
  type PeriodSummary,
  type ParsedFiscalDocument,
  type ParsedBoleto,
  type PayableStageSummaryResponse,
  type PayableStageItem,
  type UploadTarget,
  type Payable,
  type PayableAttachment,
  type PayableAttachmentKind,
  type PayableInput,
  type PayableOrigin,
  type PayableStatus,
  type PayableReturnTarget,
  type Payment,
  type PaymentHistoryItem,
  type PaymentMethod,
  type PaymentQueueSummary,
  type Receipt,
  type Receivable,
  type ReceivableInput,
  type ReceivableStageSummaryResponse,
  type ReceivableStageItem,
  type ReceivableStatus,
  type RegisterPaymentInput,
  type RegisterReceiptInput,
  type StatementImport,
  type Transaction,
  type TransactionStatus,
  type TransactionType,
  type Unit,
  type Vendor,
  type VendorInput,
  type WithholdingTaxType,
  type RetentionGroup,
  type Remittance,
  type RemittanceListItem,
  type RemittanceStatus,
  type RemittanceCategory,
  type RemittanceLineStatus,
  type RemittanceReturnOutcome,
  type CreateRemittanceResponse,
  type CreateRemittanceInput,
  type PaymentsReportParams,
  type YearEndClosingResult,
} from './api';

export interface CostCenterBudgetsParams {
  unitId?: string;
  competencyFrom?: string;
  competencyTo?: string;
}

export interface DimensionBudgetControlParams {
  costCenterId?: string;
  unitId?: string;
  competencyFrom?: string;
  competencyTo?: string;
  excludeRequisitionId?: string;
}

export interface RequisitionsParams {
  companyId?: string;
  unitId?: string;
  costCenterId?: string;
  vendorId?: string;
  status?: RequisitionStatus;
  /** Multi-status OR-set (AEL-876); takes precedence over `status` when non-empty. */
  statuses?: RequisitionStatus[];
  type?: RequisitionType;
  competency?: string;
  competencyFrom?: string;
  competencyTo?: string;
  /** Free-text: description / #code / vendor name (AEL-876). */
  search?: string;
  /** Inclusive estimate-value range (AEL-876). */
  minValue?: number;
  maxValue?: number;
  /** Column sort, e.g. `estimate` / `-estimate` / `competency` (AEL-876). */
  sort?: string;
  page?: number;
  pageSize?: number;
  /** Scopes the list to the current user's own requisitions (Requester dashboard). */
  mine?: boolean;
  /** Only post-approval requisitions still missing their Nota Fiscal ("NF pendente" list). */
  awaitingNf?: boolean;
}

export interface BudgetAdjustmentsParams {
  companyId?: string;
  unitId?: string;
  costCenterId?: string;
  status?: BudgetAdjustmentStatus;
  type?: BudgetAdjustmentType;
  competency?: string;
  page?: number;
  pageSize?: number;
}

/** Sentinel `companyId` for the consolidated "Todas" mode (AEL-906): company-scoped hooks treat it as "omit
 * companyId". Defined here (not store.tsx) to avoid a store↔queries import cycle. */
export const ALL_COMPANIES = '__all__';

/** Stable query-key factory — single source of truth for cache keys + invalidation. */
export const queryKeys = {
  companies: ['companies'] as const,
  me: ['me'] as const,
  roles: ['roles'] as const,
  users: ['users'] as const,
  auditLogs: (params: AuditLogsParams) => ['auditLogs', params] as const,
  permissionCatalog: ['permissionCatalog'] as const,
  units: (companyId: string) => ['units', companyId] as const,
  costCenters: (companyId: string) => ['costCenters', companyId] as const,
  costCenter: (id: string) => ['costCenter', id] as const,
  projects: (companyId: string, search?: string) => ['projects', companyId, search ?? ''] as const,
  project: (id: string) => ['project', id] as const,
  projectMetrics: (id: string) => ['projectMetrics', id] as const,
  costCenterBudgets: (costCenterId: string, params: CostCenterBudgetsParams) =>
    ['costCenterBudgets', costCenterId, params] as const,
  requisitionBudgetControl: (id: string) => ['budgetControl', 'req', id] as const,
  dimensionBudgetControl: (params: DimensionBudgetControlParams) => ['budgetControl', 'dim', params] as const,
  requisicoes: (params: RequisitionsParams) => ['requisicoes', params] as const,
  requisicao: (id: string) => ['requisicao', id] as const,
  requisitionStageCounts: (companyId: string, mine?: boolean) =>
    ['requisitionStageCounts', companyId, mine ?? false] as const,
  budgetAdjustments: (params: BudgetAdjustmentsParams) => ['budgetAdjustments', params] as const,
  budgetAdjustment: (id: string) => ['budgetAdjustment', id] as const,
  notifications: (page: number, unreadOnly: boolean) => ['notifications', page, unreadOnly] as const,
  notificationsUnread: ['notificationsUnread'] as const,
  transactions: (params: TransactionsParams) => ['transactions', params] as const,
  transaction: (id: string) => ['transaction', id] as const,
  vendors: (search?: string, page = 1) => ['vendors', search ?? '', page] as const,
  vendor: (id: string) => ['vendor', id] as const,
  customers: (companyId: string, search?: string, page = 1) => ['customers', companyId, search ?? '', page] as const,
  employees: (companyId: string, search?: string, page = 1) => ['employees', companyId, search ?? '', page] as const,
  payables: (params: PayablesParams) => ['payables', params] as const,
  payable: (id: string) => ['payable', id] as const,
  payableStageSummary: (companyId: string) => ['payableStageSummary', companyId] as const,
  payableStageItems: (params: PayableStageItemsParams) => ['payableStageItems', params] as const,
  paymentQueueSummary: (params: PaymentQueueSummaryParams) => ['paymentQueueSummary', params] as const,
  paymentsHistory: (params: PaymentsHistoryParams) => ['paymentsHistory', params] as const,
  paymentsReport: (params: PaymentsReportListParams) => ['paymentsReport', params] as const,
  paymentsReportSummary: (params: PaymentsReportParams) => ['paymentsReportSummary', params] as const,
  payableAuditTrail: (id: string) => ['payableAuditTrail', id] as const,
  payableAccountingView: (id: string) => ['payableAccountingView', id] as const,
  receivables: (params: ReceivablesParams) => ['receivables', params] as const,
  receivable: (id: string) => ['receivable', id] as const,
  receivableStageSummary: (companyId: string) => ['receivableStageSummary', companyId] as const,
  receivableStageItems: (params: ReceivableStageItemsParams) => ['receivableStageItems', params] as const,
  receivableAuditTrail: (id: string) => ['receivableAuditTrail', id] as const,
  expenseCategories: (includeInactive = false) => ['expenseCategories', includeInactive] as const,
  withholdingRules: (includeInactive = false) => ['withholdingRules', includeInactive] as const,
  withholdingPreview: (params: WithholdingPreviewParams) => ['withholdingPreview', params] as const,
  bankAccounts: (companyId: string, includeInactive = false) => ['bankAccounts', companyId, includeInactive] as const,
  statementImports: (companyId?: string, bankAccountId?: string) =>
    ['statementImports', companyId ?? '', bankAccountId ?? ''] as const,
  bankTransactions: (params: BankTransactionsParams) => ['bankTransactions', params] as const,
  periodSummary: (params: PeriodSummaryParams) => ['periodSummary', params] as const,
  classificationPolicy: (companyId: string) => ['classificationPolicy', companyId] as const,
  aging: (companyId: string, asOf?: string) => ['aging', companyId, asOf ?? ''] as const,
  agingItems: (params: AgingItemsParams) => ['agingItems', params] as const,
  ledgerAccounts: (params: LedgerAccountsParams) => ['ledgerAccounts', params] as const,
  journalEntries: (params: JournalEntriesParams) => ['journalEntries', params] as const,
  journalEntry: (id: string) => ['journalEntry', id] as const,
  trialBalance: (params: TrialBalanceParams) => ['trialBalance', params] as const,
  incomeStatement: (params: IncomeStatementParams) => ['incomeStatement', params] as const,
  journalBook: (params: JournalBookParams) => ['journalBook', params] as const,
  generalLedger: (params: GeneralLedgerParams) => ['generalLedger', params] as const,
  balanceSheet: (params: BalanceSheetParams) => ['balanceSheet', params] as const,
  withholdingsReport: (companyId?: string) => ['withholdingsReport', companyId ?? ''] as const,
  fiscalAssessment: (params: FiscalAssessmentParams) => ['fiscalAssessment', params] as const,
  cashFlow: (params: CashFlowParams) => ['cashFlow', params] as const,
  slaReport: (params: SlaReportParams) => ['slaReport', params] as const,
  cashFlowItems: (params: CashFlowItemsParams) => ['cashFlowItems', params] as const,
  remittances: (params: RemittancesParams) => ['remittances', params] as const,
  remittanceLines: (params: RemittanceLinesParams) => ['remittanceLines', params] as const,
  remittance: (id: string) => ['remittance', id] as const,
  remittanceReturnHistory: (payingBankAccountId: string) => ['remittanceReturnHistory', payingBankAccountId] as const,
};

export interface LedgerAccountsParams {
  accountGroup?: AccountGroup;
  kind?: LedgerAccountKind;
  activeOnly?: boolean;
}

export interface JournalEntriesParams {
  companyId?: string;
  status?: JournalStatus;
  from?: string;
  to?: string;
}

export interface TrialBalanceParams {
  companyId?: string;
  from?: string;
  to?: string;
  costCenterId?: string;
  unitId?: string;
}

export interface IncomeStatementParams {
  companyId?: string;
  from?: string;
  to?: string;
  costCenterId?: string;
  unitId?: string;
}

export interface JournalBookParams {
  companyId?: string;
  from?: string;
  to?: string;
  unitId?: string;
}

export interface GeneralLedgerParams {
  companyId?: string;
  ledgerAccountId?: string;
  root?: string;
  from?: string;
  to?: string;
  costCenterId?: string;
  unitId?: string;
  page?: number;
  pageSize?: number;
}

export interface BalanceSheetParams {
  companyId?: string;
  asOf?: string;
}

export interface SlaReportParams {
  companyId?: string;
  unitId?: string;
  from?: string;
  to?: string;
}

export interface CashFlowParams {
  companyId?: string;
  granularity?: CashFlowGranularity;
  horizon?: number;
  asOf?: string;
  minReserve?: number;
}

export interface CashFlowItemsParams {
  companyId?: string;
  from: string;
  to: string;
  direction: 'in' | 'out';
  asOf?: string;
}

export interface RemittancesParams {
  companyId?: string;
  status?: RemittanceStatus;
  category?: RemittanceCategory;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface RemittanceLinesParams {
  companyId?: string;
  unitId?: string;
  costCenterId?: string;
  category?: RemittanceCategory;
  status?: RemittanceLineStatus;
  from?: string;
  to?: string;
  remittanceId?: string;
  page?: number;
  pageSize?: number;
}

/** Report list params = the shared report filters plus pagination. */
export type PaymentsReportListParams = PaymentsReportParams & { page?: number; pageSize?: number };

export interface FiscalAssessmentParams {
  perApur: string;
  companyId?: string;
  costCenterId?: string;
  taxType?: WithholdingTaxType;
  retentionGroup?: RetentionGroup;
}

export interface BankTransactionsParams {
  companyId?: string;
  bankAccountId?: string;
  importId?: string;
  matchStatus?: MatchStatus;
  stage?: ReconciliationStage;
  direction?: TransactionDirection;
  dueFrom?: string;
  dueTo?: string;
  page?: number;
  pageSize?: number;
}

export interface PeriodSummaryParams {
  companyId?: string;
  bankAccountId?: string;
  direction?: TransactionDirection;
  dueFrom?: string;
  dueTo?: string;
}

export interface PayablesParams {
  companyId?: string;
  status?: PayableStatus;
  vendorId?: string;
  costCenterId?: string;
  categoryId?: string;
  /** Accounting competency "YYYY-MM" — keeps payables whose IssueDate falls in that month. */
  competency?: string;
  dueFrom?: string;
  dueTo?: string;
  paid?: boolean;
  classified?: boolean;
  /** NEW (AEL-871): payee / CNPJ / document. */
  search?: string;
  /** NEW (AEL-871): effective method (payable override ?? account default). */
  paymentMethod?: PaymentMethod;
  page?: number;
  pageSize?: number;
}

/** NEW (AEL-871): same filters as the queue list, minus pagination. */
export interface PaymentQueueSummaryParams {
  companyId: string;
  status?: PayableStatus;
  paid?: boolean;
  search?: string;
  paymentMethod?: PaymentMethod;
}

/** NEW (AEL-871): cross-payable settlement history filters. */
export interface PaymentsHistoryParams {
  companyId: string;
  from?: string;
  to?: string;
  bankAccountId?: string;
  method?: PaymentMethod;
  search?: string;
  includeReversed?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ReceivablesParams {
  companyId?: string;
  status?: ReceivableStatus;
  payerId?: string;
  costCenterId?: string;
  dueFrom?: string;
  dueTo?: string;
  received?: boolean;
  page?: number;
  pageSize?: number;
}

export interface PayableStageItemsParams {
  companyId: string;
  status?: PayableStatus;
  /** Filter by NF presence — e.g. the Pendências "Nota Fiscal" tab passes `false`. */
  hasFiscalDocument?: boolean;
  /** Filter by intake origin — e.g. the "Adiantamentos" tab passes `Advance` (no status). */
  origin?: PayableOrigin;
  /** Requisition auto-schedule review gate (AEL-846): only payables still awaiting review. */
  reviewPending?: boolean;
  /** Free-text search (AEL-865): vendor / description / NF number / requisition code. */
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface ReceivableStageItemsParams {
  companyId: string;
  status?: ReceivableStatus;
  page?: number;
  pageSize?: number;
}

/** Filters of the audit trail list. All optional; the backend combines them with AND. */
export interface AuditLogsParams {
  entityType?: string;
  entityId?: string;
  actorUserId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface TransactionsParams {
  companyId?: string;
  type?: TransactionType;
  status?: TransactionStatus;
  costCenterId?: string;
  page?: number;
  pageSize?: number;
}

export interface AgingItemsParams {
  companyId: string;
  bucket?: AgingBucket;
  vendorId?: string;
  costCenterId?: string;
  asOf?: string;
}

/* ===================== Queries ===================== */

export function useCompanies() {
  return useQuery({ queryKey: queryKeys.companies, queryFn: () => api.listCompanies() });
}

export function useMe() {
  // networkMode 'always': the auth bootstrap must resolve to success OR error even when the
  // browser reports offline. With the default 'online' mode a failed /auth/me pauses the retry
  // (status stays 'pending'), so the guard would render null forever → white screen. 'always'
  // lets the failure surface as isError, so RoleGate can redirect or show the session-error screen.
  return useQuery({ queryKey: queryKeys.me, queryFn: () => api.me(), networkMode: 'always' });
}

// ---------- Access administration (roles → permissions) ----------
export function useRoles(opts?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.roles,
    queryFn: () => api.listRoles(),
    enabled: opts?.enabled ?? true,
  });
}

export function usePermissionCatalog() {
  return useQuery({ queryKey: queryKeys.permissionCatalog, queryFn: () => api.listPermissionCatalog() });
}

export function useSetRolePermissions(
  options?: UseMutationOptions<AdminRole, unknown, { id: string; permissions: string[] }>,
) {
  return useInvalidatingMutation(
    ({ id, permissions }) => api.setRolePermissions(id, permissions),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.roles }),
    options,
  );
}

// ---------- Audit trail (admin) ----------
/** Global write trail, newest first. Paginated server-side; the previous page stays put while the next loads. */
export function useAuditLogs(params: AuditLogsParams) {
  return useQuery({
    queryKey: queryKeys.auditLogs(params),
    queryFn: () => api.listAuditLogs(params),
    placeholderData: keepPreviousData,
  });
}

// ---------- User administration (company-scoped access — AEL-764) ----------
/** `enabled` lets callers that only *optionally* need names (audit trail) skip a 403 without admin:users. */
export function useUsers(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.users,
    queryFn: () => api.listUsers(),
    enabled: options?.enabled ?? true,
  });
}

/** Pre-registers a user by e-mail (convite). Invalidates the users list so the new row shows up pending. */
export function useCreateUser(options?: UseMutationOptions<AdminUser, unknown, NewUser>) {
  return useInvalidatingMutation(
    (body) => api.createUser(body),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.users }),
    options,
  );
}

export function useSetUserScope(
  options?: UseMutationOptions<
    AdminUser,
    unknown,
    { id: string; companyIds: string[]; units: UnitScopeAssignment[]; costCenters: CostCenterScopeAssignment[] }
  >,
) {
  return useInvalidatingMutation(
    ({ id, companyIds, units, costCenters }) => api.setUserScope(id, companyIds, units, costCenters),
    (qc) => {
      void qc.invalidateQueries({ queryKey: queryKeys.users });
      void qc.invalidateQueries({ queryKey: queryKeys.me });
    },
    options,
  );
}

export function useSetUserRole(options?: UseMutationOptions<AdminUser, unknown, { id: string; role: string }>) {
  return useInvalidatingMutation(
    ({ id, role }) => api.setUserRole(id, role),
    (qc) => {
      void qc.invalidateQueries({ queryKey: queryKeys.users });
      void qc.invalidateQueries({ queryKey: queryKeys.me });
    },
    options,
  );
}

export function useSetUserActive(options?: UseMutationOptions<AdminUser, unknown, { id: string; active: boolean }>) {
  return useInvalidatingMutation(
    ({ id, active }) => api.setUserActive(id, active),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.users }),
    options,
  );
}

/** Grants/revokes a user's authority to approve budget adjustments (AEL-883). Invalidates the users list and
 * `me` (the affected user's approve button toggles once their `me` refetches). */
export function useSetBudgetAdjustmentApprover(
  options?: UseMutationOptions<AdminUser, unknown, { id: string; canApprove: boolean }>,
) {
  return useInvalidatingMutation(
    ({ id, canApprove }) => api.setBudgetAdjustmentApprover(id, canApprove),
    (qc) => {
      void qc.invalidateQueries({ queryKey: queryKeys.users });
      void qc.invalidateQueries({ queryKey: queryKeys.me });
    },
    options,
  );
}

export function useUnits(companyId: string) {
  // Consolidated mode (AEL-906): fetch every company's units (shares the useAllUnits cache key).
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.units(all ? 'all' : companyId),
    queryFn: () => (all ? api.listAllUnits() : api.listUnits(companyId)),
    enabled: all || !!companyId,
  });
}

/** Units of a company restricted to the caller's scope (AEL-883) — the requisition form only offers the
 * units a scoped requester may use (Admin / non-scoped roles get all, the backend bypasses). Distinct cache
 * key from useUnits, but the unit-CRUD invalidations (`['units', companyId]`) prefix-match it. */
export function useScopedUnits(companyId: string) {
  return useQuery({
    queryKey: [...queryKeys.units(companyId), 'scoped'] as const,
    queryFn: () => api.listUnits(companyId, true),
    enabled: !!companyId,
  });
}

/** All units across companies (admin users screen groups them by company). */
export function useAllUnits(opts?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.units('all'),
    queryFn: () => api.listAllUnits(),
    enabled: opts?.enabled ?? true,
  });
}

/** All cost centers across companies (admin users screen groups them by company). */
export function useAllCostCenters(opts?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.costCenters('all'),
    queryFn: () => api.listAllCostCenters(),
    enabled: opts?.enabled ?? true,
  });
}

// ---------- Projects (Budgeting / AEL-792) ----------

export function useProjects(companyId: string, search?: string) {
  // Consolidated mode (AEL-906): omit companyId → projects across all companies.
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.projects(all ? 'all' : companyId, search),
    queryFn: () => api.listProjects({ companyId: all ? undefined : companyId, search }).then((p) => p.items),
    enabled: all || !!companyId,
  });
}

export function useProject(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.project(id ?? ''),
    queryFn: () => api.getProject(id!),
    enabled: !!id,
  });
}

/** Accumulated tracking (budgeted × forecast × realized × balance) for the project detail screen. */
export function useProjectMetrics(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.projectMetrics(id ?? ''),
    queryFn: () => api.getProjectMetrics(id!),
    enabled: !!id,
  });
}

export type ProjectVars = ProjectInput & { id?: string };

export function useSaveProject(options?: UseMutationOptions<Project, unknown, ProjectVars>) {
  return useInvalidatingMutation(
    ({ id, ...input }) => (id ? api.updateProject(id, input) : api.createProject(input)),
    (qc, data) => {
      void qc.invalidateQueries({ queryKey: ['projects'] });
      void qc.invalidateQueries({ queryKey: queryKeys.project(data.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.projectMetrics(data.id) });
    },
    options,
  );
}

export function useDeleteProject(options?: UseMutationOptions<void, unknown, string>) {
  return useInvalidatingMutation(
    (id) => api.deleteProject(id),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['projects'] });
    },
    options,
  );
}

// ---------- Cost-center budget (Core / AEL-830) ----------

// The budget cell is now owned by the cost center (Core). A change in any cell alters the
// CC roll-up, so the invalidation reaches the CC list/detail besides the cells themselves.
function invalidateCostCenterBudget(qc: ReturnType<typeof useQueryClient>, costCenterId: string) {
  void qc.invalidateQueries({ queryKey: ['costCenterBudgets', costCenterId] });
  void qc.invalidateQueries({ queryKey: queryKeys.costCenter(costCenterId) });
  void qc.invalidateQueries({ queryKey: ['costCenters'] });
}

export function useCostCenterBudgets(
  costCenterId: string | undefined,
  params: CostCenterBudgetsParams = {},
  options?: { enabled?: boolean },
) {
  return useQuery({
    queryKey: queryKeys.costCenterBudgets(costCenterId ?? '', params),
    queryFn: () => api.listCostCenterBudgets(costCenterId!, params),
    enabled: (options?.enabled ?? true) && !!costCenterId,
    placeholderData: keepPreviousData,
  });
}

export function useCreateCostCenterBudget(
  costCenterId: string,
  options?: UseMutationOptions<CostCenterUnitBudgetPeriod, unknown, CostCenterUnitBudgetInput>,
) {
  return useInvalidatingMutation(
    (input) => api.createCostCenterBudget(costCenterId, input),
    (qc) => invalidateCostCenterBudget(qc, costCenterId),
    options,
  );
}

export function useUpdateCostCenterBudget(
  costCenterId: string,
  options?: UseMutationOptions<CostCenterUnitBudget, unknown, { id: string; input: CostCenterUnitBudgetUpdateInput }>,
) {
  return useInvalidatingMutation(
    ({ id, input }) => api.updateCostCenterBudget(costCenterId, id, input),
    (qc) => invalidateCostCenterBudget(qc, costCenterId),
    options,
  );
}

export function useDeleteCostCenterBudget(costCenterId: string, options?: UseMutationOptions<void, unknown, string>) {
  return useInvalidatingMutation(
    (id) => api.deleteCostCenterBudget(costCenterId, id),
    (qc) => invalidateCostCenterBudget(qc, costCenterId),
    options,
  );
}

// ---------- Server-side budget control (AEL-830) ----------

export function useRequisitionBudgetControl(id: string | undefined, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.requisitionBudgetControl(id ?? ''),
    queryFn: () => api.getRequisitionBudgetControl(id!),
    enabled: (options?.enabled ?? true) && !!id,
    placeholderData: keepPreviousData,
  });
}

/** BATCH budget control for the approval queue (aside of the Approver dashboard): ONE grouped request
 * (`/requisitions/budget-controls`) instead of one per requisition. Returns one entry per input id (aligned
 * to `ids`) so callers keep aggregating "X of Y fit the budget" / "N require adequação" unchanged. */
export function useQueueBudgetControls(ids: string[]) {
  const q = useQuery({
    queryKey: ['budgetControl', 'batch', [...ids].sort()] as const,
    queryFn: () => api.getRequisitionBudgetControls(ids),
    enabled: ids.length > 0,
    placeholderData: keepPreviousData,
  });
  return ids.map((id) => ({ data: q.data?.controls[id] }));
}

/** BATCH project metrics (Approver dashboard/budgets): ONE grouped request (`/projects/metrics`) instead of
 * one per project. Returns one entry per input id (aligned to `ids`) so callers know how many overflowed. */
export function useProjectMetricsList(ids: string[]) {
  const q = useQuery({
    queryKey: ['projectMetrics', 'batch', [...ids].sort()] as const,
    queryFn: () => api.getProjectMetricsList(ids),
    enabled: ids.length > 0,
    placeholderData: keepPreviousData,
  });
  return ids.map((id) => ({ data: q.data?.metrics[id] }));
}

export function useDimensionBudgetControl(params: DimensionBudgetControlParams, options?: { enabled?: boolean }) {
  const ready = !!params.costCenterId && !!params.unitId && !!params.competencyFrom;
  return useQuery({
    queryKey: queryKeys.dimensionBudgetControl(params),
    queryFn: () =>
      api.getDimensionBudgetControl({
        costCenterId: params.costCenterId!,
        unitId: params.unitId!,
        competencyFrom: params.competencyFrom!,
        competencyTo: params.competencyTo,
        excludeRequisitionId: params.excludeRequisitionId,
      }),
    enabled: (options?.enabled ?? true) && ready,
    placeholderData: keepPreviousData,
  });
}

// ---------- Requisitions (Budgeting / AEL-788 + workflow AEL-789) ----------

export function useRequisitions(params: RequisitionsParams, options?: { enabled?: boolean }) {
  // Consolidated mode (AEL-906): omit companyId → requisitions across all companies (distinct 'all' cache key).
  const all = params.companyId === ALL_COMPANIES;
  const effective = all ? { ...params, companyId: undefined } : params;
  return useQuery({
    queryKey: queryKeys.requisicoes(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.listRequisitions(effective),
    enabled: (options?.enabled ?? true) && (all || !!params.companyId),
    placeholderData: keepPreviousData,
  });
}

/** Decisions (Approved/Returned/Rejected) of the logged-in approver — inbox "Histórico" tab (AEL-859).
 * Consolidated "Todas" (AEL-906): omit companyId — my-decisions is already scoped to the caller. */
export function useMyRequisitionDecisions(companyId: string | undefined, page: number) {
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: ['myDecisions', all ? 'all' : (companyId ?? ''), page] as const,
    queryFn: () => api.listMyRequisitionDecisions({ companyId: all ? undefined : companyId, page }),
    enabled: all || !!companyId,
    placeholderData: keepPreviousData,
  });
}

/** Triage decisions (forwarded/returned/rejected) of the logged-in controller — inbox "Histórico" tab.
 * Consolidated "Todas" (AEL-906): omit companyId — my-triage-decisions is already scoped to the caller. */
export function useMyRequisitionTriageDecisions(companyId: string | undefined, page: number) {
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: ['myTriageDecisions', all ? 'all' : (companyId ?? ''), page] as const,
    queryFn: () => api.listMyRequisitionTriageDecisions({ companyId: all ? undefined : companyId, page }),
    enabled: all || !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function useRequisition(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.requisicao(id ?? ''),
    queryFn: () => api.getRequisition(id!),
    enabled: !!id,
  });
}

/** Budget by natureza (cost center) for the company in a competência; unitId optional = consolidated. */
export function useCostCenterBudgetSummary(companyId: string, competency: string, unitId?: string) {
  return useQuery({
    queryKey: ['costCenterBudgetSummary', companyId, competency, unitId ?? null] as const,
    queryFn: () => api.getCostCenterBudgetSummary({ companyId, competency, unitId }),
    enabled: !!companyId && !!competency,
    placeholderData: keepPreviousData,
  });
}

/** Total realized per cost center for the company — cost-center list card (spend × budget). */
export function useCostCenterRealized(companyId: string | undefined, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['costCenterRealized', companyId ?? ''] as const,
    queryFn: () => api.getCostCenterRealized(companyId!),
    enabled: (options?.enabled ?? true) && !!companyId,
    placeholderData: keepPreviousData,
  });
}

/** Multi-month projection of the entire queue against the budget (AEL-876); unitId optional = whole company. */
export function useQueueBudgetProjection(
  companyId: string,
  params?: { unitId?: string; from?: string; to?: string },
  options?: { enabled?: boolean },
) {
  return useQuery({
    queryKey: [
      'queueBudgetProjection',
      companyId,
      params?.unitId ?? null,
      params?.from ?? null,
      params?.to ?? null,
    ] as const,
    queryFn: () => api.getQueueBudgetProjection({ companyId, ...params }),
    enabled: (options?.enabled ?? true) && !!companyId,
    placeholderData: keepPreviousData,
  });
}

/**
 * Counts (and summed estimated value) per status for the stage tabs/KPIs. One grouped query on the server
 * (`/requisitions/stage-counts`) replaces the former per-status fan-out (one PageSize=1 list call per status).
 * `counts`/`totals` are projected down to the requested `statuses`; an absent status reads as zero.
 */
export function useRequisitionStageCounts(
  companyId: string,
  statuses: readonly RequisitionStatus[],
  opts?: { mine?: boolean },
) {
  const mine = opts?.mine ?? false;
  // Consolidated mode (AEL-906): omit companyId → counts across all companies.
  const all = companyId === ALL_COMPANIES;
  const q = useQuery({
    queryKey: queryKeys.requisitionStageCounts(all ? 'all' : companyId, mine),
    queryFn: () => api.getRequisitionStageCounts({ companyId: all ? undefined : companyId, mine }),
    enabled: all || !!companyId,
  });
  const stages = q.data?.stages;
  const counts: Partial<Record<RequisitionStatus, number>> = {};
  const totals: Partial<Record<RequisitionStatus, number>> = {};
  statuses.forEach((status) => {
    counts[status] = stages?.[status]?.count ?? 0;
    totals[status] = stages?.[status]?.total ?? 0;
  });
  return { counts, totals, isLoading: q.isLoading };
}

/** Invalidates lists, counts, the decision history and (if there is an id) the requisition detail. */
function invalidateRequisition(qc: ReturnType<typeof useQueryClient>, id?: string) {
  void qc.invalidateQueries({ queryKey: ['requisicoes'] });
  void qc.invalidateQueries({ queryKey: ['requisitionStageCounts'] });
  void qc.invalidateQueries({ queryKey: ['myDecisions'] });
  void qc.invalidateQueries({ queryKey: ['myTriageDecisions'] });
  if (id) void qc.invalidateQueries({ queryKey: queryKeys.requisicao(id) });
}

export type RequisitionVars = RequisitionInput & { id?: string };

export function useSaveRequisition(options?: UseMutationOptions<Requisition, unknown, RequisitionVars>) {
  return useInvalidatingMutation(
    ({ id, ...input }) => {
      if (id) {
        // On update, unitId/costCenterId are immutable — the backend doesn't accept them.
        const { unitId: _unit, costCenterId: _cc, ...editable } = input;
        return api.updateRequisition(id, editable);
      }
      return api.createRequisition(input);
    },
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}

export function useDeleteRequisition(options?: UseMutationOptions<void, unknown, string>) {
  return useInvalidatingMutation(
    (id) => api.deleteRequisition(id),
    (qc) => invalidateRequisition(qc),
    options,
  );
}

interface TransitionVars {
  id: string;
}
interface ReasonVars {
  id: string;
  reason?: string;
}

export function useSubmitRequisition(options?: UseMutationOptions<Requisition, unknown, TransitionVars>) {
  return useInvalidatingMutation(
    ({ id }) => api.submitRequisition(id),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
/** Controllership approves triage → FinancialComplement (perm requisitions:triage). */
export function useApproveTriage(options?: UseMutationOptions<Requisition, unknown, TransitionVars>) {
  return useInvalidatingMutation(
    ({ id }) => api.approveTriage(id),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export interface SubmitFinancialComplementVars {
  id: string;
  issueDate: string;
  installments: FinancialComplementInstallmentInput[];
}
/** Requester submits the financial complement (authored installments + issue date) → Approval (or returns to Triage). */
export function useSubmitFinancialComplement(
  options?: UseMutationOptions<Requisition, unknown, SubmitFinancialComplementVars>,
) {
  return useInvalidatingMutation(
    ({ id, issueDate, installments }) => api.submitFinancialComplement(id, { issueDate, installments }),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export function useApproveRequisition(options?: UseMutationOptions<Requisition, unknown, TransitionVars>) {
  return useInvalidatingMutation(
    ({ id }) => api.approveRequisition(id),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export function useReturnRequisitionForComplement(options?: UseMutationOptions<Requisition, unknown, ReasonVars>) {
  return useInvalidatingMutation(
    ({ id, reason }) => api.returnRequisitionForComplement(id, reason),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
/** Approver returns a front-loaded requisition to revise the financial plan (Approval → FinancialComplement). */
export function useReturnRequisitionToFinancialComplement(
  options?: UseMutationOptions<Requisition, unknown, ReasonVars>,
) {
  return useInvalidatingMutation(
    ({ id, reason }) => api.returnRequisitionToFinancialComplement(id, reason),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export function useResubmitRequisition(options?: UseMutationOptions<Requisition, unknown, TransitionVars>) {
  return useInvalidatingMutation(
    ({ id }) => api.resubmitRequisition(id),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export function useCancelRequisition(options?: UseMutationOptions<Requisition, unknown, ReasonVars>) {
  return useInvalidatingMutation(
    ({ id, reason }) => api.cancelRequisition(id, reason),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}
export interface AddRecurrenceCycleVars {
  id: string;
  body: AddRecurrenceCycleInput;
  /** Optional NF uploaded together with the invoice (Conta Fixa) — lands on the cycle's Payable (AEL-877). */
  file?: File | null;
}
/** Requester launches the month's invoice on a recurring requisition (AEL-877); consumes the month's ceiling. */
export function useAddRecurrenceCycle(options?: UseMutationOptions<Requisition, unknown, AddRecurrenceCycleVars>) {
  return useInvalidatingMutation(
    ({ id, body, file }) => api.addRecurrenceCycle(id, body, file),
    (qc, data) => {
      invalidateRequisition(qc, data.id);
      void qc.invalidateQueries({ queryKey: ['budgetControl'] }); // the cycle consumed the target month's ceiling
    },
    options,
  );
}
export interface TerminateRecurrenceVars {
  id: string;
  endCompetency: string;
}
/** Terminates a recurring requisition at a competency so it can finalize (AEL-877). */
export function useTerminateRecurrence(options?: UseMutationOptions<Requisition, unknown, TerminateRecurrenceVars>) {
  return useInvalidatingMutation(
    ({ id, endCompetency }) => api.terminateRecurrence(id, endCompetency),
    (qc, data) => invalidateRequisition(qc, data.id),
    options,
  );
}

export function useUploadRequisitionAttachment(
  options?: UseMutationOptions<
    RequisitionAttachment,
    unknown,
    { id: string; file: File; kind?: RequisitionAttachmentKind }
  >,
) {
  return useInvalidatingMutation(
    ({ id, file, kind }) => api.uploadRequisitionAttachment(id, file, kind),
    (qc, _data, vars) => invalidateRequisition(qc, vars.id),
    options,
  );
}
/** Registers an already-uploaded boleto blob as a requisition attachment, by key — no re-upload (AEL-938). */
export function useRegisterRequisitionAttachment(
  options?: UseMutationOptions<
    RequisitionAttachment,
    unknown,
    { id: string } & BoletoBlobRef & { kind?: RequisitionAttachmentKind }
  >,
) {
  return useInvalidatingMutation(
    ({ id, ...body }) => api.registerRequisitionAttachmentByKey(id, body),
    (qc, _data, vars) => invalidateRequisition(qc, vars.id),
    options,
  );
}
export function useDeleteRequisitionAttachment(
  options?: UseMutationOptions<void, unknown, { id: string; attachmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, attachmentId }) => api.deleteRequisitionAttachment(id, attachmentId),
    (qc, _data, vars) => invalidateRequisition(qc, vars.id),
    options,
  );
}
/** Reclassifies an existing attachment's type (requester/controllership fixing a mis-typed file). */
export function useUpdateRequisitionAttachmentKind(
  options?: UseMutationOptions<
    RequisitionAttachment,
    unknown,
    { id: string; attachmentId: string; kind: RequisitionAttachmentKind }
  >,
) {
  return useInvalidatingMutation(
    ({ id, attachmentId, kind }) => api.updateRequisitionAttachmentKind(id, attachmentId, kind),
    (qc, _data, vars) => invalidateRequisition(qc, vars.id),
    options,
  );
}

// ---------- Notifications (AEL-794) ----------

export function useNotifications(page: number, unreadOnly = false) {
  return useQuery({
    queryKey: queryKeys.notifications(page, unreadOnly),
    queryFn: () => api.listNotifications(page, 20, unreadOnly),
    placeholderData: keepPreviousData,
  });
}

/** Bell badge — invalidated by the SSE stream (RealtimeProvider) instead of polling. */
export function useUnreadNotificationsCount() {
  return useQuery({
    queryKey: queryKeys.notificationsUnread,
    queryFn: () => api.unreadNotificationsCount(),
  });
}

function invalidateNotifications(qc: ReturnType<typeof useQueryClient>) {
  void qc.invalidateQueries({ queryKey: ['notifications'] });
  void qc.invalidateQueries({ queryKey: queryKeys.notificationsUnread });
}

export function useMarkNotificationRead(options?: UseMutationOptions<void, unknown, string>) {
  return useInvalidatingMutation(
    (id) => api.markNotificationRead(id),
    (qc) => invalidateNotifications(qc),
    options,
  );
}

export function useMarkAllNotificationsRead(options?: UseMutationOptions<{ count: number }, unknown, void>) {
  return useInvalidatingMutation(
    () => api.markAllNotificationsRead(),
    (qc) => invalidateNotifications(qc),
    options,
  );
}

// ---------- Budget adjustments (Budgeting / AEL-791) ----------

export function useBudgetAdjustments(params: BudgetAdjustmentsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.budgetAdjustments(params),
    queryFn: () => api.listBudgetAdjustments(params),
    enabled: (options?.enabled ?? true) && !!params.companyId,
    placeholderData: keepPreviousData,
  });
}

export function useBudgetAdjustment(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.budgetAdjustment(id ?? ''),
    queryFn: () => api.getBudgetAdjustment(id!),
    enabled: !!id,
  });
}

/** Invalidates the lists + (if id) the adjustment detail, and ALWAYS the budgets — approving/reversing
 *  mutates the Budget ceiling, so the Budget screens must refetch. */
function invalidateBudgetAdjustment(qc: ReturnType<typeof useQueryClient>, id?: string) {
  void qc.invalidateQueries({ queryKey: ['budgetAdjustments'] });
  // The Adequação mutates the budget cells (Core) → invalidates the CC list/detail and the cells.
  void qc.invalidateQueries({ queryKey: ['costCenterBudgets'] });
  void qc.invalidateQueries({ queryKey: ['costCenters'] });
  if (id) void qc.invalidateQueries({ queryKey: queryKeys.budgetAdjustment(id) });
}

export type BudgetAdjustmentVars = BudgetAdjustmentInput & { id?: string };

export function useSaveBudgetAdjustment(options?: UseMutationOptions<BudgetAdjustment, unknown, BudgetAdjustmentVars>) {
  return useInvalidatingMutation(
    ({ id, ...input }) => {
      if (id) {
        // Dimensions (unit/CC) are immutable on update — the backend doesn't accept them.
        const { unitId: _unit, costCenterId: _cc, ...editable } = input;
        return api.updateBudgetAdjustment(id, editable);
      }
      return api.createBudgetAdjustment(input);
    },
    (qc, data) => invalidateBudgetAdjustment(qc, data.id),
    options,
  );
}

export function useDeleteBudgetAdjustment(options?: UseMutationOptions<void, unknown, string>) {
  return useInvalidatingMutation(
    (id) => api.deleteBudgetAdjustment(id),
    (qc) => invalidateBudgetAdjustment(qc),
    options,
  );
}

export function useSendBudgetAdjustmentToApproval(
  options?: UseMutationOptions<BudgetAdjustment, unknown, TransitionVars>,
) {
  return useInvalidatingMutation(
    ({ id }) => api.sendBudgetAdjustmentToApproval(id),
    (qc, data) => invalidateBudgetAdjustment(qc, data.id),
    options,
  );
}
export function useApproveBudgetAdjustment(options?: UseMutationOptions<BudgetAdjustment, unknown, TransitionVars>) {
  return useInvalidatingMutation(
    ({ id }) => api.approveBudgetAdjustment(id),
    (qc, data) => invalidateBudgetAdjustment(qc, data.id),
    options,
  );
}
export function useCancelBudgetAdjustment(options?: UseMutationOptions<BudgetAdjustment, unknown, ReasonVars>) {
  return useInvalidatingMutation(
    ({ id, reason }) => api.cancelBudgetAdjustment(id, reason),
    (qc, data) => invalidateBudgetAdjustment(qc, data.id),
    options,
  );
}
export function useReverseBudgetAdjustment(options?: UseMutationOptions<BudgetAdjustment, unknown, ReasonVars>) {
  return useInvalidatingMutation(
    ({ id, reason }) => api.reverseBudgetAdjustment(id, reason),
    (qc, data) => invalidateBudgetAdjustment(qc, data.id),
    options,
  );
}

export function useCostCenters(companyId: string) {
  // Consolidated mode (AEL-906): fetch every company's cost centers (shares the useAllCostCenters cache key).
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.costCenters(all ? 'all' : companyId),
    queryFn: () => (all ? api.listAllCostCenters() : api.listCostCenters(companyId)),
    enabled: all || !!companyId,
  });
}

/** Cost centers of a company restricted to the caller's scope (AEL-883) — the requisition form only offers
 * the CCs a scoped requester may use (Admin / non-scoped roles get all). Prefix-matched by the CC-CRUD
 * invalidations (`['costCenters', companyId]`). */
export function useScopedCostCenters(companyId: string) {
  return useQuery({
    queryKey: [...queryKeys.costCenters(companyId), 'scoped'] as const,
    queryFn: () => api.listCostCenters(companyId, true),
    enabled: !!companyId,
  });
}

export function useCostCenter(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.costCenter(id ?? ''),
    queryFn: () => api.getCostCenter(id!),
    enabled: !!id,
  });
}

export function useTransactions(params: TransactionsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.transactions(params),
    queryFn: () => api.listTransactions(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useTransaction(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.transaction(id ?? ''),
    queryFn: () => api.getTransaction(id!),
    enabled: !!id,
  });
}

// Vendors are global to the organization — the list is not company-scoped. `enabled` lets callers gate
// the fetch (e.g. disable it while a receivable/customer counterparty is being picked).
export function useVendors(search?: string, page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.vendors(search, page),
    queryFn: () => api.listVendors(search, page),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useVendor(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.vendor(id ?? ''),
    queryFn: () => api.getVendor(id!),
    enabled: !!id && enabled,
  });
}

export function useCustomers(companyId: string, search?: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.customers(companyId, search, page),
    queryFn: () => api.listCustomers(companyId, search, page),
    enabled: !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function useEmployees(companyId: string, search?: string, page = 1) {
  return useQuery({
    queryKey: queryKeys.employees(companyId, search, page),
    queryFn: () => api.listEmployees(companyId, search, page),
    enabled: !!companyId,
    placeholderData: keepPreviousData,
  });
}

export function usePayables(params: PayablesParams, options?: { enabled?: boolean }) {
  // Consolidated mode (AEL-906): omit companyId so /payables returns every company (sentinel isn't a Guid).
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.payables(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.listPayables(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function usePayable(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payable(id ?? ''),
    queryFn: () => api.getPayable(id!),
    enabled: !!id,
  });
}

// Contábil/Finance work queues must stay live: a requisition's payable is created async (outbox) after
// approval and never invalidates these queries, so the 30s default staleTime would hide a just-arrived item
// until it elapsed. staleTime 0 → refetch on every mount / company switch (AEL-906 Contábil review fix).
const WORK_QUEUE_STALE_TIME = 0;

export function usePayableStageSummary(companyId: string | undefined) {
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.payableStageSummary(all ? 'all' : (companyId ?? '')),
    queryFn: () => api.payableStageSummary(all ? undefined : companyId),
    enabled: all || !!companyId,
    staleTime: WORK_QUEUE_STALE_TIME,
  });
}

/** Relação de Pagamentos (AEL-958): filtered per-installment list. */
export function usePaymentsReport(params: PaymentsReportListParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.paymentsReport(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.paymentsReport(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

/** Whole-set aggregate behind the "Copiar Resumo" button (AEL-958). */
export function usePaymentsReportSummary(params: PaymentsReportParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.paymentsReportSummary(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.paymentsReportSummary(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

/** NEW (AEL-871): whole-set group-header aggregates for the payment queue. */
export function usePaymentQueueSummary(params: PaymentQueueSummaryParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.paymentQueueSummary(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.paymentQueueSummary(all ? { ...params, companyId: undefined } : params),
    enabled: (options?.enabled ?? true) && (all || !!params.companyId),
    placeholderData: keepPreviousData,
  });
}

/** NEW (AEL-871): cross-payable settlement history (Histórico de baixas). */
export function usePaymentsHistory(params: PaymentsHistoryParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.paymentsHistory(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.listPayments(all ? { ...params, companyId: undefined } : params),
    enabled: (options?.enabled ?? true) && (all || !!params.companyId),
    placeholderData: keepPreviousData,
  });
}

export function usePayableStageItems(params: PayableStageItemsParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.payableStageItems(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.payableStageItems(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? (all || !!params.companyId),
    placeholderData: keepPreviousData,
    staleTime: WORK_QUEUE_STALE_TIME,
  });
}

export function usePayableAuditTrail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payableAuditTrail(id ?? ''),
    queryFn: () => api.payableAuditTrail(id!),
    enabled: !!id,
  });
}

export function usePayableAccountingView(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payableAccountingView(id ?? ''),
    queryFn: () => api.payableAccountingView(id!),
    enabled: !!id,
  });
}

export function useReceivables(params: ReceivablesParams, options?: { enabled?: boolean }) {
  // Consolidated mode (AEL-906): omit companyId so /receivables returns every company (sentinel isn't a Guid).
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.receivables(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.listReceivables(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useReceivable(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.receivable(id ?? ''),
    queryFn: () => api.getReceivable(id!),
    enabled: !!id,
  });
}

export function useReceivableStageSummary(companyId: string | undefined) {
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.receivableStageSummary(all ? 'all' : (companyId ?? '')),
    queryFn: () => api.receivableStageSummary(all ? undefined : companyId),
    enabled: all || !!companyId,
  });
}

export function useReceivableStageItems(params: ReceivableStageItemsParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.receivableStageItems(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.receivableStageItems(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? (all || !!params.companyId),
    placeholderData: keepPreviousData,
  });
}

export function useReceivableAuditTrail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.receivableAuditTrail(id ?? ''),
    queryFn: () => api.receivableAuditTrail(id!),
    enabled: !!id,
  });
}

export function useExpenseCategories(includeInactive = false) {
  return useQuery({
    queryKey: queryKeys.expenseCategories(includeInactive),
    queryFn: () => api.listExpenseCategories(includeInactive),
  });
}

export function useWithholdingRules(includeInactive = false) {
  return useQuery({
    queryKey: queryKeys.withholdingRules(includeInactive),
    queryFn: () => api.listWithholdingRules(includeInactive),
  });
}

export function useWithholdingPreview(params: WithholdingPreviewParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.withholdingPreview(params),
    queryFn: () => api.previewWithholdingRules(params),
    enabled: options?.enabled ?? true,
  });
}

export function useBankAccounts(companyId: string, includeInactive = false) {
  return useQuery({
    queryKey: queryKeys.bankAccounts(companyId, includeInactive),
    queryFn: () => api.listBankAccounts(companyId, includeInactive),
    enabled: !!companyId,
  });
}

export function useStatementImports(companyId?: string, bankAccountId?: string) {
  return useQuery({
    queryKey: queryKeys.statementImports(companyId, bankAccountId),
    queryFn: () => api.listStatementImports(companyId, bankAccountId),
  });
}

export function usePeriodSummary(params: PeriodSummaryParams, options?: { enabled?: boolean }) {
  return useQuery<PeriodSummary>({
    queryKey: queryKeys.periodSummary(params),
    queryFn: () => api.periodSummary(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useBankTransactions(params: BankTransactionsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.bankTransactions(params),
    queryFn: () => api.listBankTransactions(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

/* ===================== Mutations ===================== */

/** Thin wrapper so call sites can pass an `onSuccess` while we own cache invalidation. */
function useInvalidatingMutation<TData, TVars>(
  mutationFn: (vars: TVars) => Promise<TData>,
  invalidate: (qc: ReturnType<typeof useQueryClient>, data: TData, vars: TVars) => void,
  options?: UseMutationOptions<TData, unknown, TVars>,
) {
  const qc = useQueryClient();
  return useMutation<TData, unknown, TVars>({
    mutationFn,
    ...options,
    onSuccess: (data, vars, ...rest) => {
      invalidate(qc, data, vars);
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Companies ---------- */
export function useCreateCompany(options?: UseMutationOptions<Company, unknown, { legalName: string; cnpj: string }>) {
  return useInvalidatingMutation(
    ({ legalName, cnpj }) => api.createCompany(legalName, cnpj),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.companies }),
    options,
  );
}

export function useUpdateCompany(
  options?: UseMutationOptions<Company, unknown, { id: string; legalName: string; cnpj: string }>,
) {
  return useInvalidatingMutation(
    ({ id, legalName, cnpj }) => api.updateCompany(id, legalName, cnpj),
    (qc) => void qc.invalidateQueries({ queryKey: queryKeys.companies }),
    options,
  );
}

// Inactivate/reactivate (core:write) or delete (admin-only, soft-delete), keyed on action. The caller
// (estrutura.tsx) owns the refresh via refreshEmpresas — which also re-selects after a delete — so no invalidate here.
export function useCompanyAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeCompany(id)
        : action === 'activate'
          ? api.activateCompany(id)
          : api.deactivateCompany(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Units ---------- */
export function useCreateUnit(
  options?: UseMutationOptions<
    Unit,
    unknown,
    { companyId: string; name: string; acronym: string; cnpj?: string | null; isMatriz?: boolean }
  >,
) {
  return useInvalidatingMutation(
    ({ companyId, name, acronym, cnpj, isMatriz }) => api.createUnit(companyId, name, acronym, cnpj, isMatriz),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.units(data.companyId) }),
    options,
  );
}

export function useUpdateUnit(
  options?: UseMutationOptions<
    Unit,
    unknown,
    { id: string; name: string; acronym: string; cnpj?: string | null; isMatriz?: boolean }
  >,
) {
  return useInvalidatingMutation(
    ({ id, name, acronym, cnpj, isMatriz }) => api.updateUnit(id, name, acronym, cnpj, isMatriz),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.units(data.companyId) }),
    options,
  );
}

/* ---------- Cost centers ---------- */
export interface CostCenterVars {
  id?: string;
  companyId: string;
  code: string;
  name: string;
  autoApprovalLimit: number;
  unitIds: string[];
}

export function useSaveCostCenter(options?: UseMutationOptions<CostCenter, unknown, CostCenterVars>) {
  return useInvalidatingMutation(
    (v) =>
      v.id
        ? api.updateCostCenter(v.id, v.code, v.name, v.autoApprovalLimit, v.unitIds)
        : api.createCostCenter(v.companyId, v.code, v.name, v.autoApprovalLimit, v.unitIds),
    (qc, data) => {
      void qc.invalidateQueries({ queryKey: queryKeys.costCenters(data.companyId) });
      void qc.invalidateQueries({ queryKey: queryKeys.costCenter(data.id) });
    },
    options,
  );
}

/* ---------- Transactions ---------- */
export function useCreateTransaction(
  options?: UseMutationOptions<
    Transaction,
    unknown,
    {
      companyId: string;
      unitId: string;
      costCenterId: string;
      type: TransactionType;
      description: string;
      vendorId?: string | null;
      customerId?: string | null;
      amount: number;
      dueDate: string;
    }
  >,
) {
  return useInvalidatingMutation(
    (body) => api.createTransaction(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['transactions'] }),
    options,
  );
}

/** Invalidates list + single caches after a transaction state change. */
function invalidateTransaction(qc: ReturnType<typeof useQueryClient>, data: Transaction) {
  void qc.invalidateQueries({ queryKey: ['transactions'] });
  void qc.invalidateQueries({ queryKey: queryKeys.transaction(data.id) });
}

export function useApproveTransaction(options?: UseMutationOptions<Transaction, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.approveTransaction(id), invalidateTransaction, options);
}

export function useRejectTransaction(
  options?: UseMutationOptions<Transaction, unknown, { id: string; reason: string }>,
) {
  return useInvalidatingMutation(({ id, reason }) => api.rejectTransaction(id, reason), invalidateTransaction, options);
}

export function usePayTransaction(options?: UseMutationOptions<Transaction, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.payTransaction(id), invalidateTransaction, options);
}

/* ---------- Vendors ---------- */
export function useSaveVendor(options?: UseMutationOptions<Vendor, unknown, { id?: string; body: VendorInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateVendor(id, body) : api.createVendor(body)),
    // Vendors are global to the organization — invalidate every vendors list.
    (qc) => void qc.invalidateQueries({ queryKey: ['vendors'] }),
    options,
  );
}

export function useVendorAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeVendor(id)
        : action === 'activate'
          ? api.activateVendor(id)
          : api.deactivateVendor(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['vendors'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Customers ---------- */
export function useSaveCustomer(options?: UseMutationOptions<Customer, unknown, { id?: string; body: CustomerInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateCustomer(id, body) : api.createCustomer(body)),
    (qc, data) => void qc.invalidateQueries({ queryKey: ['customers', data.companyId] }),
    options,
  );
}

export function useCustomerAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeCustomer(id)
        : action === 'activate'
          ? api.activateCustomer(id)
          : api.deactivateCustomer(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['customers'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Employees ---------- */
export function useSaveEmployee(options?: UseMutationOptions<Employee, unknown, { id?: string; body: EmployeeInput }>) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateEmployee(id, body) : api.createEmployee(body)),
    (qc, data) => void qc.invalidateQueries({ queryKey: ['employees', data.companyId] }),
    options,
  );
}

export function useEmployeeAction(
  options?: UseMutationOptions<unknown, unknown, { id: string; action: 'activate' | 'deactivate' | 'delete' }>,
) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'activate' | 'deactivate' | 'delete' }) =>
      action === 'delete'
        ? api.removeEmployee(id)
        : action === 'activate'
          ? api.activateEmployee(id)
          : api.deactivateEmployee(id),
    ...options,
    onSuccess: (data, vars, ...rest) => {
      void qc.invalidateQueries({ queryKey: ['employees'] });
      options?.onSuccess?.(data, vars, ...rest);
    },
  });
}

/* ---------- Payables ---------- */
/** Invalidates the payables lists + the single payable cache after a state change. */
function invalidatePayable(qc: ReturnType<typeof useQueryClient>, data: Payable) {
  void qc.invalidateQueries({ queryKey: ['payables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.payable(data.id) });
  // The timeline (Linha do Tempo) is a separate query — refresh it so a state change (devolução, aprovação,
  // baixa…) shows on the same screen without a manual reload (AEL-935).
  void qc.invalidateQueries({ queryKey: queryKeys.payableAuditTrail(data.id) });
}

/** Reject/return of a requisition-sourced payable bounces the requisition back (AEL-935). The requisition
 *  and payable views can share one client cache (single session / admin "Ver como"), so also mark the origin
 *  requisition stale — otherwise its histórico + reason banner keep serving the pre-return copy. */
function invalidatePayableAndOriginRequisition(qc: ReturnType<typeof useQueryClient>, data: Payable) {
  invalidatePayable(qc, data);
  if (data.originRequisitionId) invalidateRequisition(qc, data.originRequisitionId);
}

export function useCreatePayable(options?: UseMutationOptions<Payable, unknown, PayableInput>) {
  return useInvalidatingMutation(
    (body) => api.createPayable(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['payables'] }),
    options,
  );
}

export function useUpdatePayable(
  options?: UseMutationOptions<Payable, unknown, { id: string; body: Omit<PayableInput, 'origin'> }>,
) {
  return useInvalidatingMutation(({ id, body }) => api.updatePayable(id, body), invalidatePayable, options);
}

export function useApprovePayable(options?: UseMutationOptions<Payable, unknown, { id: string }>) {
  return useInvalidatingMutation(({ id }) => api.approvePayable(id), invalidatePayable, options);
}

export function useConfirmAccounting(options?: UseMutationOptions<Payable, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.confirmAccounting(id),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
    },
    options,
  );
}

/** "Liberar para pagamento": the tax reviewer confirms the tax analysis → Scheduled + PayableScheduled
 * (accounting incurral). Same cache surface as confirming accounting (also invalidates the withholdings report). */
export function useConfirmTaxAnalysis(options?: UseMutationOptions<Payable, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.confirmTaxAnalysis(id),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
      void qc.invalidateQueries({ queryKey: ['withholdingsReport'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
    },
    options,
  );
}

/** "Solicitar NF": pings the requester (via notification). No local cache changes — the payable stays
 * NF-pending until the requester uploads the NF and it propagates back. */
export function useRequestFiscalDocument(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.requestFiscalDocument(id),
    () => {},
    options,
  );
}

export function useRejectPayable(
  options?: UseMutationOptions<Payable, unknown, { id: string; reason: string; returnTo?: PayableReturnTarget }>,
) {
  return useInvalidatingMutation(
    ({ id, reason, returnTo }) => api.rejectPayable(id, reason, returnTo),
    invalidatePayableAndOriginRequisition,
    options,
  );
}

export function useReturnPayable(
  options?: UseMutationOptions<Payable, unknown, { id: string; reason: string; returnTo: PayableReturnTarget }>,
) {
  return useInvalidatingMutation(
    ({ id, reason, returnTo }) => api.returnPayable(id, reason, returnTo),
    invalidatePayableAndOriginRequisition,
    options,
  );
}

export function useCancelPayable(options?: UseMutationOptions<Payable, unknown, { id: string; reason: string }>) {
  return useInvalidatingMutation(({ id, reason }) => api.cancelPayable(id, reason), invalidatePayable, options);
}

export function useUploadPayableAttachment(
  options?: UseMutationOptions<PayableAttachment, unknown, { id: string; file: File; kind?: PayableAttachmentKind }>,
) {
  return useInvalidatingMutation(
    ({ id, file, kind }) => api.uploadPayableAttachment(id, file, kind),
    (qc, _data, vars) => {
      // An attached document can satisfy the NF gate (Pendências "Nota Fiscal" tab), so refresh the stage
      // lists/counts and the accounting view too — not just the payable + list.
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
    },
    options,
  );
}

export function useDeletePayableAttachment(
  options?: UseMutationOptions<void, unknown, { id: string; attachmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, attachmentId }) => api.deletePayableAttachment(id, attachmentId),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
    },
    options,
  );
}

export interface WithholdingItem {
  taxType: WithholdingTaxType;
  baseAmount: number;
  rate?: number | null;
  amount: number;
}

export function useSetPayableWithholdings(
  options?: UseMutationOptions<Payable, unknown, { id: string; items: WithholdingItem[] }>,
) {
  return useInvalidatingMutation(
    ({ id, items }) => api.setPayableWithholdings(id, items),
    (qc, _d, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      // The withholdings report and the fiscal assessment aggregate withholdings across payables — otherwise the
      // consolidated "Retenções" screen keeps a stale cache and a just-added retention (e.g. ISS) won't show.
      void qc.invalidateQueries({ queryKey: ['withholdingsReport'] });
      void qc.invalidateQueries({ queryKey: ['fiscalAssessment'] });
    },
    options,
  );
}

export function useHoldInstallment(
  options?: UseMutationOptions<void, unknown, { id: string; installmentId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ id, installmentId, reason }) => api.holdInstallment(id, installmentId, reason),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
    },
    options,
  );
}

export function useReleaseInstallmentHold(
  options?: UseMutationOptions<void, unknown, { id: string; installmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, installmentId }) => api.releaseInstallmentHold(id, installmentId),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
    },
    options,
  );
}

/** Requests a presigned/SAS PUT URL for uploading a fiscal document directly to blob storage. */
export function useRequestFiscalUploadUrl(
  options?: UseMutationOptions<UploadTarget, unknown, { companyId: string; fileName: string }>,
) {
  return useMutation<UploadTarget, unknown, { companyId: string; fileName: string }>({
    mutationFn: ({ companyId, fileName }) => api.requestFiscalUploadUrl(companyId, fileName),
    ...options,
  });
}

/**
 * PUTs the file directly to the presigned/SAS upload URL returned by `useRequestFiscalUploadUrl`.
 * The `x-ms-blob-type` header is required by Azure Blob SAS.
 */
export async function uploadToPresignedUrl(uploadUrl: string, file: File): Promise<void> {
  const res = await fetch(uploadUrl, {
    method: 'PUT',
    body: file,
    headers: { 'Content-Type': file.type, 'x-ms-blob-type': 'BlockBlob' },
  });
  if (!res.ok) throw new Error(`Upload failed: ${res.status} ${res.statusText}`);
}

/** Stateless parse/preview of an NF already in object storage — no cache to invalidate. */
export function useParseFiscalDocument(
  options?: UseMutationOptions<ParsedFiscalDocument, unknown, { companyId: string; key: string; fileName: string }>,
) {
  return useMutation<ParsedFiscalDocument, unknown, { companyId: string; key: string; fileName: string }>({
    mutationFn: ({ companyId, key, fileName }) => api.parseFiscalDocument(companyId, key, fileName),
    ...options,
  });
}

/** Presigned upload URL for a boleto PDF/image (AEL-938). Reuse uploadToPresignedUrl for the PUT. */
export function useRequestBoletoUploadUrl(
  options?: UseMutationOptions<UploadTarget, unknown, { companyId: string; fileName: string }>,
) {
  return useMutation<UploadTarget, unknown, { companyId: string; fileName: string }>({
    mutationFn: ({ companyId, fileName }) => api.requestBoletoUploadUrl(companyId, fileName),
    ...options,
  });
}

/** Stateless AI parse/preview of a boleto already in object storage — no cache to invalidate. */
export function useParseBoleto(
  options?: UseMutationOptions<ParsedBoleto, unknown, { companyId: string; key: string; fileName: string }>,
) {
  return useMutation<ParsedBoleto, unknown, { companyId: string; key: string; fileName: string }>({
    mutationFn: ({ companyId, key, fileName }) => api.parseBoleto(companyId, key, fileName),
    ...options,
  });
}

export function useUploadPayableFiscalDocument(
  options?: UseMutationOptions<
    FiscalDocument,
    unknown,
    { id: string; key: string; fileName: string; contentType: string; sizeBytes: number }
  >,
) {
  return useInvalidatingMutation(
    ({ id, key, fileName, contentType, sizeBytes }) =>
      api.uploadPayableFiscalDocument(id, { key, fileName, contentType, sizeBytes }),
    (qc, _data, vars) => {
      // Mirror useConfirmAccounting: an attached NF flips hasFiscalDocument, so the Pendências
      // "Nota Fiscal" list/count and the accounting view must refresh too.
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
    },
    options,
  );
}

/* ---------- Expense categories ---------- */
export function useSaveExpenseCategory(
  options?: UseMutationOptions<ExpenseCategory, unknown, { id?: string; name: string; active?: boolean }>,
) {
  return useInvalidatingMutation(
    ({ id, name, active }) =>
      id ? api.updateExpenseCategory(id, name, active ?? true) : api.createExpenseCategory(name),
    (qc) => void qc.invalidateQueries({ queryKey: ['expenseCategories'] }),
    options,
  );
}

export function useDeleteExpenseCategory(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteExpenseCategory(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['expenseCategories'] }),
    options,
  );
}

/* ---------- Withholding rules ---------- */
export function useSaveWithholdingRule(
  options?: UseMutationOptions<WithholdingRule, unknown, { id?: string; body: WithholdingRuleInput }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => (id ? api.updateWithholdingRule(id, body) : api.createWithholdingRule(body)),
    (qc) => void qc.invalidateQueries({ queryKey: ['withholdingRules'] }),
    options,
  );
}

export function useDeleteWithholdingRule(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteWithholdingRule(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['withholdingRules'] }),
    options,
  );
}

/* ---------- Bank accounts ---------- */
export function useSaveBankAccount(
  options?: UseMutationOptions<
    BankAccount,
    unknown,
    { id?: string; create?: BankAccountInput; update?: BankAccountUpdateInput }
  >,
) {
  return useInvalidatingMutation(
    ({ id, create, update }) => (id ? api.updateBankAccount(id, update!) : api.createBankAccount(create!)),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.bankAccounts(data.companyId) }),
    options,
  );
}

export function useDeleteBankAccount(options?: UseMutationOptions<void, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.deleteBankAccount(id),
    (qc) => void qc.invalidateQueries({ queryKey: ['bankAccounts'] }),
    options,
  );
}

/* ---------- Payments (settlement) ---------- */
/** Invalidates the payables lists + the single payable cache after a payment change. */
function invalidatePayablePayment(qc: ReturnType<typeof useQueryClient>, payableId: string) {
  void qc.invalidateQueries({ queryKey: ['payables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.payable(payableId) });
  // The treasury home derives its group headers/aside from the queue summary and lists the settlement
  // in the history tab — both change on register/reverse.
  void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
  void qc.invalidateQueries({ queryKey: ['paymentsHistory'] });
  // A payment moves cash (actual outflow + account balance) — keep cash flow fresh.
  void qc.invalidateQueries({ queryKey: ['cashFlow'] });
  void qc.invalidateQueries({ queryKey: ['cashFlowItems'] });
  // A settlement realizes (or, when reversed, un-realizes) the CSRF retentions → refresh the withholdings report
  // (withheld × to withhold) and the fiscal assessment (CSRF is attributed by payment date).
  void qc.invalidateQueries({ queryKey: ['withholdingsReport'] });
  void qc.invalidateQueries({ queryKey: ['fiscalAssessment'] });
}

export function useRegisterPayment(
  options?: UseMutationOptions<
    Payable,
    unknown,
    { payableId: string; installmentId: string; body: RegisterPaymentInput }
  >,
) {
  return useInvalidatingMutation(
    ({ payableId, installmentId, body }) => api.registerPayment(payableId, installmentId, body),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

export function useReversePayment(
  options?: UseMutationOptions<Payable, unknown, { payableId: string; paymentId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ payableId, paymentId, reason }) => api.reversePayment(payableId, paymentId, reason),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

export function useUploadPaymentProof(
  options?: UseMutationOptions<Payment, unknown, { payableId: string; paymentId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ payableId, paymentId, file }) => api.uploadPaymentProof(payableId, paymentId, file),
    (qc, _data, vars) => invalidatePayablePayment(qc, vars.payableId),
    options,
  );
}

/* ---------- Receivables (accounts receivable) ---------- */
/** Invalidates the receivables lists + the single receivable cache after a state change. */
function invalidateReceivable(qc: ReturnType<typeof useQueryClient>, data: Receivable) {
  void qc.invalidateQueries({ queryKey: ['receivables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.receivable(data.id) });
}

export function useCreateReceivable(options?: UseMutationOptions<Receivable, unknown, ReceivableInput>) {
  return useInvalidatingMutation(
    (body) => api.createReceivable(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['receivables'] }),
    options,
  );
}

/* ---------- OFX reconciliation (statement imports + bank transactions) ---------- */
/** Invalidates the bank-transaction lists + the statement-import lists after a match/settlement change.
 * Also invalidates payables, since confirming/matching a transaction registers a settlement. */
function invalidateConciliation(qc: ReturnType<typeof useQueryClient>) {
  void qc.invalidateQueries({ queryKey: ['bankTransactions'] });
  void qc.invalidateQueries({ queryKey: ['periodSummary'] });
  void qc.invalidateQueries({ queryKey: ['statementImports'] });
  void qc.invalidateQueries({ queryKey: ['payables'] });
}

export function useClearReconciliation(
  options?: UseMutationOptions<
    { ignored: number },
    unknown,
    { companyId: string; bankAccountId: string; date: string }
  >,
) {
  return useInvalidatingMutation((body) => api.clearReconciliation(body), invalidateConciliation, options);
}

export function useImportStatement(
  options?: UseMutationOptions<StatementImport, unknown, { bankAccountId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ bankAccountId, file }) => api.importStatement(bankAccountId, file),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['bankTransactions'] });
      void qc.invalidateQueries({ queryKey: ['periodSummary'] });
      void qc.invalidateQueries({ queryKey: ['statementImports'] });
    },
    options,
  );
}

export function useUpdateReceivable(
  options?: UseMutationOptions<Receivable, unknown, { id: string; body: Omit<ReceivableInput, 'origin'> }>,
) {
  return useInvalidatingMutation(({ id, body }) => api.updateReceivable(id, body), invalidateReceivable, options);
}

export function useCancelReceivable(options?: UseMutationOptions<Receivable, unknown, { id: string; reason: string }>) {
  return useInvalidatingMutation(({ id, reason }) => api.cancelReceivable(id, reason), invalidateReceivable, options);
}

/* ---------- Receipts (inbound settlement) ---------- */
/** Invalidates the receivables lists + the single receivable cache after a receipt change. */
function invalidateReceivableReceipt(qc: ReturnType<typeof useQueryClient>, receivableId: string) {
  void qc.invalidateQueries({ queryKey: ['receivables'] });
  void qc.invalidateQueries({ queryKey: queryKeys.receivable(receivableId) });
  // A receipt moves cash (actual inflow + account balance) — keep cash flow fresh.
  void qc.invalidateQueries({ queryKey: ['cashFlow'] });
  void qc.invalidateQueries({ queryKey: ['cashFlowItems'] });
}

export function useRegisterReceipt(
  options?: UseMutationOptions<
    Receivable,
    unknown,
    { receivableId: string; installmentId: string; body: RegisterReceiptInput }
  >,
) {
  return useInvalidatingMutation(
    ({ receivableId, installmentId, body }) => api.registerReceipt(receivableId, installmentId, body),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

export function useConfirmMatch(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.confirmMatch(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useMatchTransaction(
  options?: UseMutationOptions<BankTransaction, unknown, { id: string; installmentId: string }>,
) {
  return useInvalidatingMutation(
    ({ id, installmentId }) => api.matchTransaction(id, installmentId),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useIgnoreTransaction(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.ignoreTransaction(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useUnmatchTransaction(options?: UseMutationOptions<BankTransaction, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.unmatchTransaction(id),
    (qc) => invalidateConciliation(qc),
    options,
  );
}

export function useAging(companyId: string | undefined, asOf?: string) {
  const all = companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.aging(all ? 'all' : (companyId ?? ''), asOf),
    queryFn: () => api.aging(all ? undefined : companyId, asOf),
    enabled: all || !!companyId,
  });
}

export function useAgingItems(params: AgingItemsParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.agingItems(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.agingItems(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? (all || !!params.companyId),
  });
}

/* ---------- Cash flow ---------- */
export function useCashFlow(params: CashFlowParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.cashFlow(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.cashFlow(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

/* ---------- SLA report (SLA analysis — AEL-872) ---------- */
export function useSlaReport(params: SlaReportParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.slaReport(params),
    queryFn: () => api.slaReport(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useCashFlowItems(params: CashFlowItemsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.cashFlowItems(params),
    queryFn: () => api.cashFlowItems(params),
    enabled: options?.enabled ?? true,
  });
}

export function useSetReconciledBalance(
  options?: UseMutationOptions<
    Awaited<ReturnType<typeof api.setReconciledBalance>>,
    unknown,
    { bankAccountId: string; balance: number; asOf: string }
  >,
) {
  return useInvalidatingMutation(
    ({ bankAccountId, balance, asOf }) => api.setReconciledBalance(bankAccountId, { balance, asOf }),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['cashFlow'] });
      void qc.invalidateQueries({ queryKey: ['cashFlowItems'] });
      void qc.invalidateQueries({ queryKey: ['bankAccounts'] });
    },
    options,
  );
}

export function useReverseReceipt(
  options?: UseMutationOptions<Receivable, unknown, { receivableId: string; receiptId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ receivableId, receiptId, reason }) => api.reverseReceipt(receivableId, receiptId, reason),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

export function useUploadReceiptProof(
  options?: UseMutationOptions<Receipt, unknown, { receivableId: string; receiptId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ receivableId, receiptId, file }) => api.uploadReceiptProof(receivableId, receiptId, file),
    (qc, _data, vars) => invalidateReceivableReceipt(qc, vars.receivableId),
    options,
  );
}

/* ---------- Classification ---------- */
export function useClassifyPayable(
  options?: UseMutationOptions<Payable, unknown, { id: string; body: { accountingAccountId?: string | null } }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.classifyPayable(id, body),
    (qc, _data, vars) => {
      // ['payables'] prefix-matches the LIST keys but NOT ['payable', id] (usePayable). Invalidate the single
      // payable explicitly so the accounting drawer's retentions/account (bound to `p`) refresh after reclassify,
      // plus the accounting view and stage lists (classify can also auto-approve → status/bucket changes).
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
    },
    options,
  );
}

export function useSetPayableDocumentNumber(
  options?: UseMutationOptions<Payable, unknown, { id: string; documentNumber: string | null }>,
) {
  return useInvalidatingMutation(
    ({ id, documentNumber }) => api.setPayableDocumentNumber(id, documentNumber),
    (qc, _data, vars) => {
      // The número is read from `p` (usePayable) in the drawers and echoed by the accounting view + stage lists.
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: queryKeys.payable(vars.id) });
      void qc.invalidateQueries({ queryKey: queryKeys.payableAccountingView(vars.id) });
      void qc.invalidateQueries({ queryKey: ['payableStageItems'] });
      void qc.invalidateQueries({ queryKey: ['payableStageSummary'] });
    },
    options,
  );
}

export function useClassifyBatch(
  options?: UseMutationOptions<{ classifiedCount: number }, unknown, Parameters<typeof api.classifyBatch>[0]>,
) {
  return useInvalidatingMutation(
    (body) => api.classifyBatch(body),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['payables'] });
    },
    options,
  );
}

export function useAiClassificationSuggestion(
  options?: UseMutationOptions<AiClassificationSuggestion, unknown, string>,
) {
  return useMutation({
    mutationFn: (payableId: string) => api.aiClassificationSuggestion(payableId),
    ...options,
  });
}

export function useClassificationSuggestion(payableId: string | undefined, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['classificationSuggestion', payableId ?? ''],
    queryFn: () => api.classificationSuggestion(payableId!),
    enabled: (options?.enabled ?? true) && !!payableId,
  });
}

/* ---------- Classification Policies ---------- */
export function useClassificationPolicy(companyId: string) {
  return useQuery({
    queryKey: queryKeys.classificationPolicy(companyId),
    queryFn: () => api.getClassificationPolicy(companyId),
    enabled: !!companyId,
  });
}

export function useUpdateClassificationPolicy(
  options?: UseMutationOptions<
    ClassificationPolicy,
    unknown,
    { companyId: string; body: { requireToApprove: boolean; requireToPay: boolean } }
  >,
) {
  return useInvalidatingMutation(
    ({ companyId, body }) => api.updateClassificationPolicy(companyId, body),
    (qc, data) => void qc.invalidateQueries({ queryKey: queryKeys.classificationPolicy(data.companyId) }),
    options,
  );
}

/* ---------- Accounting: Ledger accounts (accounting chart of accounts) ---------- */
export function useLedgerAccounts(params: LedgerAccountsParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.ledgerAccounts(params),
    queryFn: () => api.listLedgerAccounts(params),
    enabled: options?.enabled ?? true,
  });
}

export function useCreateLedgerAccount(options?: UseMutationOptions<LedgerAccount, unknown, LedgerAccountInput>) {
  return useInvalidatingMutation(
    (body) => api.createLedgerAccount(body),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

export function useUpdateLedgerAccount(
  options?: UseMutationOptions<LedgerAccount, unknown, { id: string; body: LedgerAccountUpdateInput }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.updateLedgerAccount(id, body),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

export function useSeedLedgerAccounts(options?: UseMutationOptions<LedgerAccount[], unknown, void>) {
  return useInvalidatingMutation(
    () => api.seedLedgerAccounts(),
    (qc) => void qc.invalidateQueries({ queryKey: ['ledgerAccounts'] }),
    options,
  );
}

/* ---------- Accounting: Journal entries ---------- */
export function useJournalEntries(params: JournalEntriesParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.journalEntries(params),
    queryFn: () => api.listJournalEntries(params),
    enabled: options?.enabled ?? true,
  });
}

export function useJournalEntry(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.journalEntry(id ?? ''),
    queryFn: () => api.getJournalEntry(id!),
    enabled: !!id,
  });
}

/** Invalidates the journal lists + single cache + every accounting report after a change. */
function invalidateJournal(qc: ReturnType<typeof useQueryClient>, entryId?: string) {
  void qc.invalidateQueries({ queryKey: ['journalEntries'] });
  if (entryId) void qc.invalidateQueries({ queryKey: queryKeys.journalEntry(entryId) });
  void qc.invalidateQueries({ queryKey: ['trialBalance'] });
  void qc.invalidateQueries({ queryKey: ['incomeStatement'] });
  void qc.invalidateQueries({ queryKey: ['balanceSheet'] });
}

export function useCreateJournalEntry(options?: UseMutationOptions<JournalEntry, unknown, JournalEntryInput>) {
  return useInvalidatingMutation(
    (body) => api.createJournalEntry(body),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function useUpdateJournalEntry(
  options?: UseMutationOptions<JournalEntry, unknown, { id: string; body: Omit<JournalEntryInput, 'companyId'> }>,
) {
  return useInvalidatingMutation(
    ({ id, body }) => api.updateJournalEntry(id, body),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function usePostJournalEntry(options?: UseMutationOptions<JournalEntry, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.postJournalEntry(id),
    (qc, data) => invalidateJournal(qc, data.id),
    options,
  );
}

export function useReverseJournalEntry(
  options?: UseMutationOptions<JournalEntry, unknown, { id: string; reason: string; competenceDate?: string | null }>,
) {
  return useInvalidatingMutation(
    ({ id, reason, competenceDate }) => api.reverseJournalEntry(id, reason, competenceDate),
    (qc, _data, vars) => invalidateJournal(qc, vars.id),
    options,
  );
}

/* ---------- Accounting: Reports ---------- */
export function useTrialBalance(params: TrialBalanceParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.trialBalance(params),
    queryFn: () => api.trialBalance(params),
    enabled: options?.enabled ?? true,
  });
}

export function useIncomeStatement(params: IncomeStatementParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.incomeStatement(params),
    queryFn: () => api.incomeStatement(params),
    enabled: options?.enabled ?? true,
  });
}

/** Year-end closing. Invalidates the reports whose data changes: DRE/Balancete/Balanço. */
export function useYearEndClosing(
  options?: UseMutationOptions<YearEndClosingResult, unknown, { companyId: string; fiscalYear: number }>,
) {
  return useInvalidatingMutation(
    (v) => api.yearEndClosing(v.companyId, v.fiscalYear),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['incomeStatement'] });
      void qc.invalidateQueries({ queryKey: ['trialBalance'] });
      void qc.invalidateQueries({ queryKey: ['balanceSheet'] });
    },
    options,
  );
}

export function useBalanceSheet(params: BalanceSheetParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.balanceSheet(params),
    queryFn: () => api.balanceSheet(params),
    enabled: options?.enabled ?? true,
  });
}

export function useJournalBook(params: JournalBookParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.journalBook(params),
    queryFn: () => api.journalBook(params),
    enabled: options?.enabled ?? true,
  });
}

export function useGeneralLedger(params: GeneralLedgerParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.generalLedger(params),
    queryFn: () => api.generalLedger(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useWithholdingsReport(companyId?: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.withholdingsReport(companyId),
    queryFn: () => api.withholdingsReport(companyId),
    enabled: options?.enabled ?? true,
  });
}

/* ---------- Fiscal: withholding assessment (Obrigações Acessórias) ---------- */
export function useFiscalAssessment(params: FiscalAssessmentParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.fiscalAssessment(params),
    queryFn: () => api.fiscalAssessment(params),
    enabled: (options?.enabled ?? true) && !!params.perApur,
  });
}

/* ---------- Remittances (CNAB240) ---------- */
export function useRemittances(params: RemittancesParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.remittances(params),
    queryFn: () => api.listRemittances(params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useRemittance(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.remittance(id ?? ''),
    queryFn: () => api.getRemittance(id!),
    enabled: !!id,
  });
}

/** Flat list of remittance títulos (AEL-958), filterable by unidade/natureza/forma/status/período. */
export function useRemittanceLines(params: RemittanceLinesParams, options?: { enabled?: boolean }) {
  const all = params.companyId === ALL_COMPANIES;
  return useQuery({
    queryKey: queryKeys.remittanceLines(all ? { ...params, companyId: 'all' } : params),
    queryFn: () => api.listRemittanceLines(all ? { ...params, companyId: undefined } : params),
    enabled: options?.enabled ?? true,
    placeholderData: keepPreviousData,
  });
}

export function useCreateRemittance(
  options?: UseMutationOptions<CreateRemittanceResponse, unknown, CreateRemittanceInput>,
) {
  return useInvalidatingMutation(
    (body) => api.createRemittance(body),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['remittances'] });
      // Included installments flip to InRemittance server-side — the payment queue must refetch
      // (pre-existing staleness gap, fixed with the treasury home).
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
    },
    options,
  );
}

export function useCancelRemittance(options?: UseMutationOptions<Remittance, unknown, { id: string }>) {
  return useInvalidatingMutation(
    ({ id }) => api.cancelRemittance(id),
    (qc, _data, vars) => {
      void qc.invalidateQueries({ queryKey: ['remittances'] });
      void qc.invalidateQueries({ queryKey: queryKeys.remittance(vars.id) });
      // Cancelled lines return to the queue (Open again).
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
    },
    options,
  );
}

/** Invalidates remittance list + detail after a return-related action. */
function invalidateRemittance(qc: ReturnType<typeof useQueryClient>, remittanceId: string) {
  void qc.invalidateQueries({ queryKey: ['remittances'] });
  void qc.invalidateQueries({ queryKey: queryKeys.remittance(remittanceId) });
  // A processed return registers settlements / releases installments server-side — refresh the queue,
  // its headers, the history tab and the cash-flow projection.
  void qc.invalidateQueries({ queryKey: ['payables'] });
  void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
  void qc.invalidateQueries({ queryKey: ['paymentsHistory'] });
  void qc.invalidateQueries({ queryKey: ['cashFlow'] });
}

export function useImportRemittanceReturn(
  options?: UseMutationOptions<RemittanceReturnOutcome, unknown, { id: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ id, file }) => api.importRemittanceReturn(id, file),
    (qc, _data, vars) => invalidateRemittance(qc, vars.id),
    options,
  );
}

/** Return files already processed for a paying account — the stored .RETs, newest first. */
export function useRemittanceReturnHistory(payingBankAccountId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.remittanceReturnHistory(payingBankAccountId ?? ''),
    queryFn: () => api.remittanceReturnHistory(payingBankAccountId!),
    enabled: !!payingBankAccountId,
  });
}

/** Dry-run preview of a return file against a paying account's pending lines — read-only, no invalidation. */
export function useRemittanceReturnPreview(
  options?: UseMutationOptions<RemittanceReturnPreview, unknown, { payingBankAccountId: string; file: File }>,
) {
  return useMutation({
    mutationFn: ({ payingBankAccountId, file }) => api.remittanceReturnPreview(payingBankAccountId, file),
    ...options,
  });
}

/** Dry-run reading of a CRÍTICA file (accept/reject) — read-only, no invalidation. */
export function useRemittanceReturnCriticism(
  options?: UseMutationOptions<RemittanceCriticism, unknown, { payingBankAccountId: string; file: File }>,
) {
  return useMutation({
    mutationFn: ({ payingBankAccountId, file }) => api.remittanceReturnCriticism(payingBankAccountId, file),
    ...options,
  });
}

/** Upload that auto-confirms the "Pago" lines (baixa) and returns the refreshed preview. Invalidates the
 *  payment queue / payables / history so the settled titles disappear from the queue. */
export function useRemittanceReturnAutoConfirm(
  options?: UseMutationOptions<RemittanceReturnPreview, unknown, { payingBankAccountId: string; file: File }>,
) {
  return useInvalidatingMutation(
    ({ payingBankAccountId, file }) => api.remittanceReturnAutoConfirm(payingBankAccountId, file),
    (qc) => {
      void qc.invalidateQueries({ queryKey: ['remittances'] });
      // The upload records the file — the history panel right below the picker has to show it at once.
      void qc.invalidateQueries({ queryKey: ['remittanceReturnHistory'] });
      void qc.invalidateQueries({ queryKey: ['payables'] });
      void qc.invalidateQueries({ queryKey: ['paymentQueueSummary'] });
      void qc.invalidateQueries({ queryKey: ['paymentsHistory'] });
      void qc.invalidateQueries({ queryKey: ['cashFlow'] });
    },
    options,
  );
}

export function useConfirmRemittanceLine(
  options?: UseMutationOptions<void, unknown, { id: string; lineId: string; ret?: RemittanceReturnLineData }>,
) {
  return useInvalidatingMutation(
    ({ id, lineId, ret }) => api.confirmRemittanceLine(id, lineId, ret),
    (qc, _data, vars) => invalidateRemittance(qc, vars.id),
    options,
  );
}

export function useMarkRemittanceLineReturned(
  options?: UseMutationOptions<void, unknown, { id: string; lineId: string; reason: string }>,
) {
  return useInvalidatingMutation(
    ({ id, lineId, reason }) => api.markRemittanceLineReturned(id, lineId, reason),
    (qc, _data, vars) => invalidateRemittance(qc, vars.id),
    options,
  );
}

// Re-export types for consumers that only import from queries
export type {
  AuditTrailEvent,
  PayableStageSummaryResponse,
  PayableStageItem,
  PaymentHistoryItem,
  PaymentQueueSummary,
  ReceivableStageSummaryResponse,
  ReceivableStageItem,
  Remittance,
  RemittanceListItem,
  RemittanceReturnOutcome,
  CreateRemittanceResponse,
  CreateRemittanceInput,
  WithholdingTaxType,
};
