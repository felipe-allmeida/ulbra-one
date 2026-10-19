import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Ban, Link as LinkIcon, SendHorizonal } from 'lucide-react';
import { Link } from 'react-router-dom';
import { type PaymentMethod, type Requisition } from '../../api';
import { useAddRecurrenceCycle, useTerminateRecurrence, useVendor } from '../../queries';
import { PAYMENT_METHODS } from '../schemas';
import { brlCents, moneyInput, parseMoneyInput } from '../../lib';
import { useOriginState } from '../../nav';
import { Button, ConfirmDialog, CurrencyInput, FilePicker, Input, Panel, Select } from '../../ui';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';
import { fmtCompetency, shiftCompetency } from './competency';
import { boletoDigits, isBoletoLineValid, isConvenioBoleto } from './boleto';
import { BoletoPdfReader } from './boleto-pdf-reader';
import { AutoHint } from '../registry-forms';

/** Whitelist de anexo espelha o backend (FileContentValidator). Mesma lista do form de requisição. */
const ACCEPT = '.png,.jpg,.jpeg,.webp,.gif,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.xml';

/** Status de pagamento de um ciclo pelo realizado × devido (AEL-881), mesma tolerância de centavos do domínio. */
type CycleStatus = 'paid' | 'partial' | 'open';
const cycleStatus = (realized: number, amount: number): CycleStatus =>
  realized + 0.005 >= amount ? 'paid' : realized > 0 ? 'partial' : 'open';

/**
 * AEL-877 — painel de ciclos de uma requisição recorrente (Conta Fixa) já em pagamento: lista os
 * títulos (Payables) gerados — o base da aprovação + cada fatura mensal lançada — e, para o Solicitante, oferece
 * lançar a fatura do mês (reusando o campo de boleto + badge de convênio do complemento financeiro) e encerrar a
 * recorrência. Sem re-aprovação: a aprovação inicial cobre as faturas futuras.
 */
