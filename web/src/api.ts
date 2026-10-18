import { apiErrorFromProblem } from './apiError';
import { getApiBaseUrl } from './config/env';
import { queryClient } from './queryClient';
import type { components } from './api/schema';

/** All generated OpenAPI component schemas. Source of truth for wire types. */
type Schemas = components['schemas'];

const BASE_URL = getApiBaseUrl();

const TOKEN_KEY = 'ulbra-one.token';

export const auth = {
  get token() {
    return localStorage.getItem(TOKEN_KEY);
  },
  set(token: string) {
    localStorage.setItem(TOKEN_KEY, token);
    // Session/role change: reset the previous user's React Query cache (e.g. `/auth/me`, permissions,
    // scoped lists) so the new role is fetched from scratch. Without this, the post-login SPA navigation
    // keeps the stale `useMe` and the UI stays on the previous profile until a manual browser reload.
    queryClient.clear();
  },
  clear() {
    localStorage.removeItem(TOKEN_KEY);
    // Logout (including a role switch in the dev login): drop the previous user's cache. Logout uses
    // SPA navigation (`nav('/')`), which does not reload the page — so the cache must be cleared here,
    // otherwise the next login inherits the previous role's data.
    queryClient.clear();
  },
  /**
   * True only if a token exists AND is a structurally-valid, non-expired JWT.
   * Presence alone is not enough: an expired/stale token survives deploys and must
   * not admit the user into `/app` (that path white-screens). Uses the same 1-min
   * clock skew as the backend (`ClockSkew`) so the client is never *stricter* than
   * the server — a token the server would still accept is not rejected here.
   */
  hasValidToken(): boolean {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return false;
    const exp = jwtExp(token);
    if (exp === null) return false;
    return Date.now() < exp * 1000 + 60_000;
  },
};

/**
 * Reads the `exp` claim (seconds since epoch) from a JWT payload, or `null` if the
 * token is malformed/unreadable. We only need the numeric `exp`, so a plain base64url
 * decode is enough — no UTF-8 handling: multibyte bytes in text claims stay inside
 * string values and don't break `JSON.parse` of the numeric `exp`.
 */
