import { Fragment, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2 } from 'lucide-react';
import {
  type BoletoBlobRef,
  type FinancialComplementInstallmentInput,
  type ParsedBoleto,
  type PaymentMethod,
  type Requisition,
} from '../../api';
import { PAYMENT_METHODS, previewInstallments } from '../schemas';
import { boletoDigits, isBoletoLineValid, isConvenioBoleto } from './boleto';
import { BoletoPdfReader } from './boleto-pdf-reader';
import { AutoHint } from '../registry-forms';
import { moneyInput, parseMoneyInput } from '../../lib';
import { Button, CurrencyInput, Input, Select } from '../../ui';

/** Which boleto error to show: a 47-digit convênio (lead '8') is almost always a 48-digit line that lost a
 * digit — call that out specifically; otherwise the generic length hint. */
function boletoErrorKey(line?: string): string {
  const d = boletoDigits(line ?? '');
  return isConvenioBoleto(d) && d.length === 47
    ? 'requisitions.financialComplement.boletoConvenioIncomplete'
    : 'requisitions.financialComplement.boletoInvalid';
}

/**
 * Controlled installment editor shared by the financial-complement step and the create/edit form's front-load
 * section: the emission-date field, the installment rows (due date / value / method + boleto sub-row) and
 * the add/redistribute actions. State lives in the parent (rows + issueDate); the KPI summary and the submit flow
 * stay with each caller.
 */
export interface Row {
  dueDate: string;
  amount: string; // raw string as typed; wire sends Number(amount)
  paymentMethod: PaymentMethod;
  boletoDigitableLine?: string; // only relevant when paymentMethod === 'Boleto'
  pixKeyOverride?: string; // only relevant when paymentMethod === 'Pix' (AEL-938)
}

/** Cadence → month step used only to SEED due dates: 6 for Semiannual, 12 for Annual, 1 for anything else
 * (Monthly/Biweekly). Only a coarse seed — the user edits the actual dates anyway. */
export function intervalMonthsFor(freq: Requisition['paymentFrequency']): number {
  return freq === 'Semiannual' ? 6 : freq === 'Annual' ? 12 : 1;
}

export interface InstallmentsValidity {
  issueMissing: boolean;
  issueAfterDue: boolean;
  issueValid: boolean;
  rowsValid: boolean;
  boletoValid: boolean;
  valid: boolean;
}

/** Validity of the authored plan — mirrors the domain guards (AEL-844): emission date required and not after the
 * earliest due date; every row has a due date + a positive amount. Boleto (AEL-932): a Boleto row REQUIRES a
 * valid linha digitável (47/48 dígitos) — blank is rejected — so an invalid/missing barcode never becomes a
 * silent remittance-time skip. Mirrors the backend FinancialPlanValidation. */
export function validateInstallments(rows: Row[], issueDate: string): InstallmentsValidity {
  const earliestDue = rows.reduce((min, r) => (r.dueDate && (!min || r.dueDate < min) ? r.dueDate : min), '');
  const issueMissing = !issueDate;
  const issueAfterDue = !!issueDate && !!earliestDue && issueDate > earliestDue;
  const issueValid = !issueMissing && !issueAfterDue;
  const rowsValid = rows.length > 0 && rows.every((r) => r.dueDate && (parseMoneyInput(r.amount) ?? 0) > 0);
  const boletoValid = rows.every(
    (r) =>
      r.paymentMethod !== 'Boleto' || (!!r.boletoDigitableLine?.trim() && isBoletoLineValid(r.boletoDigitableLine)),
  );
  return {
    issueMissing,
    issueAfterDue,
    issueValid,
    rowsValid,
    boletoValid,
    valid: issueValid && rowsValid && boletoValid,
  };
}

/** Projects the rows to the wire shape — the boleto key is omitted entirely on non-boleto rows. */
export function buildInstallmentsPayload(rows: Row[]): FinancialComplementInstallmentInput[] {
  return rows.map((r) => ({
    dueDate: r.dueDate,
    amount: parseMoneyInput(r.amount) ?? 0,
    paymentMethod: r.paymentMethod,
    ...(r.paymentMethod === 'Boleto' && boletoDigits(r.boletoDigitableLine ?? '')
      ? { boletoDigitableLine: boletoDigits(r.boletoDigitableLine ?? '') }
      : {}),
    // PIX override (AEL-938): sent only on a filled Pix row; blank = use the vendor's key.
    ...(r.paymentMethod === 'Pix' && r.pixKeyOverride?.trim() ? { pixKeyOverride: r.pixKeyOverride.trim() } : {}),
  }));
}

