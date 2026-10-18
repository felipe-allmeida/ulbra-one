import { type ReactNode, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SendHorizonal } from 'lucide-react';
import { type Requisition } from '../../api';
import { useRegisterRequisitionAttachment, useSubmitFinancialComplement, useVendor } from '../../queries';
import { previewInstallments } from '../schemas';
import {
  type Row,
  InstallmentsSubform,
  buildInstallmentsPayload,
  intervalMonthsFor,
  validateInstallments,
} from './installments-subform';
import { brlCents, moneyInput, parseMoneyInput, roundCents } from '../../lib';
import { Button, Kpi, Panel } from '../../ui';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';

/**
 * AEL-844 — "Send financial information" form (FinancialComplement step), below the forecast panel. The
 * requester authors the installment plan (due date, value to transfer and payment method) and informs the
 * single issue (emission) date. The installment editor is the shared `InstallmentsSubform` (reused by the
 * create/edit form's front-load section); this form keeps the "billing summary" — Approved × Programmed ×
 * Difference — and the submit flow: if the programmed total rises above the amount approved at triage, submitting
 * sends the requisition back to Triage (server rule). The fiscal-document/invoice upload is the attachments panel
 * passed via `children` (rendered between the summary and the submit button).
 */
function seedRows(req: Requisition): Row[] {
  // Re-visiting after a submit that returned to triage → prefill from the previously authored plan.
  if (req.installments.length > 0)
    return req.installments.map((i) => ({
      dueDate: i.dueDate,
      amount: moneyInput(i.amount),
      paymentMethod: i.paymentMethod,
      boletoDigitableLine: i.boletoDigitableLine ?? '',
      pixKeyOverride: i.pixKeyOverride ?? '',
    }));
  const count = req.paymentInstallmentsCount && req.paymentInstallmentsCount > 1 ? req.paymentInstallmentsCount : 1;
  return previewInstallments(req.estimateValue, count, req.firstDueDate, intervalMonthsFor(req.paymentFrequency)).map(
    (p) => ({
      dueDate: p.dueDate,
      amount: moneyInput(p.amount),
      paymentMethod: 'Pix',
      boletoDigitableLine: '',
      pixKeyOverride: '',
    }),
  );
}

/** `children` = the attachments panel (fiscal document or invoice), rendered between the summary and the button. */
export function FinancialComplementForm({ req, children }: { req: Requisition; children?: ReactNode }) {
  const { t } = useTranslation();
  const toast = useToast();
  const submit = useSubmitFinancialComplement();
  // Vendor's registered PIX key, for the per-payment override warning (AEL-938). The requisition carries only
  // the vendorId, so fetch it (same pattern as the summary card).
  const vendorPixKey = useVendor(req.vendorId).data?.bankInfo?.pixKey ?? '';
  // The requisition already exists here, so a boleto read by the AI is attached immediately, by key (AEL-938).
  const registerAttachment = useRegisterRequisitionAttachment();

  const [rows, setRows] = useState<Row[]>(() => seedRows(req));
  const [issueDate, setIssueDate] = useState<string>(req.issueDate ?? '');
  const [showErrors, setShowErrors] = useState(false);

  // Recorrente (Conta Fixa): o complemento autora só a PRIMEIRA fatura (uma parcela = um boleto);
  // os meses seguintes são lançados como faturas após a aprovação — sem "adicionar parcela" (guard do domínio,
  // AEL-877). O seed já produz a linha única.
  const recorrente = req.type === 'FixedExpense';

  // "Approved" = the requisition's current value (in the FinancialComplement stage it is the amount approved at triage).
  const approved = req.estimateValue;
  const programmed = useMemo(() => rows.reduce((s, r) => s + (parseMoneyInput(r.amount) ?? 0), 0), [rows]);
  const difference = programmed - approved;

  const validity = validateInstallments(rows, issueDate);

  const handleSubmit = () => {
    if (!validity.valid) {
      setShowErrors(true);
      return;
    }
    submit.mutate(
      { id: req.id, issueDate, installments: buildInstallmentsPayload(rows) },
      {
        onError: (e) => toast.error(getErrorMessage(e)),
        onSuccess: (r) =>
          toast.success(
            r.status === 'Approval'
              ? t('requisitions.financialComplement.sent')
              : t('requisitions.financialComplement.returnedToTriage'),
          ),
      },
    );
  };

  const busy = submit.isPending;

  return (
    <>
      <Panel title={t('requisitions.financialComplement.title')}>
        <div className='fc-body'>
          <p className='fc-hint'>{t('requisitions.financialComplement.hint')}</p>

          <InstallmentsSubform
            companyId={req.companyId}
            vendorPixKey={vendorPixKey}
            onBoletoBlob={(b) =>
              // A boleto read via "Ler PDF do boleto" is always a boleto — tag it BankSlip (AEL-959), never Other.
              registerAttachment.mutate(
                { id: req.id, ...b, kind: 'BankSlip' },
                { onError: (e) => toast.error(getErrorMessage(e)) },
              )
            }
            rows={rows}
            onRowsChange={setRows}
            issueDate={issueDate}
            onIssueDateChange={setIssueDate}
            recurring={recorrente}
            firstDueDate={req.firstDueDate}
            fallbackTotal={approved}
            showErrors={showErrors}
          />

          <div className='kpi-grid'>
            <Kpi label={t('requisitions.financialComplement.approved')} value={brlCents(approved)} />
            <Kpi label={t('requisitions.financialComplement.programmed')} value={brlCents(programmed)} />
            <Kpi
              label={t('requisitions.financialComplement.difference')}
              value={brlCents(difference)}
              foot={roundCents(difference) > 0 ? t('requisitions.financialComplement.willReturnToTriage') : undefined}
            />
          </div>

          {showErrors && !validity.rowsValid && (
            <div className='field-err'>{t('requisitions.financialComplement.rowsInvalid')}</div>
          )}
        </div>
      </Panel>

      {/* Attachments panel (fiscal document or invoice) — the single upload point, injected by the detail. */}
      {children}

      <div className='fc-submit'>
        <Button variant='brand' icon={<SendHorizonal />} disabled={busy} onClick={handleSubmit}>
          {busy ? t('requisitions.financialComplement.sending') : t('requisitions.financialComplement.submit')}
        </Button>
      </div>
      {/* Resumo dos obrigatórios em branco, logo abaixo do botão — deixa claro o que falta (ex.: data de emissão). */}
      {showErrors && !(validity.issueValid && validity.rowsValid) && (
        <div className='field-err' style={{ marginTop: 8 }}>
          <div>{t('common.requiredMissing')}</div>
          {validity.issueMissing && <div>• {t('requisitions.financialComplement.issueDateRequired')}</div>}
          {validity.issueAfterDue && <div>• {t('requisitions.financialComplement.issueDateAfterDue')}</div>}
          {!validity.rowsValid && <div>• {t('requisitions.financialComplement.rowsInvalid')}</div>}
        </div>
      )}
    </>
  );
}
