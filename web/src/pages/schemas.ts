import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import type { Resolver, FieldValues } from 'react-hook-form';
import i18n from '../i18n';
import { isValidCpf, isValidCnpj, onlyDigits, normalizeCnpj } from '../format';
import { parseMoneyInput } from '../lib';
import { monthsBetween } from './requisitions/competency';
import { isBoletoLengthValid } from './requisitions/boleto';

/**
 * Zod schemas for the app's forms. Validation messages go through i18n (pt-BR).
 * Messages are resolved lazily via getters so the active language is respected.
 *
 * Field rules mirror the backend validators:
 * - registry name required;
 * - document = CNPJ when personType is Company, otherwise CPF;
 * - employee requires CPF + unit + role + hire date;
 * - email optional but must be valid when present;
 * - creditLimit / annualBudget / autoApprovalLimit >= 0;
 * - transaction amount > 0.
 */

const t = (key: string) => i18n.t(key);

/** An optional email: blank passes, otherwise must look like an email. */
const optionalEmail = z
  .string()
  .trim()
  .refine((v) => v === '' || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v), { message: 'validation.emailInvalid' });

/** Address sub-schema (all fields optional; an entirely-blank address is allowed). */
export const addressSchema = z.object({
  cep: z.string(),
  street: z.string(),
  number: z.string(),
  complement: z.string(),
  neighborhood: z.string(),
  city: z.string(),
  state: z.string(),
});
export type AddressValues = z.infer<typeof addressSchema>;

/** Builds a vendor/customer-shaped person schema; `withDocument` validates CPF/CNPJ by personType. */
function personFields() {
  return {
    personType: z.enum(['Individual', 'Company']),
    name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
    tradeName: z.string(),
    document: z.string(),
    email: optionalEmail,
    phone: z.string(),
    address: addressSchema,
  };
}

/** Document refinement: required, and valid for the selected person type. */
function refineDocument<T extends { personType: 'Individual' | 'Company'; document: string }>(schema: z.ZodType<T>) {
  return schema.superRefine((val, ctx) => {
    const isCompany = val.personType === 'Company';
    // CNPJ may be alphanumeric (RFB, Jul/2026); keep its letters. CPF stays digits-only.
    const cleaned = isCompany ? normalizeCnpj(val.document) : onlyDigits(val.document);
    if (cleaned.length === 0) {
      ctx.addIssue({ code: 'custom', message: 'validation.documentRequired', path: ['document'] });
      return;
    }
    const ok = isCompany ? isValidCnpj(cleaned) : isValidCpf(cleaned);
    if (!ok) {
      ctx.addIssue({
        code: 'custom',
        message: val.personType === 'Company' ? 'validation.cnpjInvalid' : 'validation.cpfInvalid',
        path: ['document'],
      });
    }
  });
}

/** Vendor bank info sub-schema — all fields optional strings (no required bank data). */
export const vendorBankInfoSchema = z.object({
  bankCode: z.string(),
  branch: z.string(),
  account: z.string(),
  accountDigit: z.string(),
  pixKey: z.string(),
});
export type VendorBankInfoValues = z.infer<typeof vendorBankInfoSchema>;

export const TAX_REGIMES = ['Unknown', 'Mei', 'SimplesNacional', 'LucroPresumido', 'LucroReal'] as const;
export type TaxRegimeValue = (typeof TAX_REGIMES)[number];

export const SIMPLES_ANEXOS = ['I', 'II', 'III', 'IV', 'V'] as const;
export type SimplesAnexoValue = (typeof SIMPLES_ANEXOS)[number];

export const PAYMENT_SCHEDULE_MODES = ['OnDueDate', 'FixedDayOfMonth', 'FirstDayOfMonth'] as const;
export type PaymentScheduleMode = (typeof PAYMENT_SCHEDULE_MODES)[number];

// Payment methods — single source of truth in the frontend, mirrors the backend PaymentMethod enum.
export const PAYMENT_METHODS = ['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other'] as const;
export type PaymentMethodValue = (typeof PAYMENT_METHODS)[number];

