import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2 } from 'lucide-react';
import { type Payable, type WithholdingTaxType } from '../../api';
import { useSetPayableWithholdings } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, CurrencyInput, Input, Select, Panel } from '../../ui';
import { brlCents, fmtPct, moneyInput, parseMoneyInput, roundCents } from '../../lib';

const TAX_TYPES: WithholdingTaxType[] = ['Irrf', 'Iss', 'Inss', 'Pis', 'Cofins', 'Csll', 'Other'];

// CSRF (PIS/COFINS/CSLL) is retained at payment, not at emission — so its amount here is the foreseen value,
// not yet retido. Mirrors WithholdingTimings.For (Finance.Domain).
const PAYMENT_TAX_TYPES: WithholdingTaxType[] = ['Pis', 'Cofins', 'Csll'];
const isPaymentTax = (t: WithholdingTaxType) => PAYMENT_TAX_TYPES.includes(t);

interface Row {
  taxType: WithholdingTaxType;
  baseAmount: string;
  ratePct: string; // percent as typed by the user (e.g. "1.5" = 1,5%); wire stores it as a decimal fraction
}

function emptyRow(): Row {
  return { taxType: 'Irrf', baseAmount: '', ratePct: '' };
}

/** Withheld amount = base × rate%, rounded to cents like the backend (the server trusts this amount). */
function computeAmount(r: Row): number {
  const base = parseMoneyInput(r.baseAmount) ?? 0;
  const pct = Number(r.ratePct) || 0;
  return roundCents(base * (pct / 100));
}

function rowToItem(r: Row) {
  const pct = r.ratePct.trim() ? Number(r.ratePct) : null;
  return {
    taxType: r.taxType,
    baseAmount: parseMoneyInput(r.baseAmount) ?? 0,
    rate: pct != null ? pct / 100 : null, // wire = decimal fraction (0.0065 = 0,65%)
    amount: computeAmount(r),
  };
}

/** Stable serialization of the edited rows — for the "unsaved changes" comparison against the server state. */
function serializeRows(rows: Row[]): string {
  return JSON.stringify(rows.map((r) => [r.taxType, r.baseAmount.trim(), r.ratePct.trim()]));
}

