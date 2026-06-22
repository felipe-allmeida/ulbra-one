import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import type { Resolver, FieldValues } from 'react-hook-form';
import i18n from '../i18n';
import { isValidCpf, isValidCnpj, onlyDigits } from '../format';

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
    const digits = onlyDigits(val.document);
    if (digits.length === 0) {
      ctx.addIssue({ code: 'custom', message: 'validation.documentRequired', path: ['document'] });
      return;
    }
    const ok = val.personType === 'Company' ? isValidCnpj(digits) : isValidCpf(digits);
    if (!ok) {
      ctx.addIssue({
        code: 'custom',
        message: val.personType === 'Company' ? 'validation.cnpjInvalid' : 'validation.cpfInvalid',
        path: ['document'],
      });
    }
  });
}

export const vendorSchema = refineDocument(
  z.object({
    ...personFields(),
    paymentTerms: z.string(),
    bankInfo: z.string(),
  }),
);
export type VendorValues = z.infer<typeof vendorSchema>;

const nonNegativeNumberString = z
  .string()
  .refine((v) => v.trim() === '' || (!Number.isNaN(Number(v)) && Number(v) >= 0), {
    message: 'validation.nonNegative',
  });

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
    amount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
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
    financialAccountId: z.string().nullable(),
    description: z.string().trim().min(1, { message: 'validation.descriptionRequired' }),
    documentNumber: z.string(),
    issueDate: z.string(),
    mode: z.enum(['single', 'installments']),
    totalAmount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
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
export type PayableValues = z.infer<typeof payableSchema>;

/** Preview the generated installments for the form (mirrors backend GenerateInstallments). */
export function previewInstallments(total: number, count: number, firstDue: string, intervalMonths: number) {
  const each = Math.round((total / count) * 100) / 100;
  const rows: { sequence: number; dueDate: string; amount: number }[] = [];
  let acc = 0;
  const base = new Date(firstDue + 'T00:00:00');
  for (let i = 0; i < count; i++) {
    const amount = i === count - 1 ? Math.round((total - acc) * 100) / 100 : each;
    acc += amount;
    const d = new Date(base);
    d.setMonth(d.getMonth() + intervalMonths * i);
    rows.push({ sequence: i + 1, dueDate: d.toISOString().slice(0, 10), amount });
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
});
export type BankAccountValues = z.infer<typeof bankAccountSchema>;

export const paymentSchema = z.object({
  paymentDate: z.string().min(1, { message: 'validation.paymentDateRequired' }),
  amount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
  bankAccountId: z.string().min(1, { message: 'validation.bankAccountRequired' }),
  method: z.enum(['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other']),
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
    totalAmount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
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
  amount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
  bankAccountId: z.string().min(1, { message: 'validation.bankAccountRequired' }),
  method: z.enum(['Pix', 'Ted', 'Boleto', 'Cash', 'DirectDebit', 'Other']),
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
  amount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
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
    const debit = val.lines.filter((l) => l.side === 'Debit').reduce((acc, l) => acc + (Number(l.amount) || 0), 0);
    const credit = val.lines.filter((l) => l.side === 'Credit').reduce((acc, l) => acc + (Number(l.amount) || 0), 0);
    if (Math.round((debit - credit) * 100) !== 0) {
      ctx.addIssue({ code: 'custom', message: 'validation.unbalancedEntry', path: ['lines'] });
    }
  });
export type JournalEntryValues = z.infer<typeof journalEntrySchema>;

export const reconciledBalanceSchema = z.object({
  balance: z.string().refine((v) => v.trim() !== '' && !Number.isNaN(Number(v)), {
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
});
export type UnitValues = z.infer<typeof unitSchema>;

export const costCenterSchema = z.object({
  code: z.string().trim().min(1, { message: 'validation.codeRequired' }),
  name: z.string().trim().min(1, { message: 'validation.nameRequired' }),
  annualBudget: nonNegativeNumberString,
  autoApprovalLimit: nonNegativeNumberString,
  unitIds: z.array(z.string()),
});
export type CostCenterValues = z.infer<typeof costCenterSchema>;

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