export const vendorSchema = refineDocument(
  z
    .object({
      ...personFields(),
      paymentTerms: z.string(),
      bankInfo: vendorBankInfoSchema,
      taxRegime: z.enum(TAX_REGIMES),
      // INSS (indicativos do cadastro): CPRB previsto (Sim/Não/Desconhecido) + anexo do Simples.
      cprbForeseen: z.boolean().nullable().default(null),
      simplesAnexo: z.enum(SIMPLES_ANEXOS).nullable().default(null),
      paymentScheduleMode: z.enum(PAYMENT_SCHEDULE_MODES).default('OnDueDate'),
      paymentScheduleDay: z.number().nullable().default(null),
      // Accepted payment methods.
      acceptedPaymentMethods: z.array(z.enum(PAYMENT_METHODS)).default([]),
      // Per-tax withholding rate overrides (map taxType → rate as % string; '' = no override).
      withholdingRates: z.record(z.string(), z.string()).default({}),
      // Default expense ledger accounts (LedgerAccount ids). Empty = none.
      associatedLedgerAccountIds: z.array(z.string()).default([]),
      // Primary CNAE from the CNPJ lookup (code + description); optional.
      primaryCnaeCode: z.string().default(''),
      primaryCnaeDescription: z.string().default(''),
    })
    .superRefine((val, ctx) => {
      if (val.paymentScheduleMode === 'FixedDayOfMonth') {
        const day = val.paymentScheduleDay;
        if (day === null || day === undefined) {
          ctx.addIssue({
            code: 'custom',
            message: 'validation.paymentScheduleDayRequired',
            path: ['paymentScheduleDay'],
          });
        } else if (!Number.isInteger(day) || day < 1 || day > 28) {
          ctx.addIssue({
            code: 'custom',
            message: 'validation.paymentScheduleDayRange',
            path: ['paymentScheduleDay'],
          });
        }
      }
    }),
);
export type VendorValues = z.infer<typeof vendorSchema>;

/** Money amount ≥ 0 (pt-BR: aceita vírgula decimal e separador de milhar). Vazio passa. */
const nonNegativeNumberString = z.string().refine(
  (v) => {
    if (v.trim() === '') return true;
    const n = parseMoneyInput(v);
    return n !== null && n >= 0;
  },
  { message: 'validation.nonNegative' },
);

/** Money amount > 0 (pt-BR: aceita vírgula decimal e separador de milhar). */
const positiveMoneyString = z.string().refine(
  (v) => {
    const n = parseMoneyInput(v);
    return n !== null && n > 0;
  },
  { message: 'validation.amountPositive' },
);

export const customerSchema = refineDocument(
  z.object({
    ...personFields(),
    creditLimit: nonNegativeNumberString,
  }),
);
export type CustomerValues = z.infer<typeof customerSchema>;

export const employeeSchema = z.object({
  name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  document: z
    .string()
    .refine((v) => onlyDigits(v).length > 0, { message: 'validation.documentRequired' })
    .refine((v) => onlyDigits(v).length === 0 || isValidCpf(v), { message: 'validation.cpfInvalid' }),
  email: optionalEmail,
  phone: z.string(),
  role: z.string().trim().min(1, { message: 'validation.roleRequired' }),
  unitId: z.string().min(1, { message: 'validation.unitRequired' }),
  costCenterId: z.string(),
  hireDate: z.string().min(1, { message: 'validation.hireDateRequired' }),
  address: addressSchema,
});
export type EmployeeValues = z.infer<typeof employeeSchema>;