function jwtExp(token: string): number | null {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    let b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    b64 += '='.repeat((4 - (b64.length % 4)) % 4);
    const payload = JSON.parse(atob(b64)) as { exp?: unknown };
    return typeof payload.exp === 'number' ? payload.exp : null;
  } catch {
    return null;
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  // FormData bodies must keep the browser-set multipart boundary — only force JSON for the rest.
  if (!(init.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);

  const res = await fetch(`${BASE_URL}${path}`, { ...init, headers });
  if (res.status === 401) {
    // Missing/expired session: clear the token and redirect to login (no loop if already there).
    auth.clear();
    if (!window.location.pathname.startsWith('/login')) {
      window.location.assign('/login');
    }
    throw new Error('Sessão expirada. Faça login novamente.');
  }
  if (!res.ok) {
    const contentType = res.headers.get('content-type') ?? '';
    if (contentType.includes('application/problem+json')) {
      try {
        const body = await res.json();
        throw apiErrorFromProblem(res.status, body);
      } catch (e) {
        // Re-throw the ApiError we just built; only fall through if JSON parsing failed.
        if (e instanceof Error && e.name === 'ApiError') throw e;
      }
    }
    const text = await res.text();
    throw new Error(text || `${res.status} ${res.statusText}`);
  }
  if (res.status === 204) return undefined as T;
  // Some endpoints answer 200 with an empty body (e.g. TypedResults.Ok() with no value). Guard against
  // res.json() throwing a SyntaxError on an empty body — treat "no content" as undefined for void callers.
  const body = await res.text();
  return (body ? JSON.parse(body) : undefined) as T;
}

/**
 * Turns a failed `fetch` Response into the same typed error as {@link request} — so raw `fetch` callers
 * (multipart uploads, which can't reuse `request`) surface a friendly problem+json message instead of dumping
 * the raw JSON body. Always throws.
 */
async function throwFromResponse(res: Response): Promise<never> {
  const contentType = res.headers.get('content-type') ?? '';
  const text = await res.text();
  if (contentType.includes('application/problem+json') || contentType.includes('application/json')) {
    try {
      throw apiErrorFromProblem(res.status, JSON.parse(text));
    } catch (e) {
      if (e instanceof Error && e.name === 'ApiError') throw e;
    }
  }
  throw new Error(text || `${res.status} ${res.statusText}`);
}

// ---------- Wire types (aliases to generated OpenAPI schemas) ----------
// Exported names are kept stable so pages/queries don't need to change.
export type User = Schemas['UserSummary'];
/** Someone present on a realtime-presence resource — `userId` + display name already resolved server-side. */
export type PresenceUser = Schemas['PresenceUserResponse'];

/** Role + its permissions (endpoints /roles — access administration). */
export interface AdminRole {
  id: string;
  name: string;
  permissions: string[];
}
/** A (company, unit) pair of a unit data-scope grant (AEL-883). */
export interface UnitScopeAssignment {
  companyId: string;
  unitId: string;
}
/** A (company, cost center) pair of a cost-center data-scope grant (AEL-883, iteration 2). */
export interface CostCenterScopeAssignment {
  companyId: string;
  costCenterId: string;
}
/** User + role + unified intersection scope (companies × units × cost centers) + active flag (AEL-883
 * iteration 2). The scope is a set of EXPLICIT lists — there is no "empty = all"; an empty list means none.
 * It is only enforced for the Requester/Approver roles; every other role sees everything. */
export interface AdminUser {
  id: string;
  email: string;
  name: string;
  role: string;
  isAdmin: boolean;
  active: boolean;
  /** Per-user grant to approve/reverse budget adjustments (AEL-883). Only meaningful for Controladoria. */
  canApproveBudgetAdjustment: boolean;
  /** Pre-registered (convite) but never signed in yet — the role/scope below is what the first login will
   * carry. */
  pendingFirstLogin: boolean;
  companyIds: string[];
  units: UnitScopeAssignment[];
  costCenters: CostCenterScopeAssignment[];
}
/** Pre-registration payload (POST /users): the e-mail plus the access it should already have on first login. */
export interface NewUser {
  email: string;
  name: string;
  role: string;
  companyIds: string[];
  units: UnitScopeAssignment[];
  costCenters: CostCenterScopeAssignment[];
  /** Approval ceiling for adequações; null means unlimited. */
  approvalLimit: number | null;
  canApproveBudgetAdjustment: boolean;
}
export type Company = Schemas['CompanyResponse'];
export type Unit = Schemas['UnitResponse'];
export type Transaction = Schemas['TransactionResponse'];
export type CostCenter = Schemas['CostCenterResponse'];
export type Address = Schemas['AddressResponse'];

// The DTOs declare `AddressResponse? Address` (nullable), but Swashbuckle can't express the nullability
// of a nested complex member, so it emits `address` as non-null. Restore the honest `Address | null`.
type WithNullableAddress<T extends { address: Address }> = Omit<T, 'address'> & { address: Address | null };
/** LC123 Simples annex (serialised as string). */
export type SimplesAnexoValue = Schemas['SimplesAnexo'];
/** Per-tax withholding RATE override on the vendor (rate = decimal fraction, 0.015 = 1,5%). */
export type VendorWithholdingRate = Schemas['VendorWithholdingRate'];
// `simplesAnexo` is nullable server-side (`SimplesAnexo?`) but the generated schema types it as a non-null
// required enum — restore the honest nullability. `withholdingRates` and `cprbForeseen` already generate correctly.
export type Vendor = Omit<WithNullableAddress<Schemas['VendorResponse']>, 'simplesAnexo' | 'bankInfo'> & {
  simplesAnexo: SimplesAnexoValue | null;
  bankInfo: BankInfo | null;
  // Override to LedgerAccount ids (the generated schema types them as account ids).
  associatedLedgerAccountIds: string[];
  // `createdAt` (AEL-844/860) comes from the generated schema (VendorResponse.CreatedAt) — feeds the
  // "novo fornecedor" badge (≤30 days) on the triage screen.
};
export type Customer = WithNullableAddress<Schemas['CustomerResponse']>;
export type Employee = WithNullableAddress<Schemas['EmployeeResponse']>;

/** Structured bank info returned by the API (all fields are non-null strings). */
export type BankInfo = Schemas['BankInfoResponse'];
/** Tax regime enum (serialised as string). */
export type TaxRegime = Schemas['TaxRegime'];

/**
 * Envelope returned by the paginated browse lists (transactions + registry).
 * Mirrors the backend `PaginatedResponse<T>`; kept as a stable hand-written alias
 * so pages don't depend on the generated, item-type-specific schema names.
 */
export interface Paginated<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

// ---------- Audit trail ----------
/** Whether the audited row was inserted, updated or deleted. */
export type AuditAction = Schemas['AuditAction'];
/** One audited change, as captured at the persistence layer (tables + columns, not domain terms). */
export type AuditLog = Schemas['AuditLogResponse'];

// ---------- Budgeting: Project + Budget + Requisition ----------
// Wire types come from the generated schema (regenerated OpenAPI). Enums serialize as strings; ids as strings.

export type ProjectStatus = Schemas['ProjectStatus'];
export type Project = Schemas['ProjectResponse'];
/** Accumulated tracking: budgeted × forecast × realized × balance. `isOverCeiling` = balance < 0. */
export type ProjectMetrics = Schemas['ProjectMetricsResponse'];
/** Request body: kept by hand (Swashbuckle doesn't emit request bodies as named schemas). */
export interface ProjectInput {
  unitId: string;
  name: string;
  description?: string | null;
  budgetLimit: number;
  initialConsumedValue: number;
  status?: ProjectStatus;
}

/** Monthly cost-center budget cell (Unit × Competência "YYYY-MM"), now owned in Core
 * (relocated from the old Budget). Hand-typed until the OpenAPI regen. */
export interface CostCenterUnitBudget {
  id: string;
  costCenterId: string;
  unitId: string;
  companyId: string;
  competency: string;
  monthlyLimit: number;
  active: boolean;
}
/** Create by period: materializes 1 cell/month. `endCompetency` omitted = 12 months (start + 11); equal to start = 1 month. */
export interface CostCenterUnitBudgetInput {
  unitId: string;
  startCompetency: string;
  endCompetency?: string;
  monthlyLimit: number;
  active?: boolean;
}
/** Update only changes the monthly ceiling + active — dimensions and competência are immutable. */
export interface CostCenterUnitBudgetUpdateInput {
  monthlyLimit: number;
  active: boolean;
}
/** Response of create-by-period: N monthly cells (one per competência). */
export interface CostCenterUnitBudgetPeriod {
  startCompetency: string;
  endCompetency: string;
  count: number;
  items: CostCenterUnitBudget[];
}

/** Server-side budget control, per competência (budgeted × realized × committed × forecast). */
export interface BudgetMonth {
  competency: string;
  orcado: number;
  realized: number;
  committed: number;
  forecast: number;
  consumed: number;
  available: number;
  budgeted: boolean;
  /** Exact cost of the request in this competência (remainder-in-the-last proration); 0 in the dimension panel with no candidate. */
  cost: number;
}
/** Competência that the candidate requisition overflows. */
export interface BudgetBreach {
  competency: string;
  orcado: number;
  available: number;
  cost: number;
}
/** Backend budget-control panel (replaces the client-side sum). */
export interface BudgetControl {
  months: BudgetMonth[];
  breaches: BudgetBreach[];
  unbudgeted: string[];
  candidateExceeds: boolean;
}
/** "Budget by natureza" line (AEL-836): budgeted × consumed × available for a cost center. */
export interface CostCenterBudgetLine {
  costCenterId: string;
  costCenterName: string;
  orcado: number;
  realized: number;
  committed: number;
  forecast: number;
  consumed: number;
  available: number;
  budgeted: boolean;
}

/** Total realized (spend) of a cost center over all competencies — cost-center list card. */
export interface CostCenterRealized {
  costCenterId: string;
  realized: number;
}

/** Aggregated month (company/unit, all CCs) of the queue projection (AEL-876). Hand-typed until the regen. */
export interface AggregateBudgetMonth {
  competency: string;
  orcado: number;
  consumed: number;
  forecast: number;
  budgeted: boolean;
}
/** Multi-month projection of the entire queue against the budget (AEL-876). */
export interface QueueProjection {
  months: AggregateBudgetMonth[];
  breaches: string[];
  queueTotal: number;
}

/** In-app workflow notification (AEL-794). Hand-typed until the OpenAPI regen. */
export interface Notification {
  id: string;
  type: string;
  title: string;
  message: string;
  referenceId: string | null;
  occurredAt: string;
  readAt: string | null;
}

export type RequisitionStatus = Schemas['RequisitionStatus'];
export type RequisitionType = Schemas['RequisitionType'];
export type EstimateType = Schemas['EstimateType'];
export type PaymentFrequency = Schemas['PaymentFrequency'];
export type RequisitionAction = Schemas['RequisitionAction'];
export type RequisitionAttachment = Schemas['RequisitionAttachmentResponse'];
/** What an attachment is (Nota Fiscal / Boleto / Fatura / Outro). Only `FiscalDocument` clears the "NF
 * pendente" gate. Same value set on both the requisition and payable sides. */
export type RequisitionAttachmentKind = Schemas['RequisitionAttachmentKind'];
export type RequisitionTransition = Schemas['RequisitionTransitionResponse'];
/** Authored payment installment on a requisition (AEL-844): due date, value to transfer + payment method. */
export type RequisitionInstallment = Schemas['RequisitionInstallmentResponse'];
/** A launched monthly cycle of a recurring requisition (AEL-877): competency, amount, due date, method, boleto
 * and the Finance Payable it generated (`payableId`, null until Finance confirms). */
export type RequisitionCycle = Schemas['RequisitionCycleResponse'];
/** Per-status requisition counts for the stage KPIs (one grouped query — replaces the per-status fan-out).
 * Keyed by workflow status; a status with no requisitions is absent (treat as zero). `total` is the summed
 * estimated value in BRL. */
export interface RequisitionStageCounts {
  stages: Partial<Record<RequisitionStatus, { count: number; total: number }>>;
}

/**
 * `paymentFrequency` is nullable on the backend (`PaymentFrequency?`), but Swashbuckle emits the enum as
 * non-null (same quirk as `Payable.type`) — we restore the honest nullability.
 */
export type Requisition = Omit<Schemas['RequisitionResponse'], 'paymentFrequency'> & {
  paymentFrequency?: PaymentFrequency | null;
  /** Post-approval requisition still missing its Nota Fiscal (drives the "NF pendente" tag/list). Hand-typed
   * over the generated schema until the OpenAPI regen — see web-api-ts-handwritten-layer-over-schema. */
  fiscalDocumentPending: boolean;
  /** Denormalized vendor name on the list response (AEL-876); empty on single-entity endpoints. Hand-typed
   * over the generated schema until the OpenAPI regen. */
  vendorName: string;
};

/** An approver decision (Approve/ReturnForComplement/Cancel) — feeds the inbox "Histórico" tab.
 * Hand-typed until the OpenAPI regen (AEL-858); mirrors `RequisitionDecisionResponse`. */
export interface RequisitionDecision {
  requisitionId: string;
  code: number;
  vendorId: string;
  vendorName: string;
  description: string;
  estimateValue: number;
  status: RequisitionStatus;
  decisionAction: RequisitionAction;
  decisionReason: string | null;
  decidedAt: string;
}

/** Create body. On update, `unitId`/`costCenterId` are omitted (dimensions are immutable). */
export interface RequisitionInput {
  unitId: string;
  costCenterId: string;
  vendorId: string;
  type: RequisitionType;
  description: string;
  observation?: string | null;
  estimateValue: number;
  estimateType: EstimateType;
  startCompetency: string;
  endCompetency?: string | null;
  firstDueDate: string;
  paymentFrequency?: PaymentFrequency | null;
  paymentInstallmentsCount?: number | null;
  projectId?: string | null;
  // optional front-loaded financial plan. Present together → triage approval skips the financial
  // complement; absent on update → clears any previously front-loaded plan.
  issueDate?: string | null;
  installments?: FinancialComplementInstallmentInput[] | null;
}

/** One authored installment sent when submitting the financial complement (AEL-844). */
/** Installment of the financial-complement plan. Mirrors the generated schema but keeps
 * `boletoDigitableLine` OPTIONAL: the form omits the key entirely on non-boleto rows and the API accepts it as
 * optional/nullable. Hand-written override over the generated required-nullable shape (same pattern as the
 * other input types in this file). */
export interface FinancialComplementInstallmentInput {
  dueDate: string;
  amount: number;
  paymentMethod: PaymentMethod;
  boletoDigitableLine?: string | null;
  /** Per-payment PIX key override (AEL-938); only sent on Pix rows. Blank/omitted → the vendor's key is used. */
  pixKeyOverride?: string | null;
  /** Per-payment PIX "copia e cola" (EMV/BR Code, AEL-963); only sent on Pix rows. Mutually exclusive with pixKeyOverride. */
  pixPayloadEmv?: string | null;
}

/** Body for POST /requisitions/{id}/submit-financial-complement (AEL-844): a single emission date + the
 * authored installment plan (due date, value to transfer and payment method per installment). */
export interface SubmitFinancialComplementInput {
  issueDate: string;
  installments: FinancialComplementInstallmentInput[];
}

/** Body for POST /requisitions/{id}/cycles (AEL-877): the month's invoice launched on a recurring requisition
 * — competency, due date, amount, payment method and (when paid by boleto) the digitable line. */
export interface AddRecurrenceCycleInput {
  competency: string;
  dueDate: string;
  amount: number;
  paymentMethod: PaymentMethod;
  boletoDigitableLine?: string | null;
  /** Per-payment PIX key override (AEL-938); only sent on a Pix invoice. Blank/omitted → the vendor's key is used. */
  pixKeyOverride?: string | null;
  /** Per-payment PIX "copia e cola" (EMV/BR Code, AEL-963); only sent on a Pix invoice. Mutually exclusive with pixKeyOverride. */
  pixPayloadEmv?: string | null;
}

// ---------- Budget adjustment (Budgeting / AEL-791) ----------
// Wire types come from the generated schema. Enums serialize as strings; ids as strings.
// Reminder: list/create/transition responses carry an EMPTY `transitions`; only GET /{id} populates the trail.
export type BudgetAdjustmentType = Schemas['BudgetAdjustmentType'];
export type BudgetAdjustmentStatus = Schemas['BudgetAdjustmentStatus'];
export type BudgetAdjustmentAction = Schemas['BudgetAdjustmentAction'];
export type BudgetAdjustmentTransition = Schemas['BudgetAdjustmentTransitionResponse'];
export type BudgetAdjustment = Schemas['BudgetAdjustmentResponse'];

/** Create body: kept by hand (Swashbuckle doesn't emit request bodies as named schemas).
 *  On update, `unitId`/`costCenterId` are omitted (dimensions are immutable). */
export interface BudgetAdjustmentInput {
  unitId: string;
  costCenterId: string;
  type: BudgetAdjustmentType;
  competency: string;
  sourceCompetency?: string | null;
  value: number;
  justification: string;
  counterparts?: string | null;
}

// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type TransactionType = Schemas['TransactionType'];
export type TransactionStatus = Schemas['TransactionStatus'];

// ---------- Payables (accounts payable) ----------
/** Payable type (Service/Product — drives the withholding). */
export type PayableType = Schemas['PayableType'];
// `type` is a nullable enum on the backend (`PayableType?`); Swashbuckle emits it non-null (nullable-enum quirk),
// so restore the honest nullability. Kept optional for back-compat with existing test fixtures.
export type Payable = Omit<Schemas['PayableResponse'], 'type'> & {
  type?: PayableType | null;
  // AEL-793 — hand-written until the OpenAPI schema is regenerated: id of the originating requisition
  // (when the payable was generated by the budget flow), in the format "req_...".
  originRequisitionId?: string | null;
};
// `effectiveMethod` is a nullable enum on the backend (`PaymentMethod?`); Swashbuckle emits it
// non-null (nullable-enum quirk, same as `Payable.type`) — restore the honest nullability.
export type PayableListItem = Omit<Schemas['PayableListItemResponse'], 'effectiveMethod'> & {
  effectiveMethod: PaymentMethod | null;
};

// NEW (AEL-871): whole-set aggregates for the payment queue's group headers.
export interface PaymentQueueDayGroup {
  dueDate: string;
  count: number;
  netTotal: number;
}
export interface PaymentQueueSummary {
  asOf: string;
  overdueCount: number;
  overdueNetTotal: number;
  groups: PaymentQueueDayGroup[];
  totalCount: number;
  totalNetTotal: number;
}

// NEW (AEL-871): one row of the cross-payable settlement history (Histórico de baixas).
export interface PaymentHistoryItem {
  paymentId: string;
  payableId: string;
  installmentId: string;
  installmentSequence: number;
  installmentCount: number;
  counterpartyName: string;
  vendorDocument?: string | null;
  documentNumber?: string | null;
  paymentDate: string;
  amount: number;
  interest: number;
  fine: number;
  discount: number;
  method: string;
  bankAccountId: string;
  isReversed: boolean;
  reversalReason?: string | null;
  hasProof: boolean;
  createdAt: string;
  createdById: string;
}
export type Installment = Schemas['InstallmentResponse'];
export type PayableAttachment = Schemas['PayableAttachmentResponse'];
export type PayableAttachmentKind = Schemas['PayableAttachmentKind'];
export type ExpenseCategory = Schemas['ExpenseCategoryResponse'];
export type PayableOrigin = Schemas['PayableOrigin'];
export type PayableStatus = Schemas['PayableStatus'];
/** Role a rejected requisition-sourced payable is returned to (AEL-935). */
export type PayableReturnTarget = Schemas['PayableReturnTarget'];
export type InstallmentStatus = Schemas['InstallmentStatus'];
export type PayableStageSummaryResponse = Schemas['PayableStageSummaryResponse'];
export type ReceivableStageSummaryResponse = Schemas['ReceivableStageSummaryResponse'];
export type StageBucket = Schemas['StageBucket'];
export type AuditTrailEvent = Schemas['AuditTrailEventResponse'];

// ---------- Withholdings ----------
export type WithholdingTaxType = Schemas['WithholdingTaxType'];
export type PayableWithholding = Schemas['PayableWithholdingResponse'];

// ---------- Withholding rules ----------
// `vendorRegime` / `simplesAnexo` are nullable enums on the backend; Swashbuckle emits them non-null (nullable-enum
// quirk), so restore the honest nullability. `cprb` (a nullable bool) generates correctly and flows through.
export type WithholdingRule = Omit<Schemas['WithholdingRuleResponse'], 'vendorRegime' | 'simplesAnexo'> & {
  vendorRegime: TaxRegime | null;
  simplesAnexo: SimplesAnexoValue | null;
};
export type WithholdingRuleInput = Omit<WithholdingRule, 'id' | 'active'>;
export type RateSource = Schemas['RateSource'];
export type BaseRule = Schemas['BaseRule'];
export type NonRetentionReason = Schemas['NonRetentionReason'];

// System-default outcome per tax for a vendor tax profile (GET /withholding-rules/preview).
export type WithholdingPreviewItem = Schemas['WithholdingPreviewItem'];
// Client-side query params for the preview call (not a wire schema).
export interface WithholdingPreviewParams {
  regime: TaxRegime;
  companyId?: string | null;
  cprb?: boolean | null;
  simplesAnexo?: SimplesAnexoValue | null;
}

// ---------- Fiscal documents (NF reader) ----------
export type FiscalDocumentFormat = Schemas['FiscalDocumentFormat'];

/** Document-level fiscal signals (NF-e): CFOPs, NCMs, and tax totals. */
export type FiscalSummary = Schemas['FiscalSummary'];

/** Presigned/SAS upload URL + storage key returned by the upload-url endpoint. */
export type UploadTarget = Schemas['UploadTarget'];

/** Stateless parse/preview result — fields identified from the uploaded NF, plus a vendor suggestion. */
export interface ParsedFiscalDocument {
  format: FiscalDocumentFormat;
  accessKey?: string | null;
  documentNumber?: string | null;
  issueDate?: string | null;
  totalAmount?: number | null;
  issuerDocument?: string | null;
  issuerName?: string | null;
  recipientDocument?: string | null;
  rawExtractionJson: string;
  /** Installments (duplicatas) read from the NF — empty for upfront / NFS-e / PDF. */
  installments: { dueDate: string; amount: number }[];
  matchedVendorId?: string | null;
  matchedVendorName?: string | null;
  // NFS-e (service) — informational; null for NF-e/PDF.
  rpsNumber?: string | null;
  rpsSeries?: string | null;
  serviceDescription?: string | null;
  cnaeCode?: string | null;
  // Issuer contact (NF-e emit) — feeds the "cadastrar fornecedor" pre-fill; null for NFS-e/PDF.
  issuerPhone?: string | null;
  issuerAddress?: {
    cep?: string | null;
    street?: string | null;
    number?: string | null;
    neighborhood?: string | null;
    city?: string | null;
    state?: string | null;
  } | null;
  // Fiscal signals (NF-e): CFOPs/NCMs/tax totals; null for NFS-e/PDF.
  fiscal?: FiscalSummary | null;
  // Payable type (Service/Product) inferred by the AI extractor from a PDF/image;
  // null for XML (derived from the format) and when the model is unsure.
  suggestedType?: PayableType | null;
}

/**
 * AI-extracted boleto fields to pre-fill one installment (AEL-938). `digitableLine` null = nothing read;
 * `lengthValid` blocks, `checkDigitsOk` is advisory.
 */
export interface ParsedBoleto {
  digitableLine: string | null;
  isConvenio: boolean;
  lengthValid: boolean;
  checkDigitsOk: boolean;
  dueDate?: string | null;
  issueDate?: string | null;
  amount?: number | null;
  confidence: number;
}

/** A boleto blob already uploaded via the presigned URL — enough to register it as an attachment by key (AEL-938). */
export interface BoletoBlobRef {
  key: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

/** The fiscal document of record persisted against a payable. */
export interface FiscalDocument {
  id: string;
  payableId: string;
  format: FiscalDocumentFormat;
  accessKey?: string | null;
  documentNumber?: string | null;
  issueDate?: string | null;
  totalAmount?: number | null;
  issuerDocument?: string | null;
  issuerName?: string | null;
  recipientDocument?: string | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
}

// ---------- Bank accounts + payments (settlement) ----------
// `ofxAccountId` (BANKACCTFROM/ACCTID) augmented for AEL-767; the CNAB remittance-header profile
// (agencyDigit + paying-company address) augmented for AEL-613 — both ahead of the generated-schema regeneration.
export type BankAccount = Schemas['BankAccountResponse'] & {
  ofxAccountId?: string | null;
  agencyDigit?: string | null;
  addressStreet?: string | null;
  addressNumber?: string | null;
  addressComplement?: string | null;
  addressCity?: string | null;
  addressCep?: string | null;
  addressState?: string | null;
  payerCnpj?: string | null;
  payerLegalName?: string | null;
};
export type Payment = Schemas['PaymentResponse'];
// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type BankAccountType = Schemas['BankAccountType'];
export type PaymentMethod = Schemas['PaymentMethod'];

// ---------- Payable / Receivable stage items (payable-grain, one row per title) ----------
// `originRequisitionCode` (AEL-865) is declared here as the hand-written layer over the generated schema;
// the intersection is redundant once `openapi.json` is regenerated (it becomes native on the response).
export type PayableStageItem = Schemas['PayableStageItemResponse'] & { originRequisitionCode?: number | null };
export type PayableAccountingView = Schemas['PayableAccountingViewResponse'];
export type ReceivableStageItem = Schemas['ReceivableStageItemResponse'];

// ---------- Receivables (accounts receivable) ----------
export type Receivable = Schemas['ReceivableResponse'];
export type ReceivableListItem = Schemas['ReceivableListItemResponse'];
export type ReceivableInstallment = Schemas['ReceivableInstallmentResponse'];
export type Receipt = Schemas['ReceiptResponse'];
export type ReceivableOrigin = Schemas['ReceivableOrigin'];
export type ReceivableStatus = Schemas['ReceivableStatus'];
export type ReceivableInstallmentStatus = Schemas['ReceivableInstallmentStatus'];

// ---------- Classification ----------
export type ClassificationSuggestion = Schemas['ClassificationSuggestionResponse'];
export type AiClassificationSuggestion = Schemas['AiClassificationSuggestionResponse'];
export type ClassificationPolicy = Schemas['ClassificationPolicyResponse'];

// ---------- Accounting (ledger core) ----------
/** DRE cascade line for a result account (string enum on the wire). Hand-written here (api.ts layer) until
 *  openapi.json/schema.d.ts are regenerated from the running API. */
export type DreLine =
  | 'GrossRevenue'
  | 'RevenueDeductions'
  | 'CostOfSales'
  | 'OtherOperatingIncome'
  | 'OperatingExpenses'
  | 'FinancialRevenue'
  | 'FinancialExpense'
  | 'Other'
  | 'IncomeTax'
  | 'SocialContribution';
/** Adds `dreLine` over the generated response until the schema is regenerated. */
export type LedgerAccount = Schemas['LedgerAccountResponse'] & { dreLine?: DreLine | null };
export type AccountGroup = Schemas['AccountGroup'];
export type LedgerAccountKind = Schemas['LedgerAccountKind'];
export type NormalBalance = Schemas['NormalBalance'];
export type EntrySide = Schemas['EntrySide'];
export type JournalStatus = Schemas['JournalStatus'];
export type JournalOrigin = Schemas['JournalOrigin'];
export type JournalEntry = Schemas['JournalEntryResponse'];
export type JournalLine = Schemas['JournalLineResponse'];
export type TrialBalance = Schemas['TrialBalanceResponse'];
export type TrialBalanceRow = Schemas['TrialBalanceRowResponse'];
/** DRE (income statement) types — hand-written cascade shape (api.ts layer) until the schema is regenerated.
 *  Amounts are contributions to the result (credit − debit): revenues positive, costs/expenses/deductions
 *  negative. Subtotals are running sums down the cascade. */
export interface IncomeStatementLine {
  ledgerAccountId: string;
  accountCode: string;
  accountName: string;
  dreLine: DreLine;
  amount: number;
}
export interface IncomeStatementSection {
  line: DreLine;
  total: number;
  lines: IncomeStatementLine[];
}
export interface IncomeStatement {
  companyId?: string | null;
  costCenterId?: string | null;
  from: string;
  to: string;
  sections: IncomeStatementSection[];
  grossRevenue: number;
  revenueDeductions: number;
  netRevenue: number;
  costOfSales: number;
  grossProfit: number;
  otherOperatingIncome: number;
  operatingExpenses: number;
  operatingResult: number;
  financialRevenue: number;
  financialExpense: number;
  financialResult: number;
  otherResult: number;
  preTaxResult: number;
  incomeTax: number;
  socialContribution: number;
  netIncome: number;
}
/** Year-end closing result (hand-written until openapi.json is regenerated). */
export interface YearEndClosingResult {
  fiscalYear: number;
  alreadyClosed: boolean;
  netIncome: number;
  resultAccountsCleared: number;
}
export type BalanceSheet = Schemas['BalanceSheetResponse'];
export type BalanceSheetLine = Schemas['BalanceSheetLineResponse'];

// ---------- Journal book (read-only view over scriptured entries) ----------
// Aliases to the generated schemas; the *View names are kept stable for the pages/components.
export type JournalBookLineView = Schemas['JournalBookLineResponse'];
export type JournalBookEntryView = Schemas['JournalBookEntryResponse'];
export type JournalBookDayView = Schemas['JournalBookDayResponse'];
export type JournalBook = Schemas['JournalBookResponse'];

// ---------- General ledger (read-only, per account, running balance) ----------
// Aliases to the generated schemas; the *View names are kept stable for the pages/components.
// The response is paginated by account (Paginated<GeneralLedgerAccountView>).
// Hand-added fields (pending openapi regen): supplier identification surfaced only in the general-ledger history
// (legal name + CNPJ/CPF). Nullable — only supplier-originated entries carry them. See JournalEntry.
export type GeneralLedgerMovementView = Schemas['GeneralLedgerMovementResponse'] & {
  counterpartyName?: string | null;
  counterpartyDocument?: string | null;
};
export type GeneralLedgerAccountView = Omit<Schemas['GeneralLedgerAccountResponse'], 'movements'> & {
  movements: GeneralLedgerMovementView[];
};

// ---------- Remittances (CNAB240) ----------
export type Remittance = Schemas['RemittanceResponse'];
export type RemittanceListItem = Schemas['RemittanceListItemResponse'];
export type RemittanceLine = Schemas['RemittanceLineResponse'];
export type RemittanceLineItem = Schemas['RemittanceLineListItemResponse'];
export type RemittanceStatus = Schemas['RemittanceStatus'];
export type RemittanceCategory = Schemas['RemittanceCategory'];
export type PaymentReportItem = Schemas['PaymentReportItemResponse'];
export type PaymentsReportSummary = Schemas['PaymentsReportSummaryResponse'];
export type RemittanceReturnOutcome = Schemas['RemittanceReturnOutcome'];
/** One processed return file in a paying account's history — `applied: false` means nothing was settled. */
export type RemittanceReturnHistoryItem = Schemas['RemittanceReturnHistoryItem'];
export type CreateRemittanceResponse = Schemas['CreateRemittanceResponse'];
export type SkippedInstallment = Schemas['SkippedInstallmentResponse'];
/** Remittance line status — serialized as string at runtime. */
export type RemittanceLineStatus = 'Pending' | 'Paid' | 'Returned';

/**
 * Dry-run preview of a CNAB return file matched against a paying account's pending lines (AEL-918).
 * Hand-written here (the endpoint post-dates the last OpenAPI regen); mirrors RemittanceReturnPreviewResponse.
 */
export interface RemittanceReturnPreview {
  trailerOk: boolean;
  trailerMessages: string[];
  payer: { document: string; agency: string; account: string } | null;
  counters: {
    total: number;
    paid: number;
    rejected: number;
    divergent: number;
    unmatched: number;
    ambiguous: number;
    alreadyProcessed: number;
  };
  entries: RemittanceReturnPreviewEntry[];
  /** How many "Pago" lines were auto-confirmed (baixa aplicada) on upload. 0 for the read-only preview. */
  autoConfirmed: number;
}
export interface RemittanceReturnPreviewEntry {
  reference: string;
  favoredName: string | null;
  favoredDocument: string | null;
  method: string | null;
  valorNominal: number | null;
  valorEfetivado: number;
  effectiveDate: string | null;
  occurrenceCodes: string[];
  motivo: string | null;
  authCode: string | null;
  bankReference: string | null;
  classification: 'Paid' | 'Rejected' | 'Divergent' | 'Unmatched' | 'Ambiguous' | 'AlreadyProcessed';
  matchMethod: 'None' | 'Reference' | 'Cnpj' | 'Pix' | 'Barcode';
  remittanceId: string | null;
  lineId: string | null;
  payableId: string | null;
  installmentId: string | null;
  note: string | null;
}
/** Return settlement data forwarded to confirm-paid so the line records the real effective date/auth. */
export interface RemittanceReturnLineData {
  effectiveDate?: string | null;
  bankReference?: string | null;
  authCode?: string | null;
}

/**
 * CRÍTICA file reading (accept/reject) — not a payment, never applies baixa.
 * Hand-written here (the endpoint post-dates the last OpenAPI regen); mirrors RemittanceCriticismResponse.
 */
export interface RemittanceCriticism {
  payer: { document: string; agency: string; account: string } | null;
  total: number;
  success: number;
  rejection: number;
  records: RemittanceCriticismRecord[];
}
export interface RemittanceCriticismRecord {
  reference: string;
  favoredName: string | null;
  amount: number;
  occurrenceCodes: string[];
  motivo: string | null;
  isSuccess: boolean;
  bankReference: string | null;
  remittanceId: string | null;
  lineId: string | null;
  lineStatus: RemittanceLineStatus | null;
}

/** Request body for generating a CNAB240 remittance file. */
export interface CreateRemittanceInput {
  payingBankAccountId: string;
  dueCutoff?: string | null;
  /** NEW (AEL-871): generate from the operator's selection (cutoff ignored when present). */
  installmentIds?: string[] | null;
  /** Date the bank should execute the payments. Null = today; cannot be in the past. */
  paymentDate?: string | null;
}

/** Request body for creating a ledger account (global chart — no companyId). */
export interface LedgerAccountInput {
  code: string;
  name: string;
  accountGroup: AccountGroup;
  kind: LedgerAccountKind;
  parentId?: string | null;
  normalBalance?: NormalBalance | null;
  dreLine?: DreLine | null;
}

/** Request body for updating a ledger account (no companyId/code — both immutable).
 *  PUT is full-replace, so `reducedCode`/`natureza` must be echoed or they are cleared. */
export interface LedgerAccountUpdateInput {
  name: string;
  accountGroup: AccountGroup;
  kind: LedgerAccountKind;
  normalBalance: NormalBalance;
  parentId?: string | null;
  isActive: boolean;
  reducedCode?: string | null;
  natureza?: number | null;
  dreLine?: DreLine | null;
}

/** One debit/credit line when creating or editing a draft journal entry. */
export interface JournalLineInput {
  ledgerAccountId: string;
  side: EntrySide;
  amount: number;
  costCenterId?: string | null;
  unitId?: string | null;
  description?: string | null;
}

/** Request body for creating/editing a draft journal entry. */
export interface JournalEntryInput {
  companyId: string;
  competenceDate: string;
  description: string;
  sourceReference?: string | null;
  lines: JournalLineInput[];
}

// ---------- Payables aging ----------
export type AgingReport = Schemas['AgingReportResponse'];
export type AgingItem = Schemas['AgingItemResponse'];

// ---------- Withholdings report (retencoes) ----------
// The report splits each withholding into what is already withheld (realizedAmount) vs forecast/to withhold (foreseenAmount):
// emission taxes (IRRF/INSS/ISS) are realized at issuance, the CSRF group (PIS/COFINS/CSLL) only at payment.
// amount = realizedAmount + foreseenAmount. `timing` is OnIssue/OnPayment.
export type WithholdingTiming = Schemas['WithholdingTiming'];
export type WithholdingReportLine = Schemas['WithholdingReportLine'];
export type WithholdingByTaxRow = Schemas['WithholdingByTaxRow'];
export type WithholdingPayableRow = Schemas['WithholdingPayableRow'];
export type WithholdingsTotals = Schemas['WithholdingsTotals'];
export type WithholdingsReport = Schemas['WithholdingsReportResponse'];

// Withholding assessment (Obrigações Acessórias). Collection guide: PIS+COFINS+CSLL become CSRF.
export type RetentionGroup = Schemas['RetentionGroup'];
export type WithholdingFactRow = Schemas['WithholdingFactResponse'];
export type CompanyAssessmentRow = Schemas['CompanyAssessmentRowResponse'];
export type RetentionGroupRow = Schemas['RetentionGroupRowResponse'];
export type TaxTypeAssessmentRow = Schemas['TaxTypeRowResponse'];
export type CostCenterAssessmentRow = Schemas['CostCenterAssessmentRowResponse'];
export type FiscalAssessment = Schemas['WithholdingAssessmentResponse'];

export type AgingBucket = Schemas['AgingBucket'];

// ---------- SLA report (SLA analysis — AEL-872) ----------
// Derived from the generated OpenAPI schema (single source of truth after gen:api).
export type SlaOverdueRow = Schemas['OverdueStageResponse'];
export type SlaLive = Schemas['RequisitionSlaLiveResponse'];
export type SlaStageStat = Schemas['StageSlaMetricResponse'];
export type SlaCycle = Schemas['CycleTimeResponse'];
export type SlaUnitStat = Schemas['UnitSlaMetricResponse'];
export type SlaReportResponse = Schemas['RequisitionSlaReportResponse'];

// ---------- Cash flow ----------
export type CashFlowResponse = Schemas['CashFlowResponse'];
export type CashFlowKpis = Schemas['CashFlowKpis'];
export type CashFlowPeriod = Schemas['CashFlowPeriod'];
export type CashFlowAccountBalance = Schemas['CashFlowAccountBalance'];
export type CashFlowItem = Schemas['CashFlowItemResponse'];
// Enum union: serialized as a string at runtime via the global JsonStringEnumConverter.
export type CashFlowGranularity = Schemas['CashFlowGranularity'];
export type ReconciledBalanceResult = Schemas['SetReconciledBalanceResponse'];

// ---------- OFX reconciliation (statement imports + bank transactions) ----------
// `warning` is a non-blocking account-mismatch notice surfaced at import time (AEL-767); augmented here so
// the UI can read it before the generated schema is regenerated.
export type StatementImport = Schemas['StatementImportResponse'] & { warning?: string | null };
// `mismatch` is null on a reconciled/ignored line and on every endpoint but the working list, but Swashbuckle
// emits a nullable enum as required — so the nullability is restored here rather than trusted from the schema.
export type BankTransaction = Omit<Schemas['BankTransactionResponse'], 'mismatch' | 'matchedTitle'> & {
  mismatch: ConciliationMismatch | null;
  /** Swashbuckle emits the optional pairing as required; it is absent while the line is open and on ignored ones. */
  matchedTitle?: ConciliationMatchedTitle | null;
};
/** The payable side of a reconciled statement line — shown beside what the bank sent. */
export type ConciliationMatchedTitle = Schemas['ConciliationMatchedTitleResponse'];
/** Why a still-open statement line could not be paired automatically. */
export type ConciliationMismatch = Schemas['ConciliationMismatch'];
/** An installment close enough to a statement line to show beside it: the suggestion, or a near miss. */
export type ConciliationCandidate = Schemas['ConciliationCandidateResponse'];
// Enum unions: serialized as strings at runtime via the global JsonStringEnumConverter.
export type MatchStatus = Schemas['MatchStatus'];
export type TransactionDirection = Schemas['TransactionDirection'];
/** Review stage grouping the statuses into the two tabs: analysis (open) vs resolved (approved/ignored). */
export type ReconciliationStage = 'analysis' | 'resolved';

/** One reconciliation-status bucket (count + summed amount) of the period summary. */
export interface StatusBucket {
  count: number;
  total: number;
}

/** Aggregated "payments in the period" summary grouped by reconciliation status, plus debit/credit totals. */
export interface PeriodSummary {
  from?: string | null;
  to?: string | null;
  reconciled: StatusBucket;
  suggested: StatusBucket;
  unmatched: StatusBucket;
  ignored: StatusBucket;
  debitTotal: number;
  creditTotal: number;
}

/** Request body for creating a bank account. */
export interface BankAccountInput {
  companyId: string;
  bankCode: string;
  bankName: string;
  agency: string;
  accountNumber: string;
  accountDigit: string;
  accountType: BankAccountType;
  alias: string;
  openingBalance?: number | null;
  isDefaultForPayments?: boolean;
  cnabAgreementCode?: string | null;
  /** Accounting ledger account for this bank (credit account when settling a payable). Required on create. */
  ledgerAccountId?: string | null;
  /** OFX account identifier (BANKACCTFROM/ACCTID) used to warn on statement import. Optional. */
  ofxAccountId?: string | null;
  /** CNAB remittance-header profile (optional): agency check digit (DV) + paying-company address. */
  agencyDigit?: string | null;
  addressStreet?: string | null;
  addressNumber?: string | null;
  addressComplement?: string | null;
  addressCity?: string | null;
  addressCep?: string | null;
  addressState?: string | null;
  /** CNAB payer identity override (optional): empty falls back to the company's CNPJ/legal name. */
  payerCnpj?: string | null;
  payerLegalName?: string | null;
}

/** Request body for updating a bank account (no companyId; `active` toggles status). */
export type BankAccountUpdateInput = Omit<BankAccountInput, 'companyId'> & { active: boolean };

/** Request body for registering a payment (settlement) against an installment. */
export interface RegisterPaymentInput {
  paymentDate: string;
  amount: number;
  bankAccountId: string;
  method: PaymentMethod;
  interest?: number;
  fine?: number;
  discount?: number;
  notes?: string | null;
}

/** Request body for creating/updating a payable. `installments` lists explicit dues; `generate` derives them. */
export interface PayableInput {
  companyId: string;
  vendorId: string;
  costCenterId: string;
  categoryId?: string | null;
  origin?: PayableOrigin;
  description: string;
  documentNumber?: string | null;
  issueDate?: string | null;
  installments?: { dueDate: string; amount: number; boletoDigitableLine?: string | null }[];
  generate?: { totalAmount: number; count: number; firstDueDate: string; intervalMonths?: number };
  accountingAccountId?: string | null;
  /** Optional override: which bank account + method to use for auto-pay (null = use company default). */
  paymentBankAccountId?: string | null;
  paymentMethod?: PaymentMethod | null;
  /** Type of supply: 'Service' (triggers withholding) or 'Product'. Required. */
  type: 'Service' | 'Product';
}

/** Request body for creating/updating a receivable. `installments` lists explicit dues; `generate` derives them. */
export interface ReceivableInput {
  companyId: string;
  payerId: string;
  costCenterId: string;
  revenueCategoryId?: string | null;
  ledgerAccountId?: string | null;
  origin?: ReceivableOrigin;
  description: string;
  documentNumber?: string | null;
  issueDate?: string | null;
  installments?: { dueDate: string; amount: number }[];
  generate?: { totalAmount: number; count: number; firstDueDate: string; intervalMonths?: number };
}

/** Request body for registering a receipt (inbound settlement) against a receivable installment. */
export interface RegisterReceiptInput {
  receiptDate: string;
  amount: number;
  bankAccountId: string;
  method: PaymentMethod;
  interest?: number;
  fine?: number;
  discount?: number;
  notes?: string | null;
}

/**
 * Person type. Wire format is the string union <c>'Individual' | 'Company'</c>.
 * Exposed as a type AND a value object so existing call sites (`PersonType.Company`,
 * `useState<PersonType>`, equality checks) keep working without churn.
 */
export type PersonType = Schemas['PersonType'];
export const PersonType = {
  Individual: 'Individual',
  Company: 'Company',
} as const satisfies Record<string, PersonType>;

// Request bodies for the registry create/update methods. Derived from the response shapes (minus
// server-owned fields) rather than the generated *Request schemas: Swashbuckle can't reliably express
// the nullability of nested record members (e.g. `AddressRequest? Address`), so the generated request
// types would force a non-null address. These Omit-derived types match what the forms actually send.

/** BankInfo as sent to the API — all sub-fields nullable since they're optional in the request. */
export interface BankInfoInput {
  bankCode: string | null;
  branch: string | null;
  account: string | null;
  accountDigit: string | null;
  pixKey: string | null;
}
export type VendorInput = Omit<Vendor, 'id' | 'active' | 'bankInfo' | 'taxRegime' | 'createdAt'> & {
  bankInfo: BankInfoInput | null;
  taxRegime: TaxRegime;
};
export type CustomerInput = Omit<Customer, 'id' | 'active'>;
export type EmployeeInput = Omit<Employee, 'id' | 'active'>;

export type CepLookup = Schemas['CepResult'];
export type CnpjLookup = Schemas['CnpjResult'];

/** Builds list-query string shared by registry resources. */
function registryQuery(companyId: string, search?: string, page?: number, pageSize?: number) {
  const q = new URLSearchParams({ companyId });
  if (search?.trim()) q.set('search', search.trim());
  if (page) q.set('page', String(page));
  if (pageSize) q.set('pageSize', String(pageSize));
  return q.toString();
}

/** Shared filters of the Relação de Pagamentos (report list, summary and PDF export). */
export interface PaymentsReportParams {
  companyId?: string;
  /** Multi-select: units to keep. Empty/omitted = all units. */
  unitIds?: string[];
  /** Multi-select: naturezas orçamentárias by NAME (matched across companies), not cost-center id. */
  natures?: string[];
  /** Multi-select: lifecycle statuses to keep. Empty/omitted = every status. */
  statuses?: PayableStatus[];
  dueFrom?: string;
  dueTo?: string;
  payFrom?: string;
  payTo?: string;
  showImported?: boolean;
  search?: string;
}

function paymentsReportQuery(params: PaymentsReportParams) {
  const q = new URLSearchParams();
  // Consolidated "Todas" mode (AEL-906): the '__all__' sentinel isn't a CompanyId — omit it so the
  // backend runs over every company (the export URL is built directly here, so it can't rely on the
  // hooks that already strip the sentinel for list/summary).
  if (params.companyId && params.companyId !== '__all__') q.set('companyId', params.companyId);
  // Multi-select filters bind server-side as arrays from repeated query keys.
  for (const u of params.unitIds ?? []) q.append('unitIds', u);
  for (const n of params.natures ?? []) q.append('natures', n);
  for (const s of params.statuses ?? []) q.append('statuses', s);
  if (params.dueFrom) q.set('dueFrom', params.dueFrom);
  if (params.dueTo) q.set('dueTo', params.dueTo);
  if (params.payFrom) q.set('payFrom', params.payFrom);
  if (params.payTo) q.set('payTo', params.payTo);
  if (params.showImported) q.set('showImported', 'true');
  if (params.search?.trim()) q.set('search', params.search.trim());
  return q;
}

export const api = {
  devLogin: (email: string, name: string) =>
    request<Schemas['LoginResponse']>('/auth/dev-token', {
      method: 'POST',
      body: JSON.stringify({ email, name }),
    }),
  googleSignIn: (idToken: string) =>
    request<Schemas['LoginResponse']>('/auth/google', {
      method: 'POST',
      body: JSON.stringify({ idToken }),
    }),
  me: () => request<Schemas['MeResponse']>('/auth/me'),

  // Access administration (roles → permissions). Gated by admin:users on the back.
  listRoles: () => request<AdminRole[]>('/roles'),
  listPermissionCatalog: () => request<string[]>('/permissions'),
  setRolePermissions: (id: string, permissions: string[]) =>
    request<AdminRole>(`/roles/${id}/permissions`, { method: 'PUT', body: JSON.stringify({ permissions }) }),

  // User administration (role + unified intersection scope + active flag). admin:users.
  listUsers: () => request<AdminUser[]>('/users'),
  /** Pre-registers a user by e-mail (convite): sign-in is invitation-only, so this is how a person comes
   * into existence — the first Google login binds to this row, already with the role/scope set here. */
  createUser: (body: NewUser) => request<AdminUser>('/users', { method: 'POST', body: JSON.stringify(body) }),
  setUserScope: (
    id: string,
    companyIds: string[],
    units: UnitScopeAssignment[],
    costCenters: CostCenterScopeAssignment[],
  ) =>
    request<AdminUser>(`/users/${id}/scope`, {
      method: 'PUT',
      body: JSON.stringify({ companyIds, units, costCenters }),
    }),
  setUserRole: (id: string, role: string) =>
    request<AdminUser>(`/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  setUserActive: (id: string, active: boolean) =>
    request<AdminUser>(`/users/${id}/active`, { method: 'PUT', body: JSON.stringify({ active }) }),
  setBudgetAdjustmentApprover: (id: string, canApprove: boolean) =>
    request<AdminUser>(`/users/${id}/adjustment-approver`, { method: 'PUT', body: JSON.stringify({ canApprove }) }),

  listCompanies: () => request<Company[]>('/companies'),
  createCompany: (legalName: string, cnpj: string) =>
    request<Company>('/companies', { method: 'POST', body: JSON.stringify({ legalName, cnpj }) }),
  updateCompany: (id: string, legalName: string, cnpj: string) =>
    request<Company>(`/companies/${id}`, { method: 'PUT', body: JSON.stringify({ legalName, cnpj }) }),
  deactivateCompany: (id: string) => request<Company>(`/companies/${id}/deactivate`, { method: 'POST' }),
  activateCompany: (id: string) => request<Company>(`/companies/${id}/activate`, { method: 'POST' }),
  removeCompany: (id: string) => request<void>(`/companies/${id}`, { method: 'DELETE' }),

  listUnits: (companyId: string, scoped = false) =>
    request<Unit[]>(`/units?companyId=${companyId}${scoped ? '&scoped=true' : ''}`),
  /** All units across companies (admin screens group them by company). */
  listAllUnits: () => request<Unit[]>('/units'),
  createUnit: (companyId: string, name: string, acronym: string, cnpj?: string | null, isMatriz?: boolean) =>
    request<Unit>('/units', { method: 'POST', body: JSON.stringify({ companyId, name, acronym, cnpj, isMatriz }) }),
  updateUnit: (id: string, name: string, acronym: string, cnpj?: string | null, isMatriz?: boolean) =>
    request<Unit>(`/units/${id}`, { method: 'PUT', body: JSON.stringify({ name, acronym, cnpj, isMatriz }) }),

  listCostCenters: (companyId: string, scoped = false) =>
    request<CostCenter[]>(`/cost-centers?companyId=${companyId}${scoped ? '&scoped=true' : ''}`),
  /** All cost centers across companies (admin screens group them by company). */
  listAllCostCenters: () => request<CostCenter[]>('/cost-centers'),
  getCostCenter: (id: string) => request<CostCenter>(`/cost-centers/${id}`),
  createCostCenter: (companyId: string, code: string, name: string, autoApprovalLimit: number, unitIds: string[]) =>
    request<CostCenter>('/cost-centers', {
      method: 'POST',
      body: JSON.stringify({ companyId, code, name, autoApprovalLimit, unitIds }),
    }),
  updateCostCenter: (id: string, code: string, name: string, autoApprovalLimit: number, unitIds: string[]) =>
    request<CostCenter>(`/cost-centers/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ code, name, autoApprovalLimit, unitIds }),
    }),
  // Cost-center budget (monthly cells, Core) — create-by-period, list, update, delete.
  listCostCenterBudgets: (
    costCenterId: string,
    params: { unitId?: string; competencyFrom?: string; competencyTo?: string } = {},
  ) => {
    const q = new URLSearchParams();
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.competencyFrom) q.set('competencyFrom', params.competencyFrom);
    if (params.competencyTo) q.set('competencyTo', params.competencyTo);
    const qs = q.toString();
    return request<CostCenterUnitBudget[]>(`/cost-centers/${costCenterId}/unit-budgets${qs ? `?${qs}` : ''}`);
  },
  createCostCenterBudget: (costCenterId: string, input: CostCenterUnitBudgetInput) =>
    request<CostCenterUnitBudgetPeriod>(`/cost-centers/${costCenterId}/unit-budgets`, {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  updateCostCenterBudget: (costCenterId: string, budgetId: string, input: CostCenterUnitBudgetUpdateInput) =>
    request<CostCenterUnitBudget>(`/cost-centers/${costCenterId}/unit-budgets/${budgetId}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    }),
  deleteCostCenterBudget: (costCenterId: string, budgetId: string) =>
    request<void>(`/cost-centers/${costCenterId}/unit-budgets/${budgetId}`, { method: 'DELETE' }),

  listTransactions: (params: {
    companyId?: string;
    type?: TransactionType;
    status?: TransactionStatus;
    costCenterId?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.type) q.set('type', params.type);
    if (params.status) q.set('status', params.status);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<Transaction>>(`/transactions?${q}`);
  },
  getTransaction: (id: string) => request<Transaction>(`/transactions/${id}`),
  createTransaction: (body: {
    companyId: string;
    unitId: string;
    costCenterId: string;
    type: TransactionType;
    description: string;
    vendorId?: string | null;
    customerId?: string | null;
    amount: number;
    dueDate: string;
  }) => request<Transaction>('/transactions', { method: 'POST', body: JSON.stringify(body) }),
  approveTransaction: (id: string) => request<Transaction>(`/transactions/${id}/approve`, { method: 'POST' }),
  rejectTransaction: (id: string, reason: string) =>
    request<Transaction>(`/transactions/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
  payTransaction: (id: string) => request<Transaction>(`/transactions/${id}/pay`, { method: 'POST' }),

  // ---------- Payables (accounts payable) ----------
  listPayables: (params: {
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
    /** NEW (AEL-871): effective method filter (payable override ?? account default). */
    paymentMethod?: PaymentMethod;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.vendorId) q.set('vendorId', params.vendorId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.categoryId) q.set('categoryId', params.categoryId);
    if (params.competency) q.set('competency', params.competency);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    if (params.paid !== undefined) q.set('paid', String(params.paid));
    if (params.classified !== undefined) q.set('classified', String(params.classified));
    if (params.search?.trim()) q.set('search', params.search.trim());
    if (params.paymentMethod) q.set('paymentMethod', params.paymentMethod);
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<PayableListItem>>(`/payables?${q}`);
  },
  getPayable: (id: string) => request<Payable>(`/payables/${id}`),
  payableAccountingView: (id: string) => request<PayableAccountingView>(`/payables/${id}/accounting-view`),
  confirmAccounting: (id: string) => request<Payable>(`/payables/${id}/accounting/confirm`, { method: 'POST' }),
  // "Liberar para pagamento": the tax reviewer confirms the tax analysis (→ Scheduled + accounting incurral).
  confirmTaxAnalysis: (id: string) => request<Payable>(`/payables/${id}/tax-analysis/confirm`, { method: 'POST' }),
  requestFiscalDocument: (id: string) => request<void>(`/payables/${id}/request-fiscal-document`, { method: 'POST' }),
  payableStageSummary: (companyId?: string): Promise<PayableStageSummaryResponse> => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    return request<PayableStageSummaryResponse>(`/payables/stage-summary?${q}`);
  },
  // NEW (AEL-871): group-header aggregates for the payment queue; same filters as listPayables.
  paymentQueueSummary: (params: {
    companyId?: string;
    status?: PayableStatus;
    paid?: boolean;
    search?: string;
    paymentMethod?: PaymentMethod;
  }): Promise<PaymentQueueSummary> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.paid !== undefined) q.set('paid', String(params.paid));
    if (params.search?.trim()) q.set('search', params.search.trim());
    if (params.paymentMethod) q.set('paymentMethod', params.paymentMethod);
    return request<PaymentQueueSummary>(`/payables/payment-queue-summary?${q}`);
  },
  // NEW (AEL-871): cross-payable settlement history (Histórico de baixas).
  listPayments: (params: {
    companyId?: string;
    from?: string;
    to?: string;
    bankAccountId?: string;
    method?: PaymentMethod;
    search?: string;
    includeReversed?: boolean;
    page?: number;
    pageSize?: number;
  }): Promise<Paginated<PaymentHistoryItem>> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.bankAccountId) q.set('bankAccountId', params.bankAccountId);
    if (params.method) q.set('method', params.method);
    if (params.search?.trim()) q.set('search', params.search.trim());
    if (params.includeReversed !== undefined) q.set('includeReversed', String(params.includeReversed));
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<PaymentHistoryItem>>(`/payables/payments?${q}`);
  },
  // ---------- Relação de Pagamentos (report, AEL-958) ----------
  paymentsReport: (params: PaymentsReportParams & { page?: number; pageSize?: number }) => {
    const q = paymentsReportQuery(params);
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<PaymentReportItem>>(`/payables/report?${q}`);
  },
  paymentsReportSummary: (params: PaymentsReportParams) =>
    request<PaymentsReportSummary>(`/payables/report/summary?${paymentsReportQuery(params)}`),
  /** Direct URL for the PDF export (application/pdf), fetched via downloadWithAuth. */
  paymentsReportExportUrl: (params: PaymentsReportParams & { showDescription?: boolean }) => {
    const q = paymentsReportQuery(params);
    if (params.showDescription) q.set('showDescription', 'true');
    return `${BASE_URL}/payables/report/export?${q}`;
  },
  payableStageItems: (params: {
    companyId?: string;
    status?: PayableStatus;
    hasFiscalDocument?: boolean;
    origin?: PayableOrigin;
    /** Requisition auto-schedule review gate (AEL-846): only payables still awaiting review. */
    reviewPending?: boolean;
    /** Free-text search (AEL-865): vendor / description / NF number / requisition code. */
    search?: string;
    page?: number;
    pageSize?: number;
  }): Promise<Paginated<PayableStageItem>> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.hasFiscalDocument !== undefined) q.set('hasFiscalDocument', String(params.hasFiscalDocument));
    if (params.origin) q.set('origin', params.origin);
    if (params.reviewPending !== undefined) q.set('reviewPending', String(params.reviewPending));
    if (params.search?.trim()) q.set('search', params.search.trim());
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<PayableStageItem>>(`/payables/stage-items?${q}`);
  },
  payableAuditTrail: (id: string): Promise<AuditTrailEvent[]> =>
    request<AuditTrailEvent[]>(`/payables/${id}/audit-trail`),
  createPayable: (body: PayableInput) => request<Payable>('/payables', { method: 'POST', body: JSON.stringify(body) }),
  updatePayable: (id: string, body: Omit<PayableInput, 'origin'>) =>
    request<Payable>(`/payables/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  /** Single-field edit of the número do documento (número da NF) from the Contábil review drawer. */
  setPayableDocumentNumber: (id: string, documentNumber: string | null) =>
    request<Payable>(`/payables/${id}/document-number`, { method: 'POST', body: JSON.stringify({ documentNumber }) }),
  approvePayable: (id: string) => request<Payable>(`/payables/${id}/approve`, { method: 'POST' }),
  rejectPayable: (id: string, reason: string, returnTo?: PayableReturnTarget) =>
    request<Payable>(`/payables/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason, returnTo }) }),
  // AEL-935 — Finance returns a requisition-sourced title to the requester/controllership (payables:write).
  returnPayable: (id: string, reason: string, returnTo: PayableReturnTarget) =>
    request<Payable>(`/payables/${id}/return`, { method: 'POST', body: JSON.stringify({ reason, returnTo }) }),
  cancelPayable: (id: string, reason: string) =>
    request<Payable>(`/payables/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  /** Uploads an attachment via multipart/form-data. Uses fetch directly so the browser sets the boundary.
   * Pass kind 'FiscalDocument' when the file is the Nota Fiscal so it clears the "NF pendente" gate. */
  uploadPayableAttachment: async (id: string, file: File, kind?: PayableAttachmentKind) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const url = `${BASE_URL}/payables/${id}/attachments${kind ? `?kind=${kind}` : ''}`;
    const res = await fetch(url, { method: 'POST', headers, body: form });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<PayableAttachment>;
  },
  /** Direct download URL for an attachment (auth handled by the browser session / token in caller). */
  downloadPayableAttachmentUrl: (id: string, attachmentId: string) =>
    `${BASE_URL}/payables/${id}/attachments/${attachmentId}`,
  deletePayableAttachment: (id: string, attachmentId: string) =>
    request<void>(`/payables/${id}/attachments/${attachmentId}`, { method: 'DELETE' }),
  setPayableWithholdings: (
    id: string,
    items: { taxType: Schemas['WithholdingTaxType']; baseAmount: number; rate?: number | null; amount: number }[],
  ) => request<Payable>(`/payables/${id}/withholdings`, { method: 'PUT', body: JSON.stringify({ items }) }),
  // ---------- Fiscal documents (NF reader) ----------
  /** Requests a presigned/SAS PUT URL for uploading a fiscal document directly to blob storage. */
  requestFiscalUploadUrl: (companyId: string, fileName: string) =>
    request<UploadTarget>(`/payables/fiscal-document/upload-url?companyId=${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ fileName }),
    }),
  /**
   * Stateless parse/preview of an NF already in object storage (by key). Persists nothing; feeds the form.
   * The file must have been uploaded first via the presigned URL from `requestFiscalUploadUrl`.
   */
  parseFiscalDocument: (companyId: string, key: string, fileName: string) =>
    request<ParsedFiscalDocument>(`/payables/fiscal-document/parse?companyId=${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ key, fileName }),
    }),
  /** Persists the fiscal document of record against a payable (references an already-uploaded key). */
  uploadPayableFiscalDocument: (
    id: string,
    body: { key: string; fileName: string; contentType: string; sizeBytes: number },
  ) =>
    request<FiscalDocument>(`/payables/${id}/fiscal-document`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  // ---------- Boleto reader (AEL-938) — Budgeting/requisition endpoints ----------
  /** Presigned PUT URL for uploading a boleto PDF/image; companyId scopes the storage key (AEL-764). */
  requestBoletoUploadUrl: (companyId: string, fileName: string) =>
    request<UploadTarget>(`/requisitions/fiscal-document/upload-url?companyId=${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ fileName }),
    }),
  /** Stateless AI parse/preview of a boleto already in storage (by key) — feeds one installment; persists nothing. */
  parseBoleto: (companyId: string, key: string, fileName: string) =>
    request<ParsedBoleto>(`/requisitions/fiscal-document/parse-boleto?companyId=${companyId}`, {
      method: 'POST',
      body: JSON.stringify({ key, fileName }),
    }),

  // ---------- Installment hold / release ----------
  holdInstallment: (id: string, installmentId: string, reason: string) =>
    request<void>(`/payables/${id}/installments/${installmentId}/hold`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  releaseInstallmentHold: (id: string, installmentId: string) =>
    request<void>(`/payables/${id}/installments/${installmentId}/release-hold`, { method: 'POST' }),

  // ---------- Payments (settlement) ----------
  registerPayment: (payableId: string, installmentId: string, body: RegisterPaymentInput) =>
    request<Payable>(`/payables/${payableId}/installments/${installmentId}/payments`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  reversePayment: (payableId: string, paymentId: string, reason: string) =>
    request<Payable>(`/payables/${payableId}/payments/${paymentId}/reverse`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  /** Uploads a payment proof via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  uploadPaymentProof: async (payableId: string, paymentId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/payables/${payableId}/payments/${paymentId}/proof`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<Payment>;
  },
  /** Direct download URL for a payment proof (auth handled by the browser session / token in caller). */
  downloadPaymentProofUrl: (payableId: string, paymentId: string) =>
    `${BASE_URL}/payables/${payableId}/payments/${paymentId}/proof`,

  // ---------- Receivables (accounts receivable) ----------
  listReceivables: (params: {
    companyId?: string;
    status?: ReceivableStatus;
    payerId?: string;
    costCenterId?: string;
    dueFrom?: string;
    dueTo?: string;
    received?: boolean;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.payerId) q.set('payerId', params.payerId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    if (params.received !== undefined) q.set('received', String(params.received));
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<ReceivableListItem>>(`/receivables?${q}`);
  },
  getReceivable: (id: string) => request<Receivable>(`/receivables/${id}`),
  receivableStageSummary: (companyId?: string): Promise<ReceivableStageSummaryResponse> => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    return request<ReceivableStageSummaryResponse>(`/receivables/stage-summary?${q}`);
  },
  receivableStageItems: (params: {
    companyId?: string;
    status?: ReceivableStatus;
    page?: number;
    pageSize?: number;
  }): Promise<Paginated<ReceivableStageItem>> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<ReceivableStageItem>>(`/receivables/stage-items?${q}`);
  },
  receivableAuditTrail: (id: string): Promise<AuditTrailEvent[]> =>
    request<AuditTrailEvent[]>(`/receivables/${id}/audit-trail`),
  createReceivable: (body: ReceivableInput) =>
    request<Receivable>('/receivables', { method: 'POST', body: JSON.stringify(body) }),
  updateReceivable: (id: string, body: Omit<ReceivableInput, 'origin'>) =>
    request<Receivable>(`/receivables/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  cancelReceivable: (id: string, reason: string) =>
    request<Receivable>(`/receivables/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),

  // ---------- Receipts (inbound settlement) ----------
  registerReceipt: (receivableId: string, installmentId: string, body: RegisterReceiptInput) =>
    request<Receivable>(`/receivables/${receivableId}/installments/${installmentId}/receipts`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  reverseReceipt: (receivableId: string, receiptId: string, reason: string) =>
    request<Receivable>(`/receivables/${receivableId}/receipts/${receiptId}/reverse`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  /** Uploads a receipt proof via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  uploadReceiptProof: async (receivableId: string, receiptId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/receivables/${receivableId}/receipts/${receiptId}/proof`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<Receipt>;
  },
  /** Direct download URL for a receipt proof (auth handled by the browser session / token in caller). */
  downloadReceiptProofUrl: (receivableId: string, receiptId: string) =>
    `${BASE_URL}/receivables/${receivableId}/receipts/${receiptId}/proof`,

  // ---------- Bank accounts ----------
  listBankAccounts: (companyId: string, includeInactive?: boolean) => {
    const q = new URLSearchParams({ companyId });
    if (includeInactive) q.set('includeInactive', 'true');
    return request<BankAccount[]>(`/bank-accounts?${q}`);
  },
  createBankAccount: (body: BankAccountInput) =>
    request<BankAccount>('/bank-accounts', { method: 'POST', body: JSON.stringify(body) }),
  updateBankAccount: (id: string, body: BankAccountUpdateInput) =>
    request<BankAccount>(`/bank-accounts/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteBankAccount: (id: string) => request<void>(`/bank-accounts/${id}`, { method: 'DELETE' }),

  // ---------- OFX reconciliation (statement imports + bank transactions) ----------
  /** Imports an OFX statement via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  importStatement: async (bankAccountId: string, file: File) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/statement-imports?bankAccountId=${bankAccountId}`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<StatementImport>;
  },
  listStatementImports: (companyId?: string, bankAccountId?: string) => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    if (bankAccountId) q.set('bankAccountId', bankAccountId);
    return request<StatementImport[]>(`/statement-imports?${q}`);
  },
  listBankTransactions: (params: {
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
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.bankAccountId) q.set('bankAccountId', params.bankAccountId);
    if (params.importId) q.set('importId', params.importId);
    if (params.matchStatus) q.set('matchStatus', params.matchStatus);
    if (params.stage) q.set('stage', params.stage);
    if (params.direction) q.set('direction', params.direction);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<BankTransaction>>(`/bank-transactions?${q}`);
  },
  periodSummary: (params: {
    companyId?: string;
    bankAccountId?: string;
    direction?: TransactionDirection;
    dueFrom?: string;
    dueTo?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.bankAccountId) q.set('bankAccountId', params.bankAccountId);
    if (params.direction) q.set('direction', params.direction);
    if (params.dueFrom) q.set('dueFrom', params.dueFrom);
    if (params.dueTo) q.set('dueTo', params.dueTo);
    return request<PeriodSummary>(`/bank-transactions/period-summary?${q}`);
  },
  confirmMatch: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/confirm`, { method: 'POST' }),
  matchTransaction: (id: string, installmentId: string) =>
    request<BankTransaction>(`/bank-transactions/${id}/match`, {
      method: 'POST',
      body: JSON.stringify({ installmentId }),
    }),
  ignoreTransaction: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/ignore`, { method: 'POST' }),
  unmatchTransaction: (id: string) => request<BankTransaction>(`/bank-transactions/${id}/unmatch`, { method: 'POST' }),
  /** Bulk-ignore all still-open (Unmatched/Suggested) transactions of an account on a given day. */
  clearReconciliation: (body: { companyId: string; bankAccountId: string; date: string }) =>
    request<{ ignored: number }>('/bank-transactions/clear', { method: 'POST', body: JSON.stringify(body) }),

  // ---------- Classification ----------
  classifyPayable: (id: string, body: { accountingAccountId?: string | null }) =>
    request<Payable>(`/payables/${id}/classify`, { method: 'POST', body: JSON.stringify(body) }),
  classifyBatch: (body: { payableIds: string[]; accountingAccountId?: string | null }) =>
    request<{ classifiedCount: number }>('/payables/classify-batch', { method: 'POST', body: JSON.stringify(body) }),
  classificationSuggestion: (payableId: string) =>
    request<ClassificationSuggestion>(`/payables/${payableId}/classification-suggestion`),
  aiClassificationSuggestion: (payableId: string) =>
    request<AiClassificationSuggestion>(`/payables/${payableId}/ai-classification-suggestion`, { method: 'POST' }),

  // ---------- Classification Policies ----------
  getClassificationPolicy: (companyId: string) =>
    request<ClassificationPolicy>(`/classification-policies/${companyId}`),
  updateClassificationPolicy: (companyId: string, body: { requireToApprove: boolean; requireToPay: boolean }) =>
    request<ClassificationPolicy>(`/classification-policies/${companyId}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  // ---------- Payables aging ----------
  aging: (companyId?: string, asOf?: string): Promise<AgingReport> => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    if (asOf) q.set('asOf', asOf);
    return request<AgingReport>(`/payables/aging?${q}`);
  },
  agingItems: (params: {
    companyId?: string;
    bucket?: AgingBucket;
    vendorId?: string;
    costCenterId?: string;
    asOf?: string;
  }): Promise<AgingItem[]> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.bucket) q.set('bucket', params.bucket);
    if (params.vendorId) q.set('vendorId', params.vendorId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.asOf) q.set('asOf', params.asOf);
    return request<AgingItem[]>(`/payables/aging/items?${q}`);
  },

  // ---------- Cash flow ----------
  cashFlow: (params: {
    companyId?: string;
    granularity?: CashFlowGranularity;
    horizon?: number;
    asOf?: string;
    minReserve?: number;
  }): Promise<CashFlowResponse> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.granularity) q.set('granularity', params.granularity);
    if (params.horizon != null) q.set('horizon', String(params.horizon));
    if (params.asOf) q.set('asOf', params.asOf);
    if (params.minReserve != null) q.set('minReserve', String(params.minReserve));
    return request<CashFlowResponse>(`/cash-flow?${q}`);
  },

  // ---------- SLA report (SLA analysis) ----------
  slaReport: (params: {
    companyId?: string;
    unitId?: string;
    from?: string;
    to?: string;
  }): Promise<SlaReportResponse> => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    return request<SlaReportResponse>(`/requisitions/sla-report?${q}`);
  },
  cashFlowItems: (params: {
    companyId?: string;
    from: string;
    to: string;
    direction: 'in' | 'out';
    asOf?: string;
  }): Promise<CashFlowItem[]> => {
    const q = new URLSearchParams({ from: params.from, to: params.to, direction: params.direction });
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.asOf) q.set('asOf', params.asOf);
    return request<CashFlowItem[]>(`/cash-flow/items?${q}`);
  },
  setReconciledBalance: (
    bankAccountId: string,
    body: { balance: number; asOf: string },
  ): Promise<ReconciledBalanceResult> =>
    request<ReconciledBalanceResult>(`/bank-accounts/${bankAccountId}/reconciled-balance`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  // ---------- Expense categories ----------
  listExpenseCategories: (includeInactive?: boolean) =>
    request<ExpenseCategory[]>(`/expense-categories${includeInactive ? '?includeInactive=true' : ''}`),
  createExpenseCategory: (name: string) =>
    request<ExpenseCategory>('/expense-categories', { method: 'POST', body: JSON.stringify({ name }) }),
  updateExpenseCategory: (id: string, name: string, active: boolean) =>
    request<ExpenseCategory>(`/expense-categories/${id}`, { method: 'PUT', body: JSON.stringify({ name, active }) }),
  deleteExpenseCategory: (id: string) => request<void>(`/expense-categories/${id}`, { method: 'DELETE' }),

  // ---------- Withholding rules ----------
  listWithholdingRules: (includeInactive?: boolean) =>
    request<WithholdingRule[]>(`/withholding-rules${includeInactive ? '?includeInactive=true' : ''}`),
  // System default per tax for the vendor's tax profile (vendor overrides excluded).
  previewWithholdingRules: (params: WithholdingPreviewParams) => {
    const qs = new URLSearchParams({ regime: params.regime });
    if (params.companyId) qs.set('companyId', params.companyId);
    if (params.cprb != null) qs.set('cprb', String(params.cprb));
    if (params.simplesAnexo) qs.set('simplesAnexo', params.simplesAnexo);
    return request<WithholdingPreviewItem[]>(`/withholding-rules/preview?${qs.toString()}`);
  },
  createWithholdingRule: (body: WithholdingRuleInput) =>
    request<WithholdingRule>('/withholding-rules', { method: 'POST', body: JSON.stringify(body) }),
  updateWithholdingRule: (id: string, body: WithholdingRuleInput) =>
    request<WithholdingRule>(`/withholding-rules/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deleteWithholdingRule: (id: string) => request<void>(`/withholding-rules/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Vendors ----------
  // Vendors are global to the organization — the list takes no company filter (unlike customers/employees).
  listVendors: (search?: string, page?: number, pageSize?: number) => {
    const q = new URLSearchParams();
    if (search?.trim()) q.set('search', search.trim());
    if (page) q.set('page', String(page));
    if (pageSize) q.set('pageSize', String(pageSize));
    const qs = q.toString();
    return request<Paginated<Vendor>>(`/vendors${qs ? `?${qs}` : ''}`);
  },
  getVendor: (id: string) => request<Vendor>(`/vendors/${id}`),
  createVendor: (body: VendorInput) => request<Vendor>('/vendors', { method: 'POST', body: JSON.stringify(body) }),
  updateVendor: (id: string, body: VendorInput) =>
    request<Vendor>(`/vendors/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateVendor: (id: string) => request<Vendor>(`/vendors/${id}/deactivate`, { method: 'POST' }),
  activateVendor: (id: string) => request<Vendor>(`/vendors/${id}/activate`, { method: 'POST' }),
  removeVendor: (id: string) => request<void>(`/vendors/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Customers ----------
  listCustomers: (companyId: string, search?: string, page?: number, pageSize?: number) =>
    request<Paginated<Customer>>(`/customers?${registryQuery(companyId, search, page, pageSize)}`),
  getCustomer: (id: string) => request<Customer>(`/customers/${id}`),
  createCustomer: (body: CustomerInput) =>
    request<Customer>('/customers', { method: 'POST', body: JSON.stringify(body) }),
  updateCustomer: (id: string, body: CustomerInput) =>
    request<Customer>(`/customers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateCustomer: (id: string) => request<Customer>(`/customers/${id}/deactivate`, { method: 'POST' }),
  activateCustomer: (id: string) => request<Customer>(`/customers/${id}/activate`, { method: 'POST' }),
  removeCustomer: (id: string) => request<void>(`/customers/${id}`, { method: 'DELETE' }),

  // ---------- Registry: Employees ----------
  listEmployees: (companyId: string, search?: string, page?: number, pageSize?: number) =>
    request<Paginated<Employee>>(`/employees?${registryQuery(companyId, search, page, pageSize)}`),
  getEmployee: (id: string) => request<Employee>(`/employees/${id}`),
  createEmployee: (body: EmployeeInput) =>
    request<Employee>('/employees', { method: 'POST', body: JSON.stringify(body) }),
  updateEmployee: (id: string, body: EmployeeInput) =>
    request<Employee>(`/employees/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deactivateEmployee: (id: string) => request<Employee>(`/employees/${id}/deactivate`, { method: 'POST' }),
  activateEmployee: (id: string) => request<Employee>(`/employees/${id}/activate`, { method: 'POST' }),
  removeEmployee: (id: string) => request<void>(`/employees/${id}`, { method: 'DELETE' }),

  // ---------- Accounting: Ledger accounts (accounting chart of accounts) ----------
  listLedgerAccounts: (params: { accountGroup?: AccountGroup; kind?: LedgerAccountKind; activeOnly?: boolean }) => {
    const q = new URLSearchParams();
    if (params.accountGroup) q.set('accountGroup', params.accountGroup);
    if (params.kind) q.set('kind', params.kind);
    if (params.activeOnly != null) q.set('activeOnly', String(params.activeOnly));
    return request<LedgerAccount[]>(`/ledger-accounts?${q}`);
  },
  getLedgerAccount: (id: string) => request<LedgerAccount>(`/ledger-accounts/${id}`),
  createLedgerAccount: (body: LedgerAccountInput) =>
    request<LedgerAccount>('/ledger-accounts', { method: 'POST', body: JSON.stringify(body) }),
  updateLedgerAccount: (id: string, body: LedgerAccountUpdateInput) =>
    request<LedgerAccount>(`/ledger-accounts/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  seedLedgerAccounts: () => request<LedgerAccount[]>('/ledger-accounts/seed', { method: 'POST' }),

  // ---------- Accounting: Journal entries ----------
  listJournalEntries: (params: { companyId?: string; status?: JournalStatus; from?: string; to?: string }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    return request<JournalEntry[]>(`/journal-entries?${q}`);
  },
  getJournalEntry: (id: string) => request<JournalEntry>(`/journal-entries/${id}`),
  createJournalEntry: (body: JournalEntryInput) =>
    request<JournalEntry>('/journal-entries', { method: 'POST', body: JSON.stringify(body) }),
  updateJournalEntry: (id: string, body: Omit<JournalEntryInput, 'companyId'>) =>
    request<JournalEntry>(`/journal-entries/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  postJournalEntry: (id: string) => request<JournalEntry>(`/journal-entries/${id}/post`, { method: 'POST' }),
  reverseJournalEntry: (id: string, reason: string, competenceDate?: string | null) =>
    request<JournalEntry>(`/journal-entries/${id}/reverse`, {
      method: 'POST',
      body: JSON.stringify({ reason, competenceDate: competenceDate ?? null }),
    }),

  // ---------- Accounting: Reports ----------
  trialBalance: (params: {
    companyId?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    return request<TrialBalance>(`/accounting/trial-balance?${q}`);
  },
  trialBalanceExportUrl: (params: {
    companyId?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    return `${BASE_URL}/accounting/trial-balance/export?${q}`;
  },
  incomeStatement: (params: {
    companyId?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    return request<IncomeStatement>(`/accounting/income-statement?${q}`);
  },
  incomeStatementExportUrl: (params: {
    companyId?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    return `${BASE_URL}/accounting/income-statement/export?${q}`;
  },
  yearEndClosing: (companyId: string, fiscalYear: number) =>
    request<YearEndClosingResult>('/accounting/year-end-closing', {
      method: 'POST',
      body: JSON.stringify({ companyId, fiscalYear }),
    }),
  balanceSheet: (params: { companyId?: string; asOf?: string }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.asOf) q.set('asOf', params.asOf);
    return request<BalanceSheet>(`/accounting/balance-sheet?${q}`);
  },
  journalBook: (params: { companyId?: string; from?: string; to?: string; unitId?: string }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.unitId) q.set('unitId', params.unitId);
    return request<JournalBook>(`/accounting/journal-book?${q}`);
  },
  generalLedger: (params: {
    companyId?: string;
    ledgerAccountId?: string;
    root?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.ledgerAccountId) q.set('ledgerAccountId', params.ledgerAccountId);
    if (params.root) q.set('root', params.root);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<GeneralLedgerAccountView>>(`/accounting/ledger?${q}`);
  },
  /** Direct URL for the ledger CSV export (text/csv). Requires Bearer → fetch via downloadGeneralLedgerExport. */
  generalLedgerExportUrl: (params: {
    companyId?: string;
    ledgerAccountId?: string;
    root?: string;
    from?: string;
    to?: string;
    costCenterId?: string;
    unitId?: string;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.ledgerAccountId) q.set('ledgerAccountId', params.ledgerAccountId);
    if (params.root) q.set('root', params.root);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.unitId) q.set('unitId', params.unitId);
    return `${BASE_URL}/accounting/ledger/export?${q}`;
  },
  withholdingsReport: (companyId?: string) => {
    const q = new URLSearchParams();
    if (companyId) q.set('companyId', companyId);
    return request<WithholdingsReport>(`/payables/withholdings?${q}`);
  },

  // ---------- Fiscal: withholding assessment (basis of the Obrigações Acessórias panel) ----------
  fiscalAssessment: (params: {
    perApur: string;
    companyId?: string;
    costCenterId?: string;
    taxType?: WithholdingTaxType;
    retentionGroup?: RetentionGroup;
  }) => {
    const q = new URLSearchParams({ perApur: params.perApur });
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.taxType) q.set('taxType', params.taxType);
    if (params.retentionGroup) q.set('retentionGroup', params.retentionGroup);
    return request<FiscalAssessment>(`/fiscal/assessment?${q}`);
  },
  /** Direct URL do export CSV (text/csv). Requer Bearer → baixe via downloadAssessmentCsv (fetch+blob). */
  fiscalAssessmentCsvUrl: (params: {
    perApur: string;
    companyId?: string;
    costCenterId?: string;
    taxType?: WithholdingTaxType;
    retentionGroup?: RetentionGroup;
  }) => {
    const q = new URLSearchParams({ perApur: params.perApur });
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.taxType) q.set('taxType', params.taxType);
    if (params.retentionGroup) q.set('retentionGroup', params.retentionGroup);
    return `${BASE_URL}/fiscal/assessment/csv?${q}`;
  },
  /** Direct URL do XML da EFD-Reinf (R-4020). Requer empresa (contribuinte) + Bearer → baixe via downloadEfdReinfXml. */
  efdReinfXmlUrl: (params: { perApur: string; companyId: string; tpAmb?: number }) => {
    const q = new URLSearchParams({ perApur: params.perApur });
    // Only sends companyId when present (same pattern as CSV/assessment). An empty `companyId=` would make the
    // CompanyId? binding on the endpoint return an opaque 400; absent falls into the clean "Selecione a empresa" guard.
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.tpAmb != null) q.set('tpAmb', String(params.tpAmb));
    return `${BASE_URL}/fiscal/assessment/efd-reinf.xml?${q}`;
  },

  // ---------- Registry: Lookups ----------
  lookupCep: (cep: string) => request<CepLookup>(`/lookup/cep/${cep}`),
  lookupCnpj: (cnpj: string) => request<CnpjLookup>(`/lookup/cnpj/${cnpj}`),

  // ---------- Remittances (CNAB240) ----------
  listRemittances: (params: {
    companyId?: string;
    status?: RemittanceStatus;
    category?: RemittanceCategory;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.status) q.set('status', params.status);
    if (params.category) q.set('category', params.category);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<RemittanceListItem>>(`/remittances?${q}`);
  },
  /** Flat list of remittance títulos (AEL-958), filterable by unidade/natureza/forma/status/período. */
  listRemittanceLines: (params: {
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
  }) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.category) q.set('category', params.category);
    if (params.status) q.set('status', params.status);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.remittanceId) q.set('remittanceId', params.remittanceId);
    if (params.page) q.set('Page', String(params.page));
    if (params.pageSize) q.set('PageSize', String(params.pageSize));
    return request<Paginated<RemittanceLineItem>>(`/remittances/lines?${q}`);
  },
  getRemittance: (id: string) => request<Remittance>(`/remittances/${id}`),
  createRemittance: (body: CreateRemittanceInput) =>
    request<CreateRemittanceResponse>('/remittances', { method: 'POST', body: JSON.stringify(body) }),
  cancelRemittance: (id: string) => request<Remittance>(`/remittances/${id}/cancel`, { method: 'POST' }),
  /** Direct URL for downloading the CNAB240 .REM file (text/plain). */
  remittanceFileUrl: (id: string) => `${BASE_URL}/remittances/${id}/file`,

  /** Imports a .RET return file via multipart/form-data. Uses fetch directly so the browser sets the boundary. */
  importRemittanceReturn: async (id: string, file: File): Promise<RemittanceReturnOutcome> => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/remittances/${id}/return`, { method: 'POST', headers, body: form });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<RemittanceReturnOutcome>;
  },
  /** Dry-run: previews a .RET/.txt/.csv return against a paying account's pending lines. No baixa. */
  remittanceReturnPreview: async (payingBankAccountId: string, file: File): Promise<RemittanceReturnPreview> => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/remittances/return/preview/${payingBankAccountId}`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) await throwFromResponse(res);
    return res.json() as Promise<RemittanceReturnPreview>;
  },
  /**
   * Upload flow: auto-confirms the "Pago" lines (baixa aplicada) and returns the refreshed preview so the
   * operator only handles divergent/rejected/unmatched manually. `autoConfirmed` = how many were settled.
   */
  remittanceReturnAutoConfirm: async (payingBankAccountId: string, file: File): Promise<RemittanceReturnPreview> => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/remittances/return/auto-confirm/${payingBankAccountId}`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) await throwFromResponse(res);
    return res.json() as Promise<RemittanceReturnPreview>;
  },
  /** Return files already processed for a paying account, newest first. */
  remittanceReturnHistory: (payingBankAccountId: string, params: { page?: number; pageSize?: number } = {}) => {
    const q = new URLSearchParams();
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return request<Paginated<RemittanceReturnHistoryItem>>(
      `/remittances/return/history/${payingBankAccountId}${qs ? `?${qs}` : ''}`,
    );
  },
  /** Direct URL for downloading a stored .RET return file (text/plain). */
  remittanceReturnFileUrl: (returnId: string) => `${BASE_URL}/remittances/return/history/${returnId}/file`,

  /** Reads a CRÍTICA file (.RET/.txt) and reports accepted/rejected per record. Applies NO baixa. */
  remittanceReturnCriticism: async (payingBankAccountId: string, file: File): Promise<RemittanceCriticism> => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const res = await fetch(`${BASE_URL}/remittances/return/criticism/${payingBankAccountId}`, {
      method: 'POST',
      headers,
      body: form,
    });
    if (!res.ok) await throwFromResponse(res);
    return res.json() as Promise<RemittanceCriticism>;
  },
  /** Manually confirms a remittance line as paid, optionally recording the return's settlement data. */
  confirmRemittanceLine: (id: string, lineId: string, ret?: RemittanceReturnLineData) => {
    const q = new URLSearchParams();
    if (ret?.effectiveDate) q.set('effectiveDate', ret.effectiveDate);
    if (ret?.bankReference) q.set('bankReference', ret.bankReference);
    if (ret?.authCode) q.set('authCode', ret.authCode);
    const qs = q.toString();
    return request<void>(`/remittances/${id}/lines/${lineId}/confirm-paid${qs ? `?${qs}` : ''}`, { method: 'POST' });
  },
  /** Manually marks a remittance line as returned with a reason. */
  markRemittanceLineReturned: (id: string, lineId: string, reason: string) =>
    request<void>(`/remittances/${id}/lines/${lineId}/mark-returned`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  /** Development-only: direct URL for downloading a simulated .RET file. */
  remittanceSimulateReturnUrl: (id: string) => `${BASE_URL}/remittances/${id}/simulate-return`,

  // ---------- Projects (Budgeting) ----------
  listProjects: (params: { companyId?: string; unitId?: string; status?: ProjectStatus; search?: string } = {}) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.status) q.set('status', params.status);
    if (params.search) q.set('search', params.search);
    q.set('pageSize', '200');
    return request<Paginated<Project>>(`/projects?${q}`);
  },
  getProject: (id: string) => request<Project>(`/projects/${id}`),
  getProjectMetrics: (id: string) => request<ProjectMetrics>(`/projects/${id}/metrics`),
  // Batch metrics for a project list in one call (keyed by project id) — replaces the per-project fan-out.
  getProjectMetricsList: (ids: string[]) => {
    const q = new URLSearchParams();
    ids.forEach((id) => q.append('ids', id));
    return request<{ metrics: Record<string, ProjectMetrics> }>(`/projects/metrics?${q}`);
  },
  createProject: (input: ProjectInput) =>
    request<Project>('/projects', { method: 'POST', body: JSON.stringify(input) }),
  updateProject: (id: string, input: ProjectInput) =>
    request<Project>(`/projects/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteProject: (id: string) => request<void>(`/projects/${id}`, { method: 'DELETE' }),

  // ---------- Budget control (Requisitions, server-side) ----------
  /** Panel + verdict for the existing requisition (budgeted × consumed × forecast per competência + overflows). */
  getRequisitionBudgetControl: (id: string) => request<BudgetControl>(`/requisitions/${id}/budget-control`),
  // Batch verdict for a whole queue in one call (keyed by requisition id) — replaces the per-id fan-out.
  getRequisitionBudgetControls: (ids: string[]) => {
    const q = new URLSearchParams();
    ids.forEach((id) => q.append('ids', id));
    return request<{ controls: Record<string, BudgetControl> }>(`/requisitions/budget-controls?${q}`);
  },
  /** Panel by dimension (creation form — no saved candidate). */
  getDimensionBudgetControl: (params: {
    costCenterId: string;
    unitId: string;
    competencyFrom: string;
    competencyTo?: string;
    excludeRequisitionId?: string;
  }) => {
    const q = new URLSearchParams();
    q.set('costCenterId', params.costCenterId);
    q.set('unitId', params.unitId);
    q.set('competencyFrom', params.competencyFrom);
    if (params.competencyTo) q.set('competencyTo', params.competencyTo);
    if (params.excludeRequisitionId) q.set('excludeRequisitionId', params.excludeRequisitionId);
    return request<BudgetControl>(`/requisitions/budget-control?${q}`);
  },
  /** Budget by natureza (cost center) for the company in a competência; unitId optional = consolidated. */
  getCostCenterBudgetSummary: (params: { companyId: string; competency: string; unitId?: string }) => {
    const q = new URLSearchParams({ companyId: params.companyId, competency: params.competency });
    if (params.unitId) q.set('unitId', params.unitId);
    return request<CostCenterBudgetLine[]>(`/requisitions/budget-summary?${q}`);
  },
  /** Total realized per cost center for the company (spend × budget on the cost-center list card). */
  getCostCenterRealized: (companyId: string) => {
    const q = new URLSearchParams({ companyId });
    return request<CostCenterRealized[]>(`/requisitions/cost-center-realized?${q}`);
  },
  /** Multi-month projection of the entire queue against the budget (AEL-876); unitId optional = whole company. */
  getQueueBudgetProjection: (params: { companyId: string; unitId?: string; from?: string; to?: string }) => {
    const q = new URLSearchParams({ companyId: params.companyId });
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    return request<QueueProjection>(`/requisitions/budget-projection?${q}`);
  },

  // ---------- Notifications (AEL-794) ----------
  listNotifications: (page = 1, pageSize = 20, unreadOnly?: boolean) => {
    const q = new URLSearchParams();
    q.set('page', String(page));
    q.set('pageSize', String(pageSize));
    if (unreadOnly) q.set('unreadOnly', 'true');
    return request<Paginated<Notification>>(`/notifications?${q}`);
  },
  unreadNotificationsCount: () => request<{ count: number }>('/notifications/unread-count'),
  markNotificationRead: (id: string) => request<void>(`/notifications/${id}/read`, { method: 'POST' }),
  markAllNotificationsRead: () => request<{ count: number }>('/notifications/read-all', { method: 'POST' }),

  // ---------- Realtime presence (AEL-realtime-updates) ----------
  /**
   * Quem está vendo `resourceId` agora, com o nome já resolvido. `[]` (nunca erro) sem acesso ao
   * recurso ou com o backplane fora. `resourceId` deve chegar já normalizado (Guid hifenizado, forma
   * `D`) — `usePresence` é quem normaliza, uma vez só (ver o comentário lá); aqui só se repassa.
   */
  getPresence: (scope: string, resourceId: string) => {
    const q = new URLSearchParams({ scope, resourceId });
    return request<PresenceUser[]>(`/realtime/presence?${q}`);
  },
  /**
   * `seq`: contador estritamente crescente por `(connectionId, scope, resourceId)`, começando em 1 —
   * ver `usePresence`. O corpo é JSON (`System.Text.Json`), que só aceita Guid na forma `D`
   * (hifenizada) — a forma `N` (32 hex sem hífen, como um id tipado chega prefixado) falha a
   * desserialização no backend. `resourceId` já deve chegar normalizado (ver `getPresence` acima).
   */
  enterPresence: (connectionId: string, scope: string, resourceId: string, seq: number) =>
    request<void>('/realtime/presence', {
      method: 'POST',
      body: JSON.stringify({ connectionId, scope, resourceId, seq } satisfies Schemas['PresenceRequest']),
    }),
  /** DELETE recebe os quatro campos como query params, não como body. `keepalive` dá à saída a
   *  chance de completar mesmo se o unmount coincidir com uma navegação em curso. */
  leavePresence: (connectionId: string, scope: string, resourceId: string, seq: number) => {
    const q = new URLSearchParams({ connectionId, scope, resourceId, seq: String(seq) });
    return request<void>(`/realtime/presence?${q}`, { method: 'DELETE', keepalive: true });
  },

  // ---------- Requisitions (Budgeting) ----------
  listRequisitions: (
    params: {
      companyId?: string;
      unitId?: string;
      costCenterId?: string;
      vendorId?: string;
      status?: RequisitionStatus;
      statuses?: RequisitionStatus[];
      type?: RequisitionType;
      competency?: string;
      competencyFrom?: string;
      competencyTo?: string;
      search?: string;
      minValue?: number;
      maxValue?: number;
      sort?: string;
      page?: number;
      pageSize?: number;
      mine?: boolean;
      awaitingNf?: boolean;
    } = {},
  ) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.vendorId) q.set('vendorId', params.vendorId);
    // Multi-status via repeated `status` keys (array binding); falls back to a single status.
    if (params.statuses?.length) params.statuses.forEach((s) => q.append('status', s));
    else if (params.status) q.set('status', params.status);
    if (params.type) q.set('type', params.type);
    if (params.competency) q.set('competency', params.competency);
    if (params.competencyFrom) q.set('competencyFrom', params.competencyFrom);
    if (params.competencyTo) q.set('competencyTo', params.competencyTo);
    if (params.search) q.set('search', params.search);
    if (params.minValue != null) q.set('minValue', String(params.minValue));
    if (params.maxValue != null) q.set('maxValue', String(params.maxValue));
    if (params.sort) q.set('sort', params.sort);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    if (params.mine) q.set('mine', 'true');
    if (params.awaitingNf) q.set('awaitingNf', 'true');
    return request<Paginated<Requisition>>(`/requisitions?${q}`);
  },
  // Counts per workflow status in one grouped query — replaces the former per-status fan-out (one
  // PageSize=1 list call per status just to read `total`).
  getRequisitionStageCounts: (params: { companyId?: string; mine?: boolean } = {}) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.mine) q.set('mine', 'true');
    return request<RequisitionStageCounts>(`/requisitions/stage-counts?${q}`);
  },
  listMyRequisitionDecisions: (params: { companyId?: string; page?: number; pageSize?: number } = {}) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<RequisitionDecision>>(`/requisitions/my-decisions?${q}`);
  },
  listMyRequisitionTriageDecisions: (params: { companyId?: string; page?: number; pageSize?: number } = {}) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<RequisitionDecision>>(`/requisitions/my-triage-decisions?${q}`);
  },
  getRequisition: (id: string) => request<Requisition>(`/requisitions/${id}`),
  createRequisition: (input: RequisitionInput) =>
    request<Requisition>('/requisitions', { method: 'POST', body: JSON.stringify(input) }),
  updateRequisition: (id: string, input: Omit<RequisitionInput, 'unitId' | 'costCenterId'>) =>
    request<Requisition>(`/requisitions/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteRequisition: (id: string) => request<void>(`/requisitions/${id}`, { method: 'DELETE' }),
  // Transitions (state machine). All return the fresh Requisition with `transitions` populated.
  submitRequisition: (id: string) => request<Requisition>(`/requisitions/${id}/submit`, { method: 'POST' }),
  // Triage approved → FinancialComplement (perm requisitions:triage). Replaces the old send-to-approval.
  approveTriage: (id: string) => request<Requisition>(`/requisitions/${id}/approve-triage`, { method: 'POST' }),
  // Requester submits the financial complement → Approval, or returns to Triage if the value/vendor changed.
  submitFinancialComplement: (id: string, body: SubmitFinancialComplementInput) =>
    request<Requisition>(`/requisitions/${id}/submit-financial-complement`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  approveRequisition: (id: string) =>
    request<Requisition>(`/requisitions/${id}/approve`, {
      method: 'POST',
    }),
  returnRequisitionForComplement: (id: string, reason?: string) =>
    request<Requisition>(`/requisitions/${id}/return-for-complement`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  // Approver returns a front-loaded requisition to revise the financial plan (Approval → FinancialComplement).
  returnRequisitionToFinancialComplement: (id: string, reason?: string) =>
    request<Requisition>(`/requisitions/${id}/return-to-financial-complement`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),
  resubmitRequisition: (id: string) => request<Requisition>(`/requisitions/${id}/resubmit`, { method: 'POST' }),
  cancelRequisition: (id: string, reason?: string) =>
    request<Requisition>(`/requisitions/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  /** Launches the month's invoice on a recurring requisition (Conta Fixa), AEL-877 — generates a
   * Payable for the competency without re-approval. Multipart: an optional NF file (Conta Fixa) rides along and
   * lands on the cycle's Payable, never on the general requisition attachments. */
  addRecurrenceCycle: (id: string, body: AddRecurrenceCycleInput, file?: File | null) => {
    const form = new FormData();
    form.append('competency', body.competency);
    form.append('dueDate', body.dueDate);
    form.append('amount', String(body.amount));
    form.append('paymentMethod', body.paymentMethod);
    if (body.boletoDigitableLine) form.append('boletoDigitableLine', body.boletoDigitableLine);
    if (body.pixKeyOverride) form.append('pixKeyOverride', body.pixKeyOverride);
    if (body.pixPayloadEmv) form.append('pixPayloadEmv', body.pixPayloadEmv);
    if (file) form.append('file', file);
    return request<Requisition>(`/requisitions/${id}/cycles`, { method: 'POST', body: form });
  },
  /** Terminates a recurring requisition at a competency so it can finalize (AEL-877). */
  terminateRecurrence: (id: string, endCompetency: string) =>
    request<Requisition>(`/requisitions/${id}/terminate-recurrence`, {
      method: 'POST',
      body: JSON.stringify({ endCompetency }),
    }),
  /** Uploads a requisition attachment via multipart/form-data (browser sets the boundary). Pass kind
   * 'FiscalDocument' when the file is the Nota Fiscal so it clears the "NF pendente" pendency. */
  uploadRequisitionAttachment: async (id: string, file: File, kind?: RequisitionAttachmentKind) => {
    const form = new FormData();
    form.append('file', file);
    const headers = new Headers();
    if (auth.token) headers.set('Authorization', `Bearer ${auth.token}`);
    const url = `${BASE_URL}/requisitions/${id}/attachments${kind ? `?kind=${kind}` : ''}`;
    const res = await fetch(url, { method: 'POST', headers, body: form });
    if (!res.ok) throw new Error((await res.text()) || `${res.status} ${res.statusText}`);
    return res.json() as Promise<RequisitionAttachment>;
  },
  /**
   * Registers a blob already uploaded via the boleto presigned URL as a requisition attachment, by key — no
   * second upload (AEL-938). Mirrors `uploadPayableFiscalDocument`; the boleto reader uses this so the read file
   * is kept as an attachment without sending the bytes twice.
   */
  registerRequisitionAttachmentByKey: (id: string, body: BoletoBlobRef & { kind?: RequisitionAttachmentKind }) =>
    request<RequisitionAttachment>(`/requisitions/${id}/attachments/from-key`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  deleteRequisitionAttachment: (id: string, attachmentId: string) =>
    request<void>(`/requisitions/${id}/attachments/${attachmentId}`, { method: 'DELETE' }),
  /** Reclassifies an existing attachment's type (requester/controllership fixing a mis-typed file). */
  updateRequisitionAttachmentKind: (id: string, attachmentId: string, kind: RequisitionAttachmentKind) =>
    request<RequisitionAttachment>(`/requisitions/${id}/attachments/${attachmentId}`, {
      method: 'PATCH',
      body: JSON.stringify({ kind }),
    }),

  // ---------- Budget adjustments (Budgeting) ----------
  listBudgetAdjustments: (
    params: {
      companyId?: string;
      unitId?: string;
      costCenterId?: string;
      status?: BudgetAdjustmentStatus;
      type?: BudgetAdjustmentType;
      competency?: string;
      page?: number;
      pageSize?: number;
    } = {},
  ) => {
    const q = new URLSearchParams();
    if (params.companyId) q.set('companyId', params.companyId);
    if (params.unitId) q.set('unitId', params.unitId);
    if (params.costCenterId) q.set('costCenterId', params.costCenterId);
    if (params.status) q.set('status', params.status);
    if (params.type) q.set('type', params.type);
    if (params.competency) q.set('competency', params.competency);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<BudgetAdjustment>>(`/budget-adjustments?${q}`);
  },
  getBudgetAdjustment: (id: string) => request<BudgetAdjustment>(`/budget-adjustments/${id}`),
  createBudgetAdjustment: (input: BudgetAdjustmentInput) =>
    request<BudgetAdjustment>('/budget-adjustments', { method: 'POST', body: JSON.stringify(input) }),
  updateBudgetAdjustment: (id: string, input: Omit<BudgetAdjustmentInput, 'unitId' | 'costCenterId'>) =>
    request<BudgetAdjustment>(`/budget-adjustments/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  deleteBudgetAdjustment: (id: string) => request<void>(`/budget-adjustments/${id}`, { method: 'DELETE' }),
  // Transitions (state machine). All return the fresh BudgetAdjustment with `transitions` populated.
  sendBudgetAdjustmentToApproval: (id: string) =>
    request<BudgetAdjustment>(`/budget-adjustments/${id}/send-to-approval`, { method: 'POST' }),
  approveBudgetAdjustment: (id: string) =>
    request<BudgetAdjustment>(`/budget-adjustments/${id}/approve`, { method: 'POST' }),
  cancelBudgetAdjustment: (id: string, reason?: string) =>
    request<BudgetAdjustment>(`/budget-adjustments/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason }) }),
  reverseBudgetAdjustment: (id: string, reason?: string) =>
    request<BudgetAdjustment>(`/budget-adjustments/${id}/reverse`, {
      method: 'POST',
      body: JSON.stringify({ reason }),
    }),

  // ---------- Audit trail (admin) ----------
  /** Global write trail, newest first. Filters combine with AND; `from`/`to` are instants (ISO). */
  listAuditLogs: (
    params: {
      entityType?: string;
      entityId?: string;
      actorUserId?: string;
      from?: string;
      to?: string;
      page?: number;
      pageSize?: number;
    } = {},
  ) => {
    const q = new URLSearchParams();
    if (params.entityType) q.set('entityType', params.entityType);
    if (params.entityId) q.set('entityId', params.entityId);
    if (params.actorUserId) q.set('actorUserId', params.actorUserId);
    if (params.from) q.set('from', params.from);
    if (params.to) q.set('to', params.to);
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    return request<Paginated<AuditLog>>(`/audit-logs?${q}`);
  },
};