export function InstallmentsSubform({
  companyId,
  vendorPixKey,
  onBoletoBlob,
  rows,
  onRowsChange,
  issueDate,
  onIssueDateChange,
  recurring,
  firstDueDate,
  fallbackTotal,
  showErrors,
}: {
  /** Scopes the boleto reader's presigned upload + parse to the requisition's company. */
  companyId: string;
  /** The vendor's registered PIX key, shown in the override warning (AEL-938). Empty when none/unknown. */
  vendorPixKey?: string;
  /** Called with the boleto blob (key + metadata) read by the AI, so the parent attaches it by key (AEL-938). */
  onBoletoBlob?: (blob: BoletoBlobRef) => void;
  rows: Row[];
  onRowsChange: (rows: Row[]) => void;
  issueDate: string;
  onIssueDateChange: (value: string) => void;
  /** Conta Fixa → authors a single row; hides add/redistribute (domain guard, AEL-877). */
  recurring: boolean;
  /** Default due date for a new row. */
  firstDueDate: string;
  /** Seed total for "redistribute" when the rows have no amounts yet. */
  fallbackTotal: number;
  showErrors: boolean;
}) {
  const { t } = useTranslation();
  const { issueMissing, issueAfterDue } = validateInstallments(rows, issueDate);

  // Fields the AI auto-filled (keys `${i}:dueDate|amount|boleto` + the global `issueDate`), badged like the
  // NF-based payable form; a marker clears once the user edits that field by hand.
  const [autoKeys, setAutoKeys] = useState<Set<string>>(new Set());
  const markAuto = (keys: string[]) =>
    setAutoKeys((prev) => {
      const next = new Set(prev);
      keys.forEach((k) => next.add(k));
      return next;
    });
  const clearAuto = (...keys: string[]) =>
    setAutoKeys((prev) => {
      if (!keys.some((k) => prev.has(k))) return prev;
      const next = new Set(prev);
      keys.forEach((k) => next.delete(k));
      return next;
    });

  const addRow = () =>
    onRowsChange([
      ...rows,
      { dueDate: firstDueDate, amount: '', paymentMethod: 'Pix', boletoDigitableLine: '', pixKeyOverride: '' },
    ]);
  const removeRow = (i: number) => {
    if (rows.length <= 1) return;
    onRowsChange(rows.filter((_, idx) => idx !== i));
    setAutoKeys(new Set()); // indices shift on removal — drop the (cosmetic) auto markers
  };
  const updateRow = (i: number, field: keyof Row, value: string) => {
    onRowsChange(rows.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)));
    if (field === 'boletoDigitableLine') clearAuto(`${i}:boleto`);
    else if (field === 'dueDate') clearAuto(`${i}:dueDate`);
    else if (field === 'amount') clearAuto(`${i}:amount`);
  };
  const editIssueDate = (value: string) => {
    onIssueDateChange(value);
    clearAuto('issueDate');
  };
  // Switching method clears the previous method's field (boleto/pix) to avoid an orphan line/key.
  const changeMethod = (i: number, method: PaymentMethod) => {
    onRowsChange(
      rows.map((r, idx) =>
        idx === i
          ? {
              ...r,
              paymentMethod: method,
              boletoDigitableLine: method === 'Boleto' ? r.boletoDigitableLine : '',
              pixKeyOverride: method === 'Pix' ? r.pixKeyOverride : '',
            }
          : r,
      ),
    );
    clearAuto(`${i}:boleto`);
  };

  // Apply a parsed boleto to row i: line always; due date/amount only when present and not already set
  // (never clobber a manual split); global issue date only when still blank. Marks the filled fields as auto.
  const applyBoletoToRow = (i: number, p: ParsedBoleto) => {
    if (!p.digitableLine) return;
    const filled = [`${i}:boleto`];
    onRowsChange(
      rows.map((r, idx) => {
        if (idx !== i) return r;
        const next: Row = { ...r, boletoDigitableLine: boletoDigits(p.digitableLine!) };
        if (p.dueDate) {
          next.dueDate = p.dueDate.slice(0, 10);
          filled.push(`${i}:dueDate`);
        }
        if ((parseMoneyInput(r.amount) ?? 0) <= 0 && p.amount != null) {
          next.amount = moneyInput(p.amount);
          filled.push(`${i}:amount`);
        }
        return next;
      }),
    );
    if (p.issueDate && !issueDate) {
      onIssueDateChange(p.issueDate.slice(0, 10));
      filled.push('issueDate');
    }
    markAuto(filled);
  };

  /** Redistributes the current total evenly across the existing rows (remainder on the last), keeping dates. */
  const redistribute = () => {
    const total = rows.reduce((s, r) => s + (parseMoneyInput(r.amount) ?? 0), 0) || fallbackTotal;
    const preview = previewInstallments(total, rows.length, rows[0]?.dueDate || firstDueDate, 1);
    onRowsChange(rows.map((r, i) => ({ ...r, amount: preview[i] ? moneyInput(preview[i].amount) : r.amount })));
  };

  return (
    <>
      <div className='fc-field'>
        <label className='info-label' htmlFor='fc-issue-date'>
          {t('requisitions.financialComplement.issueDate')}
          {autoKeys.has('issueDate') && <AutoHint />}
        </label>
        <Input
          id='fc-issue-date'
          type='date'
          className={autoKeys.has('issueDate') ? 'input-auto' : undefined}
          value={issueDate}
          onChange={(e) => editIssueDate(e.target.value)}
          style={{ maxWidth: 200 }}
        />
        {showErrors && issueMissing && (
          <div className='field-err'>{t('requisitions.financialComplement.issueDateRequired')}</div>
        )}
        {showErrors && issueAfterDue && (
          <div className='field-err'>{t('requisitions.financialComplement.issueDateAfterDue')}</div>
        )}
      </div>

      <div className='fc-rows'>
        <div className='fc-head'>
          <span>#</span>
          <span>{t('requisitions.financialComplement.dueDate')}</span>
          <span>{t('requisitions.financialComplement.amount')}</span>
          <span>{t('requisitions.financialComplement.method')}</span>
          <span />
        </div>
        {rows.map((row, i) => (
          <Fragment key={i}>
            <div className='fc-row'>
              <span className='fc-seq'>{i + 1}</span>
              <Input
                type='date'
                className={autoKeys.has(`${i}:dueDate`) ? 'input-auto' : undefined}
                value={row.dueDate}
                onChange={(e) => updateRow(i, 'dueDate', e.target.value)}
              />
              <CurrencyInput
                className={autoKeys.has(`${i}:amount`) ? 'input-auto' : undefined}
                value={row.amount}
                onValueChange={(v) => updateRow(i, 'amount', v)}
              />
              <Select value={row.paymentMethod} onChange={(e) => changeMethod(i, e.target.value as PaymentMethod)}>
                {PAYMENT_METHODS.map((m) => (
                  <option key={m} value={m}>
                    {t(`payments.methods.${m}`)}
                  </option>
                ))}
              </Select>
              <Button
                type='button'
                icon={<Trash2 />}
                aria-label={t('requisitions.financialComplement.removeRow')}
                disabled={rows.length <= 1}
                onClick={() => removeRow(i)}
              />
            </div>
            {row.paymentMethod === 'Boleto' && (
              <>
                <div className='fc-boleto'>
                  <Input
                    inputMode='numeric'
                    className={autoKeys.has(`${i}:boleto`) ? 'input-auto' : undefined}
                    value={row.boletoDigitableLine ?? ''}
                    // Digits only on type/paste (strip dots, spaces and separators from the linha digitável).
                    onChange={(e) => updateRow(i, 'boletoDigitableLine', boletoDigits(e.target.value))}
                    placeholder={t('requisitions.financialComplement.boletoPlaceholder')}
                    aria-label={t('requisitions.financialComplement.boleto')}
                  />
                  {isConvenioBoleto(row.boletoDigitableLine ?? '') && (
                    <span className='fc-boleto-badge'>{t('requisitions.financialComplement.convenioBadge')}</span>
                  )}
                  {autoKeys.has(`${i}:boleto`) && <AutoHint />}
                  {showErrors && !(row.boletoDigitableLine?.trim() && isBoletoLineValid(row.boletoDigitableLine)) && (
                    <div className='field-err'>{t(boletoErrorKey(row.boletoDigitableLine))}</div>
                  )}
                </div>
                {/* Reader on its own row so it is not squeezed beside the input. */}
                <div className='fc-boleto'>
                  <BoletoPdfReader
                    companyId={companyId}
                    onApply={(p) => applyBoletoToRow(i, p)}
                    onBoleto={onBoletoBlob}
                  />
                </div>
              </>
            )}
            {row.paymentMethod === 'Pix' && (
              <div className='fc-pix'>
                {/* Warning above the field: the typed key overrides the vendor's registered one for this payment only.
                    Two variants so it never reads "overrides the key (none registered)". */}
                <span className='fc-pix-hint'>
                  {vendorPixKey?.trim()
                    ? t('requisitions.financialComplement.pixOverrideHint', { pixKey: vendorPixKey.trim() })
                    : t('requisitions.financialComplement.pixOverrideHintNone')}
                </span>
                <Input
                  value={row.pixKeyOverride ?? ''}
                  onChange={(e) => updateRow(i, 'pixKeyOverride', e.target.value)}
                  placeholder={t('requisitions.financialComplement.pixOverridePlaceholder')}
                  aria-label={t('requisitions.financialComplement.pixOverride')}
                />
              </div>
            )}
          </Fragment>
        ))}
      </div>

      {!recurring && (
        <div className='fc-actions-row'>
          <Button type='button' icon={<Plus />} onClick={addRow}>
            {t('requisitions.financialComplement.addRow')}
          </Button>
          <Button type='button' onClick={redistribute}>
            {t('requisitions.financialComplement.redistribute')}
          </Button>
        </div>
      )}
    </>
  );
}