export const transactionSchema = z
  .object({
    type: z.enum(['Payable', 'Receivable']),
    unitId: z.string().min(1, { message: 'validation.unitRequired' }),
    costCenterId: z.string().min(1, { message: 'validation.costCenterRequired' }),
    description: z.string().trim().min(1, { message: 'validation.descriptionRequired' }),
    vendorId: z.string().nullable(),
    customerId: z.string().nullable(),
    amount: positiveMoneyString,
    dueDate: z.string().min(1, { message: 'validation.dueDateRequired' }),
  })
  .superRefine((val, ctx) => {
    // A payable must link to a vendor; a receivable to a customer.
    if (val.type === 'Payable' && !val.vendorId) {
      ctx.addIssue({ code: 'custom', message: 'validation.vendorRequired', path: ['vendorId'] });
    }
    if (val.type === 'Receivable' && !val.customerId) {
      ctx.addIssue({ code: 'custom', message: 'validation.customerRequired', path: ['customerId'] });
    }
  });
export type TransactionValues = z.infer<typeof transactionSchema>;

export const payableSchema = z
  .object({
    vendorId: z.string().min(1, { message: 'validation.vendorRequired' }),
    costCenterId: z.string().min(1, { message: 'validation.costCenterRequired' }),
    categoryId: z.string().nullable(),
    accountingAccountId: z.string().nullable(),
    description: z
      .string()
      .trim()
      .min(1, { message: 'validation.descriptionRequired' })
      .max(300, { message: 'validation.descriptionMax' }),
    documentNumber: z.string(),
    issueDate: z.string(),
    // Linha digitável do boleto (à vista): anexada a installments[0] no submit; alimenta a remessa CNAB (Seg J/J-52).
    boletoDigitableLine: z.string(),
    mode: z.enum(['single', 'installments']),
    totalAmount: positiveMoneyString,
    installmentCount: z.string(),
    firstDueDate: z.string().min(1, { message: 'validation.dueDateRequired' }),
    intervalMonths: z.string(),
  })
  .superRefine((val, ctx) => {
    if (val.mode === 'installments') {
      const n = Number(val.installmentCount);
      if (!Number.isInteger(n) || n < 2) {
        ctx.addIssue({ code: 'custom', message: 'validation.installmentCountInvalid', path: ['installmentCount'] });
      }
    }
    // Boleto (AEL-932): a linha digitável, quando informada, precisa ter 47/48 dígitos. Vazia é permitida
    // (preenche-se depois — a fila mostra como pendência manual). Espelha a regra do backend na Payable.
    if (val.boletoDigitableLine.trim() && !isBoletoLengthValid(val.boletoDigitableLine)) {
      ctx.addIssue({ code: 'custom', message: 'validation.boletoDigitableLineInvalid', path: ['boletoDigitableLine'] });
    }
  });
export type PayableValues = z.infer<typeof payableSchema>;

/** Adds `months` to a "YYYY-MM-DD" date using integer arithmetic (no Date parsing), clamping the day to the
 * target month's last day so a month-end doesn't overflow (Jan 31 + 1 month → Feb 28, not Mar 3) and avoiding
 * the local-parse/UTC-format off-by-one that `new Date(s).toISOString()` produces east of UTC. */
