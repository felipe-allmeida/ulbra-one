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

export const transactionSchema = z.object({
  type: z.enum(['Payable', 'Receivable']),
  unitId: z.string().min(1, { message: 'validation.unitRequired' }),
  costCenterId: z.string().min(1, { message: 'validation.costCenterRequired' }),
  description: z.string().trim().min(1, { message: 'validation.descriptionRequired' }),
  counterparty: z.string(),
  amount: z.string().refine((v) => Number(v) > 0, { message: 'validation.amountPositive' }),
  dueDate: z.string().min(1, { message: 'validation.dueDateRequired' }),
});
export type TransactionValues = z.infer<typeof transactionSchema>;

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