export function RecurrenceCyclesPanel({ req, canLaunch }: { req: Requisition; canLaunch: boolean }) {
  const { t } = useTranslation();
  const originState = useOriginState();
  const toast = useToast();
  const add = useAddRecurrenceCycle();
  const terminate = useTerminateRecurrence();

  // Próxima competência sugerida = última competência (base ou ciclo) + 1 mês.
  const lastCompetency = useMemo(
    () => req.cycles.reduce((max, c) => (c.competency > max ? c.competency : max), req.startCompetency),
    [req.cycles, req.startCompetency],
  );

  const [competency, setCompetency] = useState(() => shiftCompetency(lastCompetency, 1));
  const [dueDate, setDueDate] = useState('');
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<PaymentMethod>('Pix');
  const [boleto, setBoleto] = useState('');
  const [pixOverride, setPixOverride] = useState(''); // AEL-938: per-invoice PIX key override
  // Fields the AI filled from the boleto, badged like the NF-based payable form; cleared once edited by hand.
  const [auto, setAuto] = useState({ boleto: false, dueDate: false, amount: false });
  const vendorPixKey = useVendor(req.vendorId).data?.bankInfo?.pixKey ?? '';
  // NF anexada no próprio lançamento (Conta Fixa) — vai direto ao Payable do ciclo, não aos anexos gerais.
  const [file, setFile] = useState<File | null>(null);
  const [showErrors, setShowErrors] = useState(false);
  const [terminating, setTerminating] = useState(false);
  const [endCompetency, setEndCompetency] = useState(lastCompetency);

  // Boleto (AEL-932): a fatura Boleto exige a linha digitável válida (47 bancário / 48 convênio) na entrada.
  const boletoOk = method !== 'Boleto' || (!!boleto.trim() && isBoletoLineValid(boleto));
  // Conta Fixa (AEL-959): the month's fatura is mandatory — mirrors the backend gate.
  const invoiceOk = req.type !== 'FixedExpense' || !!file;
  const valid = !!competency && !!dueDate && (parseMoneyInput(amount) ?? 0) > 0 && boletoOk && invoiceOk;

  const submit = () => {
    if (!valid) {
      setShowErrors(true);
      return;
    }
    add.mutate(
      {
        id: req.id,
        body: {
          competency,
          dueDate,
          amount: parseMoneyInput(amount) ?? 0,
          paymentMethod: method,
          ...(method === 'Boleto' && boletoDigits(boleto) ? { boletoDigitableLine: boletoDigits(boleto) } : {}),
          ...(method === 'Pix' && pixOverride.trim() ? { pixKeyOverride: pixOverride.trim() } : {}),
        },
        file,
      },
      {
        onError: (e) => toast.error(getErrorMessage(e)),
        onSuccess: () => {
          toast.success(t('requisitions.recurrence.launched'));
          setAmount('');
          setBoleto('');
          setPixOverride('');
          setDueDate('');
          setAuto({ boleto: false, dueDate: false, amount: false });
          setFile(null);
          setShowErrors(false);
          setCompetency((c) => shiftCompetency(c, 1));
        },
      },
    );
  };

  // O ciclo-base (Payable da aprovação) + os ciclos mensais lançados, numa lista única.
  const rows = [
    {
      key: 'base',
      isBase: true,
      competency: req.startCompetency,
      amount: req.estimateValue,
      status: cycleStatus(req.baseRealizedAmount, req.estimateValue),
      dueDate: req.firstDueDate,
      method: null as PaymentMethod | null,
      boleto: null as string | null,
      payableId: req.basePayableId,
    },
    ...req.cycles.map((c) => ({
      key: c.competency,
      isBase: false,
      competency: c.competency,
      amount: c.amount,
      status: cycleStatus(c.realizedAmount, c.amount),
      dueDate: c.dueDate,
      method: c.paymentMethod,
      boleto: c.boletoDigitableLine,
      payableId: c.payableId,
    })),
  ];

  const busy = add.isPending;

  return (
    <Panel title={t('requisitions.recurrence.title')}>
      <div className='rc-body'>
        <p className='fc-hint'>{t('requisitions.recurrence.hint')}</p>
        {req.type === 'FixedExpense' && <p className='rc-nf-hint'>{t('requisitions.recurrence.fixedExpenseNfHint')}</p>}

        <div className='rc-list'>
          <div className='rc-head'>
            <span>{t('requisitions.recurrence.competency')}</span>
            <span>{t('requisitions.recurrence.amount')}</span>
            <span>{t('requisitions.recurrence.dueDate')}</span>
            <span>{t('requisitions.recurrence.method')}</span>
            <span>{t('requisitions.recurrence.statusHeader')}</span>
            <span>{t('requisitions.recurrence.payable')}</span>
          </div>
          {rows.map((r) => (
            <div className='rc-row' key={r.key}>
              <span>
                {fmtCompetency(r.competency)}
                {r.isBase && <span className='rc-tag'>{t('requisitions.recurrence.base')}</span>}
              </span>
              <span>{brlCents(r.amount)}</span>
              <span>{r.dueDate}</span>
              <span>
                {r.method ? t(`payments.methods.${r.method}`) : '—'}
                {r.boleto && isConvenioBoleto(r.boleto) && (
                  <span className='fc-boleto-badge' style={{ marginLeft: 6 }}>
                    {t('requisitions.financialComplement.convenioBadge')}
                  </span>
                )}
              </span>
              <span>
                <span className={`rc-status rc-status-${r.status}`}>
                  {t(`requisitions.recurrence.status_${r.status}`)}
                </span>
              </span>
              <span>
                {r.payableId ? (
                  <Link to={`/app/payables/${r.payableId}`} className='rc-payable-link' state={originState}>
                    <LinkIcon size={13} /> {t('requisitions.recurrence.viewPayable')}
                  </Link>
                ) : (
                  <span className='rc-pending'>{t('requisitions.recurrence.pending')}</span>
                )}
              </span>
            </div>
          ))}
        </div>

        {canLaunch && (
          <div className='rc-launch'>
            <div className='rc-launch-title'>{t('requisitions.recurrence.launchTitle')}</div>
            <div className='rc-launch-grid'>
              <label className='rc-field'>
                <span className='info-label'>{t('requisitions.recurrence.competency')}</span>
                <Input type='month' value={competency} onChange={(e) => setCompetency(e.target.value)} />
              </label>
              <label className='rc-field'>
                <span className='info-label'>
                  {t('requisitions.recurrence.dueDate')}
                  {auto.dueDate && <AutoHint />}
                </span>
                <Input
                  type='date'
                  className={auto.dueDate ? 'input-auto' : undefined}
                  value={dueDate}
                  onChange={(e) => {
                    setDueDate(e.target.value);
                    setAuto((a) => ({ ...a, dueDate: false }));
                  }}
                />
              </label>
              <label className='rc-field'>
                <span className='info-label'>
                  {t('requisitions.recurrence.amount')}
                  {auto.amount && <AutoHint />}
                </span>
                <CurrencyInput
                  className={auto.amount ? 'input-auto' : undefined}
                  value={amount}
                  onValueChange={(v) => {
                    setAmount(v);
                    setAuto((a) => ({ ...a, amount: false }));
                  }}
                />
              </label>
              <label className='rc-field'>
                <span className='info-label'>{t('requisitions.recurrence.method')}</span>
                <Select
                  value={method}
                  onChange={(e) => {
                    setMethod(e.target.value as PaymentMethod);
                    setAuto((a) => ({ ...a, boleto: false }));
                  }}
                >
                  {PAYMENT_METHODS.map((m) => (
                    <option key={m} value={m}>
                      {t(`payments.methods.${m}`)}
                    </option>
                  ))}
                </Select>
              </label>
            </div>
            {method === 'Boleto' && (
              <>
                <div className='fc-boleto'>
                  <Input
                    inputMode='numeric'
                    className={auto.boleto ? 'input-auto' : undefined}
                    value={boleto}
                    placeholder={t('requisitions.financialComplement.boletoPlaceholder')}
                    aria-label={t('requisitions.financialComplement.boleto')}
                    // Digits only on type/paste (strip dots, spaces and separators).
                    onChange={(e) => {
                      setBoleto(boletoDigits(e.target.value));
                      setAuto((a) => ({ ...a, boleto: false }));
                    }}
                  />
                  {isConvenioBoleto(boleto) && (
                    <span className='fc-boleto-badge'>{t('requisitions.financialComplement.convenioBadge')}</span>
                  )}
                  {auto.boleto && <AutoHint />}
                  {showErrors && method === 'Boleto' && !(boleto.trim() && isBoletoLineValid(boleto)) && (
                    <div className='field-err'>{t('requisitions.financialComplement.boletoInvalid')}</div>
                  )}
                </div>
                {/* Reader on its own row so it is not squeezed beside the input. */}
                <div className='fc-boleto'>
                  <BoletoPdfReader
                    companyId={req.companyId}
                    onApply={(p) => {
                      if (!p.digitableLine) return;
                      setBoleto(boletoDigits(p.digitableLine));
                      const filled = { boleto: true, dueDate: false, amount: false };
                      if (p.dueDate) {
                        setDueDate(p.dueDate.slice(0, 10));
                        filled.dueDate = true;
                      }
                      if ((parseMoneyInput(amount) ?? 0) <= 0 && p.amount != null) {
                        setAmount(moneyInput(p.amount));
                        filled.amount = true;
                      }
                      setAuto(filled);
                    }}
                  />
                </div>
              </>
            )}
            {method === 'Pix' && (
              <div className='fc-pix'>
                {/* Warning above the field: overrides the vendor's registered key for this invoice only (AEL-938).
                    Two variants so it never reads "overrides the key (none registered)". */}
                <span className='fc-pix-hint'>
                  {vendorPixKey.trim()
                    ? t('requisitions.financialComplement.pixOverrideHint', { pixKey: vendorPixKey.trim() })
                    : t('requisitions.financialComplement.pixOverrideHintNone')}
                </span>
                <Input
                  value={pixOverride}
                  onChange={(e) => setPixOverride(e.target.value)}
                  placeholder={t('requisitions.financialComplement.pixOverridePlaceholder')}
                  aria-label={t('requisitions.financialComplement.pixOverride')}
                />
              </div>
            )}
            {/* Conta Fixa: a NF é anexada aqui e vai direto ao Payable desta fatura (não aos anexos gerais). */}
            {req.type === 'FixedExpense' && (
              <label className='rc-field'>
                <span className='info-label'>{t('requisitions.recurrence.attachNf')}</span>
                <FilePicker
                  accept={ACCEPT}
                  fileName={file?.name}
                  placeholder={t('requisitions.recurrence.attachNfPlaceholder')}
                  onSelect={setFile}
                />
                <span className='fc-hint'>{t('requisitions.recurrence.attachNfHint')}</span>
                {showErrors && !file && <div className='field-err'>{t('requisitions.recurrence.invoiceRequired')}</div>}
              </label>
            )}
            {showErrors && !valid && boletoOk && (
              <div className='field-err'>{t('requisitions.recurrence.rowInvalid')}</div>
            )}
            <div className='rc-actions'>
              <Button variant='brand' icon={<SendHorizonal />} disabled={busy} onClick={submit}>
                {busy ? t('requisitions.recurrence.launching') : t('requisitions.recurrence.launch')}
              </Button>
              <Button icon={<Ban />} onClick={() => setTerminating(true)}>
                {t('requisitions.recurrence.terminate')}
              </Button>
            </div>
          </div>
        )}
      </div>

      {terminating && (
        <ConfirmDialog
          title={t('requisitions.recurrence.terminateTitle')}
          confirmLabel={t('requisitions.recurrence.terminateConfirm')}
          busy={terminate.isPending}
          message={
            <div className='rc-field'>
              <span className='info-label'>{t('requisitions.recurrence.endCompetency')}</span>
              <Input type='month' value={endCompetency} onChange={(e) => setEndCompetency(e.target.value)} />
              <span className='fc-hint'>{t('requisitions.recurrence.terminateHelp')}</span>
            </div>
          }
          onConfirm={() =>
            terminate.mutate(
              { id: req.id, endCompetency },
              {
                onError: (e) => toast.error(getErrorMessage(e)),
                onSuccess: () => {
                  toast.success(t('requisitions.recurrence.terminated'));
                  setTerminating(false);
                },
              },
            )
          }
          onCancel={() => setTerminating(false)}
        />
      )}
    </Panel>
  );
}