/* ============ Editable withholding section ============ */
function WithholdingSectionEditable({
  payable,
  bare,
  onDirtyChange,
}: {
  payable: Payable;
  bare?: boolean;
  onDirtyChange?: (dirty: boolean) => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();

  const prefill = (): Row[] =>
    payable.withholdings.map((w) => ({
      taxType: w.taxType,
      baseAmount: moneyInput(w.baseAmount),
      // fraction → percent for display/editing (0.0065 → "0.65"); trim trailing zeros
      ratePct: w.rate != null ? String(Number((w.rate * 100).toFixed(4))) : '',
    }));

  const [rows, setRows] = useState<Row[]>(prefill);

  // Prefill from server whenever the payable changes.
  useEffect(() => {
    setRows(prefill());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [payable.id, payable.withholdings]);

  // "Unsaved changes": local rows diverge from the saved server state. Surfaced so a caller (the tax drawer)
  // can block "Liberar para pagamento" until the withholdings are saved — a release must never carry stale values.
  const dirty = serializeRows(rows) !== serializeRows(prefill());
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const save = useSetPayableWithholdings({
    onSuccess: () => toast.success(t('payables.withholding.saveSuccess')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const totalRetido = rows.reduce((s, r) => s + computeAmount(r), 0);
  const liquido = payable.totalAmount - totalRetido;

  const addRow = () => setRows((prev) => [...prev, emptyRow()]);
  const removeRow = (i: number) => setRows((prev) => prev.filter((_, idx) => idx !== i));
  const updateRow = (i: number, field: keyof Row, value: string) =>
    setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)));

  const handleSave = () => save.mutate({ id: payable.id, items: rows.map(rowToItem) });

  const body = (
    <>
      {rows.length > 0 && (
        <div className='wh-rows'>
          <div className='wh-head'>
            <span>{t('payables.withholding.columns.taxType')}</span>
            <span style={{ textAlign: 'right' }}>{t('payables.withholding.columns.base')}</span>
            <span style={{ textAlign: 'right' }}>{t('payables.withholding.columns.rate')}</span>
            <span style={{ textAlign: 'right' }}>{t('payables.withholding.columns.amount')}</span>
            <span />
          </div>
          {rows.map((row, i) => (
            <div className='wh-row' key={i}>
              <Select value={row.taxType} onChange={(e) => updateRow(i, 'taxType', e.target.value)}>
                {TAX_TYPES.map((tt) => (
                  <option key={tt} value={tt}>
                    {t(`payables.withholding.taxType.${tt}`)}
                  </option>
                ))}
              </Select>
              <CurrencyInput
                value={row.baseAmount}
                onValueChange={(val) => updateRow(i, 'baseAmount', val)}
                style={{ textAlign: 'right' }}
              />
              <Input
                inputMode='numeric'
                value={row.ratePct}
                onChange={(e) => updateRow(i, 'ratePct', e.target.value)}
                style={{ textAlign: 'right' }}
                placeholder='%'
              />
              {/* Withheld value is derived (base × rate) — locked, not editable. */}
              <div
                className='amount-cell'
                style={{ textAlign: 'right', color: 'var(--muted-foreground)' }}
                title={t('payables.withholding.amountAuto')}
              >
                {brlCents(computeAmount(row))}
              </div>
              <span className='wh-remove' data-tip={t('payables.withholding.removeRow')}>
                <Button
                  icon={<Trash2 />}
                  onClick={() => removeRow(i)}
                  aria-label={t('payables.withholding.removeRow')}
                />
              </span>
            </div>
          ))}
        </div>
      )}

      <div style={{ marginTop: 12, display: 'flex', gap: 8 }}>
        <Button icon={<Plus />} onClick={addRow}>
          {t('payables.withholding.addRow')}
        </Button>
      </div>

      <div className='info-grid' style={{ marginTop: 16 }}>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.bruto')}</div>
          <div className='info-value'>{brlCents(payable.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.retido')}</div>
          <div className='info-value'>{brlCents(totalRetido)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.liquido')}</div>
          <div className='info-value' data-testid='withholding-liquido'>
            {brlCents(liquido)}
          </div>
        </div>
      </div>

      <div style={{ marginTop: 16 }}>
        <Button variant='brand' onClick={handleSave} disabled={save.isPending}>
          {save.isPending ? t('payables.withholding.saving') : t('payables.withholding.save')}
        </Button>
      </div>
    </>
  );

  // `bare` renders the content without the Panel card — for the accounting drawer, whose sections are flat
  // (a section title + content) and already inset by the drawer body. In the Panel (payable page) the `.panel`
  // body has no padding of its own, so inset the content to align it with the panel title (not flush to the edge).
  if (bare) return body;
  return (
    <Panel title={t('payables.withholding.sectionTitle')}>
      <div className='wh-panel-body'>{body}</div>
    </Panel>
  );
}

/* ============ Read-only withholding section (2-column block) ============ */
function WithholdingSectionReadOnly({ payable, bare }: { payable: Payable; bare?: boolean }) {
  const { t } = useTranslation();

  const block = (
    <div className='ret-block'>
      {/* Left: gross → withheld → net, all with the same styling (no highlight). */}
      <div className='ret-col'>
        <div className='ret-row'>
          <span className='ret-row-label'>{t('payables.withholding.bruto')}</span>
          <span className='ret-row-value'>{brlCents(payable.totalAmount)}</span>
        </div>
        <div className='ret-row'>
          <span className='ret-row-label'>{t('payables.withholding.retido')}</span>
          <span className='ret-row-value'>{brlCents(payable.totalWithheld)}</span>
        </div>
        <div className='ret-row'>
          <span className='ret-row-label'>{t('payables.withholding.liquido')}</span>
          <span className='ret-row-value' data-testid='withholding-liquido'>
            {brlCents(payable.netTotal)}
          </span>
        </div>
      </div>

      {/* Right: one row per withheld tax — name · rate% + amount; stretches to match the left height. */}
      <div className='ret-col'>
        {payable.withholdings.length > 0 ? (
          payable.withholdings.map((w) => (
            <div className='ret-row' key={w.id}>
              <span>
                <span className='ret-row-label'>{t(`payables.withholding.taxType.${w.taxType}`)}</span>
                {w.rate != null && <span className='ret-row-sub'> · {fmtPct(w.rate)}</span>}
                {isPaymentTax(w.taxType) && (
                  <span className='ret-row-sub' title={t('payables.withholding.onPaymentHint')}>
                    {' '}
                    · {t('payables.withholding.moment.onPayment')}
                  </span>
                )}
              </span>
              <span className='ret-row-value'>{brlCents(w.amount)}</span>
            </div>
          ))
        ) : (
          <div className='ret-row ret-empty'>{t('payables.withholding.noWithholdings')}</div>
        )}
      </div>
    </div>
  );

  // `bare` drops the Panel card so the block fits the flat drawer sections (the caller supplies title + section).
  if (bare) return block;
  return <Panel title={t('payables.withholding.sectionTitle')}>{block}</Panel>;
}

/* ============ Public façade ============ */
export function WithholdingSection({
  payable,
  editable,
  bare,
  onDirtyChange,
}: {
  payable: Payable;
  editable: boolean;
  bare?: boolean;
  /** Editable only: notifies when local edits diverge from the saved state (unsaved changes). */
  onDirtyChange?: (dirty: boolean) => void;
}) {
  if (editable) return <WithholdingSectionEditable payable={payable} bare={bare} onDirtyChange={onDirtyChange} />;
  return <WithholdingSectionReadOnly payable={payable} bare={bare} />;
}
