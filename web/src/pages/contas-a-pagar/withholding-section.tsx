import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Trash2 } from 'lucide-react';
import { type Payable, type WithholdingTaxType } from '../../api';
import { useSetPayableWithholdings } from '../../queries';
import { getErrorMessage } from '../../apiError';
import { useToast } from '../../toast';
import { Button, Input, Select, Panel } from '../../ui';
import { brlCents } from '../../lib';

const TAX_TYPES: WithholdingTaxType[] = ['Irrf', 'Iss', 'Inss', 'Pis', 'Cofins', 'Csll', 'Other'];

interface Row {
  taxType: WithholdingTaxType;
  baseAmount: string;
  rate: string;
  amount: string;
}

function emptyRow(): Row {
  return { taxType: 'Irrf', baseAmount: '', rate: '', amount: '' };
}

function rowToItem(r: Row) {
  return {
    taxType: r.taxType,
    baseAmount: Number(r.baseAmount) || 0,
    rate: r.rate.trim() ? Number(r.rate) : null,
    amount: Number(r.amount) || 0,
  };
}

/* ============ Editable withholding section ============ */
function WithholdingSectionEditable({ payable }: { payable: Payable }) {
  const { t } = useTranslation();
  const toast = useToast();

  const [rows, setRows] = useState<Row[]>(() =>
    payable.withholdings.length > 0
      ? payable.withholdings.map((w) => ({
          taxType: w.taxType,
          baseAmount: String(w.baseAmount),
          rate: w.rate != null ? String(w.rate) : '',
          amount: String(w.amount),
        }))
      : [],
  );

  // Prefill from server whenever payable changes
  useEffect(() => {
    setRows(
      payable.withholdings.length > 0
        ? payable.withholdings.map((w) => ({
            taxType: w.taxType,
            baseAmount: String(w.baseAmount),
            rate: w.rate != null ? String(w.rate) : '',
            amount: String(w.amount),
          }))
        : [],
    );
  }, [payable.id, payable.withholdings]);

  const save = useSetPayableWithholdings({
    onSuccess: () => toast.success(t('payables.withholding.saveSuccess')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const totalRetido = rows.reduce((s, r) => s + (Number(r.amount) || 0), 0);
  const liquido = payable.totalAmount - totalRetido;

  const addRow = () => setRows((prev) => [...prev, emptyRow()]);
  const removeRow = (i: number) => setRows((prev) => prev.filter((_, idx) => idx !== i));

  const updateRow = (i: number, field: keyof Row, value: string) =>
    setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)));

  const handleSave = () => {
    save.mutate({ id: payable.id, items: rows.map(rowToItem) });
  };

  return (
    <Panel title={t('payables.withholding.sectionTitle')}>
      {rows.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>{t('payables.withholding.columns.taxType')}</th>
              <th style={{ textAlign: 'right' }}>{t('payables.withholding.columns.base')}</th>
              <th style={{ textAlign: 'right' }}>{t('payables.withholding.columns.rate')}</th>
              <th style={{ textAlign: 'right' }}>{t('payables.withholding.columns.amount')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={i}>
                <td>
                  <Select value={row.taxType} onChange={(e) => updateRow(i, 'taxType', e.target.value)}>
                    {TAX_TYPES.map((tt) => (
                      <option key={tt} value={tt}>
                        {t(`payables.withholding.taxType.${tt}`)}
                      </option>
                    ))}
                  </Select>
                </td>
                <td style={{ textAlign: 'right' }}>
                  <Input
                    inputMode='numeric'
                    value={row.baseAmount}
                    onChange={(e) => updateRow(i, 'baseAmount', e.target.value)}
                    style={{ textAlign: 'right' }}
                  />
                </td>
                <td style={{ textAlign: 'right' }}>
                  <Input
                    inputMode='numeric'
                    value={row.rate}
                    onChange={(e) => updateRow(i, 'rate', e.target.value)}
                    style={{ textAlign: 'right' }}
                    placeholder='—'
                  />
                </td>
                <td style={{ textAlign: 'right' }}>
                  <Input
                    inputMode='numeric'
                    value={row.amount}
                    onChange={(e) => updateRow(i, 'amount', e.target.value)}
                    style={{ textAlign: 'right' }}
                  />
                </td>
                <td className='td-action'>
                  <Button
                    icon={<Trash2 />}
                    onClick={() => removeRow(i)}
                    aria-label={t('payables.withholding.removeRow')}
                  >
                    {t('payables.withholding.removeRow')}
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
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
    </Panel>
  );
}

/* ============ Read-only withholding section ============ */
function WithholdingSectionReadOnly({ payable }: { payable: Payable }) {
  const { t } = useTranslation();

  return (
    <Panel title={t('payables.withholding.sectionTitle')}>
      {payable.withholdings.length > 0 ? (
        <table>
          <thead>
            <tr>
              <th>{t('payables.withholding.columns.taxType')}</th>
              <th style={{ textAlign: 'right' }}>{t('payables.withholding.columns.amount')}</th>
            </tr>
          </thead>
          <tbody>
            {payable.withholdings.map((w) => (
              <tr key={w.id}>
                <td>{t(`payables.withholding.taxType.${w.taxType}`)}</td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brlCents(w.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}

      <div className='info-grid' style={{ marginTop: 16 }}>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.bruto')}</div>
          <div className='info-value'>{brlCents(payable.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.retido')}</div>
          <div className='info-value'>{brlCents(payable.totalWithheld)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('payables.withholding.liquido')}</div>
          <div className='info-value' data-testid='withholding-liquido'>
            {brlCents(payable.netTotal)}
          </div>
        </div>
      </div>
    </Panel>
  );
}

/* ============ Public façade ============ */
export function WithholdingSection({ payable, editable }: { payable: Payable; editable: boolean }) {
  if (editable) return <WithholdingSectionEditable payable={payable} />;
  return <WithholdingSectionReadOnly payable={payable} />;
}