function addMonthsIso(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const targetMonth0 = m - 1 + months;
  const year = y + Math.floor(targetMonth0 / 12);
  const month0 = ((targetMonth0 % 12) + 12) % 12;
  const lastDay = new Date(year, month0 + 1, 0).getDate();
  const day = Math.min(d, lastDay);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${year}-${pad(month0 + 1)}-${pad(day)}`;
}

/** Preview the generated installments for the form (mirrors backend GenerateInstallments). */
export function previewInstallments(total: number, count: number, firstDue: string, intervalMonths: number) {
  const each = Math.round((total / count) * 100) / 100;
  const rows: { sequence: number; dueDate: string; amount: number }[] = [];
  let acc = 0;
  for (let i = 0; i < count; i++) {
    const amount = i === count - 1 ? Math.round((total - acc) * 100) / 100 : each;
    acc += amount;
    rows.push({ sequence: i + 1, dueDate: addMonthsIso(firstDue, intervalMonths * i), amount });
  }
  return rows;
}

export const bankAccountSchema = z.object({
  bankCode: z.string().trim().min(1, { message: 'validation.bankCodeRequired' }),
  bankName: z.string().trim().min(1, { message: 'validation.bankNameRequired' }),
  agency: z.string().trim().min(1, { message: 'validation.agencyRequired' }),
  accountNumber: z.string().trim().min(1, { message: 'validation.accountNumberRequired' }),
  accountDigit: z.string(),
  accountType: z.enum(['Checking', 'Savings']),
  alias: z.string().trim().min(1, { message: 'validation.aliasRequired' }),
  openingBalance: nonNegativeNumberString,
  isDefaultForPayments: z.boolean(),
  cnabAgreementCode: z.string(),
  ofxAccountId: z.string(),
  // CNAB remittance-header profile (optional): agency check digit (DV) + paying-company address.
  agencyDigit: z.string(),
  addressStreet: z.string(),
  addressNumber: z.string(),
  addressComplement: z.string(),
  addressCity: z.string(),
  addressCep: z.string(),
  addressState: z.string(),
  // CNAB payer identity override: when a CNPJ is informed it must be valid (mod-11) — mirrors the backend.
  payerCnpj: z.string().refine((v) => !v.trim() || isValidCnpj(onlyDigits(v)), { message: 'validation.cnpjInvalid' }),
  payerLegalName: z.string(),
  // Optional at schema level so editing legacy accounts (null) stays valid; the create form enforces
  // selection (Save disabled) and the backend rejects a null on create.
  ledgerAccountId: z.string().nullable(),
});
export type BankAccountValues = z.infer<typeof bankAccountSchema>;

export const paymentSchema = z.object({
  paymentDate: z.string().min(1, { message: 'validation.paymentDateRequired' }),
  amount: positiveMoneyString,
  bankAccountId: z.string().min(1, { message: 'validation.bankAccountRequired' }),
  method: z.enum(PAYMENT_METHODS),
  interest: nonNegativeNumberString,
  fine: nonNegativeNumberString,
  discount: nonNegativeNumberString,
  notes: z.string(),
});
export type PaymentValues = z.infer<typeof paymentSchema>;

export const receivableSchema = z
  .object({
    payerId: z.string().min(1, { message: 'validation.customerRequired' }),
    costCenterId: z.string().min(1, { message: 'validation.costCenterRequired' }),
    description: z.string().trim().min(1, { message: 'validation.descriptionRequired' }),
    documentNumber: z.string(),
    issueDate: z.string(),
    mode: z.enum(['single', 'installments']),
    totalAmount: positiveMoneyString,
    installmentCount: z.string(),
    firstDueDate: z.string().min(1, { message: 'validation.dueDateRequired' }),
    intervalMonths: z.string(),
  })
  .superRefine((val, ctx) => {
    if (val.mode === 'installments') {
      const n = Number(val.installmentCount);
      if (!Number.isInteger(n) || n < 2) {
        ctx.addIssue({ code: 'custom', message: 'validation.installmentCountInvalid', path: ['installmentCount'] });
      }
    }
  });
export type ReceivableValues = z.infer<typeof receivableSchema>;

export const receiptSchema = z.object({
  receiptDate: z.string().min(1, { message: 'validation.receiptDateRequired' }),
  amount: positiveMoneyString,
  bankAccountId: z.string().min(1, { message: 'validation.bankAccountRequired' }),
  method: z.enum(PAYMENT_METHODS),
  interest: nonNegativeNumberString,
  fine: nonNegativeNumberString,
  discount: nonNegativeNumberString,
  notes: z.string(),
});
export type ReceiptValues = z.infer<typeof receiptSchema>;

/* ---------- Accounting: journal entry (lançamento) ---------- */
export const journalLineSchema = z.object({
  ledgerAccountId: z.string().min(1, { message: 'validation.ledgerAccountRequired' }),
  side: z.enum(['Debit', 'Credit']),
  amount: positiveMoneyString,
  costCenterId: z.string(),
  description: z.string(),
});
export type JournalLineValues = z.infer<typeof journalLineSchema>;

export const journalEntrySchema = z
  .object({
    competenceDate: z.string().min(1, { message: 'validation.competenceDateRequired' }),
    description: z.string().trim().min(1, { message: 'validation.descriptionRequired' }),
    sourceReference: z.string(),
    lines: z.array(journalLineSchema).min(2, { message: 'validation.atLeastTwoLines' }),
  })
  .superRefine((val, ctx) => {
    // Live double-entry balance check: Σdebit must equal Σcredit (to the cent).
    const debit = val.lines
      .filter((l) => l.side === 'Debit')
      .reduce((acc, l) => acc + (parseMoneyInput(l.amount) ?? 0), 0);
    const credit = val.lines
      .filter((l) => l.side === 'Credit')
      .reduce((acc, l) => acc + (parseMoneyInput(l.amount) ?? 0), 0);
    if (Math.round((debit - credit) * 100) !== 0) {
      ctx.addIssue({ code: 'custom', message: 'validation.unbalancedEntry', path: ['lines'] });
    }
  });
export type JournalEntryValues = z.infer<typeof journalEntrySchema>;

export const reconciledBalanceSchema = z.object({
  balance: z.string().refine((v) => parseMoneyInput(v) !== null, {
    message: 'cashFlow.validation.balanceRequired',
  }),
  asOf: z.string().min(1, { message: 'cashFlow.validation.asOfRequired' }),
});
export type ReconciledBalanceValues = z.infer<typeof reconciledBalanceSchema>;

export const companySchema = z.object({
  legalName: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  cnpj: z.string().trim().min(1, { message: 'validation.cnpjRequired' }),
});
export type CompanyValues = z.infer<typeof companySchema>;

export const unitSchema = z.object({
  name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  acronym: z.string().trim().min(1, { message: 'validation.acronymRequired' }),
  // Establishment CNPJ is optional; when informed it must be a valid (mod-11) CNPJ.
  cnpj: z
    .string()
    .trim()
    .optional()
    // Unit CNPJ stays numeric for now — Unit save/store is digits-only (alphanumeric is a follow-up, ~AEL-636).
    .refine((v) => !v || isValidCnpj(onlyDigits(v)), { message: 'validation.cnpjInvalid' }),
  isMatriz: z.boolean().optional(),
});
export type UnitValues = z.infer<typeof unitSchema>;

export const costCenterSchema = z.object({
  code: z.string().trim().min(1, { message: 'validation.codeRequired' }),
  name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  autoApprovalLimit: nonNegativeNumberString,
  unitIds: z.array(z.string()),
});
export type CostCenterValues = z.infer<typeof costCenterSchema>;

// ---------- Project (teto de gasto acumulado — AEL-792) ----------
export const projectSchema = z.object({
  name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  unitId: z.string().trim().min(1, { message: 'validation.unitRequired' }),
  description: z.string().trim().optional(),
  budgetLimit: nonNegativeNumberString,
  initialConsumedValue: nonNegativeNumberString,
});
export type ProjectValues = z.infer<typeof projectSchema>;

// ---------- Orçamento do centro de custo (teto mensal por Unidade × Competência — AEL-830) ----------
/** Competência no formato "YYYY-MM" (espelha `Common/Competency.cs`). */
const competencyString = z.string().regex(/^\d{4}-(0[1-9]|1[0-2])$/, { message: 'validation.competencyRequired' });

export const unitBudgetSchema = z
  .object({
    unitId: z.string().trim().min(1, { message: 'validation.unitRequired' }),
    startCompetency: competencyString,
    // '' no submit → backend aplica o período default (12 meses a partir do início).
    endCompetency: competencyString.or(z.literal('')).default(''),
    monthlyLimit: nonNegativeNumberString,
    active: z.boolean().default(true),
  })
  .superRefine((v, ctx) => {
    if (v.endCompetency && v.endCompetency < v.startCompetency) {
      ctx.addIssue({ code: 'custom', message: 'validation.competencyEndBeforeStart', path: ['endCompetency'] });
    }
    if (v.endCompetency && monthsBetween(v.startCompetency, v.endCompetency) > 36) {
      ctx.addIssue({ code: 'custom', message: 'validation.competencyHorizonMax', path: ['endCompetency'] });
    }
  });
export type UnitBudgetValues = z.infer<typeof unitBudgetSchema>;

// ---------- Requisição de compra (AEL-788 / esteira AEL-789) ----------
export const REQUISITION_TYPES = ['Purchase', 'ServiceProvision', 'FixedExpense', 'Project'] as const;
export type RequisitionTypeValue = (typeof REQUISITION_TYPES)[number];
export const ESTIMATE_TYPES = ['Monthly', 'Full'] as const;
export type EstimateTypeValue = (typeof ESTIMATE_TYPES)[number];
export const PAYMENT_FREQUENCIES = ['Biweekly', 'Monthly', 'Semiannual', 'Annual'] as const;
export type PaymentFrequencyValue = (typeof PAYMENT_FREQUENCIES)[number];

export const requisitionSchema = z
  .object({
    unitId: z.string().trim().min(1, { message: 'validation.unitRequired' }),
    costCenterId: z.string().trim().min(1, { message: 'validation.costCenterRequired' }),
    vendorId: z.string().trim().min(1, { message: 'validation.vendorRequired' }),
    type: z.enum(REQUISITION_TYPES),
    description: z
      .string()
      .trim()
      .min(1, { message: 'validation.descriptionRequired' })
      .max(300, { message: 'validation.descriptionMax' }),
    observation: z.string().trim().optional(),
    estimateValue: positiveMoneyString,
    estimateType: z.enum(ESTIMATE_TYPES),
    startCompetency: competencyString,
    // '' → null no submit (competência final aberta, p/ Conta Fixa).
    endCompetency: competencyString.or(z.literal('')).default(''),
    // Primeiro vencimento (AEL-793) — obrigatório; "YYYY-MM-DD" do <input type="date">.
    firstDueDate: z.string().trim().min(1, { message: 'validation.firstDueDateRequired' }),
    // '' → null no submit.
    paymentFrequency: z.enum(PAYMENT_FREQUENCIES).or(z.literal('')).default(''),
    paymentInstallmentsCount: z.string().default(''),
    projectId: z.string().nullable().default(null),
  })
  .superRefine((v, ctx) => {
    if (v.endCompetency && v.endCompetency < v.startCompetency) {
      ctx.addIssue({ code: 'custom', message: 'validation.competencyEndBeforeStart', path: ['endCompetency'] });
    }
    if (v.paymentInstallmentsCount) {
      const n = Number(v.paymentInstallmentsCount);
      if (!Number.isInteger(n) || n < 1) {
        ctx.addIssue({
          code: 'custom',
          message: 'validation.installmentsMin1',
          path: ['paymentInstallmentsCount'],
        });
      }
    }
    // A periodicidade não é mais coletada no formulário do Solicitante — o cronograma do Payable gerado
    // usa a cadência mensal (padrão do domínio quando PaymentFrequency é nulo). O nº de parcelas basta.
    // Uma requisição de Projeto precisa estar vinculada a um projeto (consome seu teto acumulado — AEL-792).
    // O backend valida o projeto quando informado, mas não amarra tipo↔projeto; travamos aqui no cliente.
    if (v.type === 'Project' && !v.projectId) {
      ctx.addIssue({ code: 'custom', message: 'validation.projectRequired', path: ['projectId'] });
    }
  });
export type RequisitionValues = z.infer<typeof requisitionSchema>;

// ---------- Budget adjustment (ceiling change — AEL-791) ----------
export const ADEQUACAO_TYPES = ['Supplementation', 'Reduction', 'Transfer'] as const;
export type BudgetAdjustmentTypeValue = (typeof ADEQUACAO_TYPES)[number];

export const budgetAdjustmentSchema = z
  .object({
    unitId: z.string().trim().min(1, { message: 'validation.unitRequired' }),
    costCenterId: z.string().trim().min(1, { message: 'validation.costCenterRequired' }),
    type: z.enum(ADEQUACAO_TYPES),
    competency: competencyString,
    // '' → null on submit; required only for Transfer (validated in superRefine).
    sourceCompetency: competencyString.or(z.literal('')).default(''),
    value: positiveMoneyString,
    justification: z.string().trim().min(1, { message: 'validation.justificationRequired' }),
    counterparts: z.string().trim().optional(),
  })
  .superRefine((v, ctx) => {
    if (v.type === 'Transfer') {
      if (!v.sourceCompetency) {
        ctx.addIssue({ code: 'custom', message: 'validation.sourceCompetencyRequired', path: ['sourceCompetency'] });
      } else if (v.sourceCompetency === v.competency) {
        ctx.addIssue({ code: 'custom', message: 'validation.sourceEqualsDestination', path: ['sourceCompetency'] });
      }
    } else if (v.sourceCompetency) {
      ctx.addIssue({ code: 'custom', message: 'validation.sourceNotAllowed', path: ['sourceCompetency'] });
    }
    // Counterparts (deliverables) required except for a Reduction.
    if (v.type !== 'Reduction' && !v.counterparts?.trim()) {
      ctx.addIssue({ code: 'custom', message: 'validation.counterpartsRequired', path: ['counterparts'] });
    }
  });
export type BudgetAdjustmentValues = z.infer<typeof budgetAdjustmentSchema>;

// ---------- Withholding rule (Regra de Retenção) ----------
export const WITHHOLDING_TAX_TYPES = ['Irrf', 'Iss', 'Inss', 'Pis', 'Cofins', 'Csll', 'Other'] as const;
export type WithholdingTaxTypeValue = (typeof WITHHOLDING_TAX_TYPES)[number];
export const RATE_SOURCES = ['Fixed', 'FromNota'] as const;
export type RateSourceValue = (typeof RATE_SOURCES)[number];
export const BASE_RULES = ['Full', 'Reduced'] as const;
export type BaseRuleValue = (typeof BASE_RULES)[number];
export const NON_RETENTION_REASONS = ['None', 'RegimeExemption', 'NoMunicipalSubstitution', 'BelowMinimum'] as const;
export type NonRetentionReasonValue = (typeof NON_RETENTION_REASONS)[number];

export const withholdingRuleSchema = z
  .object({
    taxType: z.enum(WITHHOLDING_TAX_TYPES),
    retains: z.boolean().default(true),
    reason: z.enum(NON_RETENTION_REASONS).default('None'),
    rateSource: z.enum(RATE_SOURCES).default('Fixed'),
    rate: z.string().default(''), // percentual no form (ex.: "11"); convertido para fração no submit
    baseRule: z.enum(BASE_RULES).default('Full'),
    minimumAmount: nonNegativeNumberString.default(''),
    lc116Subitem: z.string().default(''),
    vendorRegime: z.enum(TAX_REGIMES).nullable().default(null),
    simplesAnexo: z.enum(SIMPLES_ANEXOS).nullable().default(null),
    cprb: z.boolean().nullable().default(null),
    municipalityIbge: z.string().default(''),
    companyId: z.string().default(''), // '' = todas as empresas (grupo-wide)
  })
  .superRefine((v, ctx) => {
    // Espelha o guard do backend: regra que retém com alíquota fixa exige alíquota positiva.
    if (v.retains && v.rateSource === 'Fixed' && !(Number(v.rate) > 0)) {
      ctx.addIssue({ code: 'custom', message: 'validation.rateRequiredForFixed', path: ['rate'] });
    }
  });
export type WithholdingRuleValues = z.infer<typeof withholdingRuleSchema>;

/** Resolves a zod error message (an i18n key) to localized text. */
export function fieldError(message?: string): string | undefined {
  return message ? t(message) : undefined;
}

/**
 * Typed zodResolver wrapper. zod v4's refined schemas have an `unknown` input type,
 * which breaks `zodResolver` inference for `useForm<T>`; this pins the form value type.
 */
export function resolver<T extends FieldValues>(schema: z.ZodType<T>): Resolver<T> {
  return zodResolver(schema as never) as Resolver<T>;
}
