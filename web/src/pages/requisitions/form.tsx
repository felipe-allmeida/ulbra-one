import { useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm, useWatch, type Control } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, Paperclip, SendHorizonal, X } from 'lucide-react';
import { useApp } from '../../store';
import {
  useDimensionBudgetControl,
  useProjects,
  useRequisition,
  useSaveRequisition,
  useScopedCostCenters,
  useScopedUnits,
  useRegisterRequisitionAttachment,
  useResubmitRequisition,
  useSubmitRequisition,
  useUploadRequisitionAttachment,
  useVendor,
} from '../../queries';
import { Button, CurrencyInput, Field, FilePicker, Input, PageHeader, Select, cx, LoadingState } from '../../ui';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';
import { brl, brlCents, moneyInput, parseMoneyInput } from '../../lib';
import { useBackNavigation } from '../../nav';
import {
  ESTIMATE_TYPES,
  REQUISITION_TYPES,
  fieldError,
  previewInstallments,
  requisitionSchema,
  resolver,
  type RequisitionValues,
} from '../schemas';
import {
  type Row,
  InstallmentsSubform,
  buildInstallmentsPayload,
  intervalMonthsFor,
  validateInstallments,
} from './installments-subform';
import { type BoletoBlobRef, type Requisition, type RequisitionAttachmentKind } from '../../api';
import { AsideLayout } from '../../components/workflow';
import { RequesterAside } from './requester-aside';
import { currentCompetency, fmtCompetency, shiftCompetency } from './competency';
import { VendorPicker } from '../payables/vendor-picker';

/** Whitelist + limite espelham o backend (`FileContentValidator` + 25 MiB). */
const ACCEPT = '.png,.jpg,.jpeg,.webp,.gif,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.xml';
const MAX_BYTES = 25 * 1024 * 1024;
/** Types the requester picks per file (only `FiscalDocument` satisfies the NF gate). */
const ATTACHMENT_KINDS: RequisitionAttachmentKind[] = ['FiscalDocument', 'BankSlip', 'Invoice', 'Other'];
/** A staged file plus its requester-chosen kind (required before save). */
interface StagedFile {
  file: File;
  kind: RequisitionAttachmentKind | '';
}

/** Prefills the installment subform rows when editing. Must carry every per-installment field —
 * including the PIX key override (AEL-938): otherwise the field opens empty and save wipes the stored key. */
export function installmentsToRows(installments: Requisition['installments']): Row[] {
  return installments.map((i) => ({
    dueDate: i.dueDate,
    amount: String(i.amount),
    paymentMethod: i.paymentMethod,
    boletoDigitableLine: i.boletoDigitableLine ?? '',
    pixKeyOverride: i.pixKeyOverride ?? '',
  }));
}

/** Snapshot da parte NÃO coberta pelo formState.isDirty do react-hook-form: plano financeiro (front-load) e
 * anexos, que vivem em useState. Serializa por valor para detectar alteração e habilitar/travar o envio. */
function serializeLocalState(
  financialOpen: boolean,
  issueDate: string,
  rows: Row[],
  boletoBlobs: BoletoBlobRef[],
  files: StagedFile[],
): string {
  return JSON.stringify({
    financialOpen,
    issueDate,
    rows,
    boletoBlobs,
    files: files.map((f) => [f.file.name, f.file.size, f.kind]),
  });
}

export function NewRequisitionPage() {
  return <RequisitionFormPage />;
}

export function EditRequisitionPage() {
  const { id } = useParams();
  const q = useRequisition(id);
  const existing = q.data;
  if (q.isLoading) return <LoadingState />;
  if (!existing) return null;
  return <RequisitionFormPage initial={existing} />;
}

function RequisitionFormPage({ initial }: { initial?: Requisition }) {
  const { t } = useTranslation();
  const { goBack } = useBackNavigation('/app/requisitions');
  const { empresa, empresaId } = useApp();
  const editing = !!initial;

  if (!empresa) {
    return (
      <PageHeader title={t(editing ? 'requisitions.form.editTitle' : 'requisitions.form.newTitle')}>
        <Button icon={<ChevronLeft />} onClick={goBack}>
          {t('requisitions.back')}
        </Button>
      </PageHeader>
    );
  }

  return <RequisitionForm companyId={empresaId} initial={initial} />;
}

function RequisitionForm({ companyId, initial }: { companyId: string; initial?: Requisition }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const location = useLocation();
  const editing = !!initial;
  // Scoped pickers (AEL-883): a unit-scoped requester only sees the units/cost centers it may open a
  // requisition for (Admin / non-scoped roles get everything — the backend bypasses).
  const unidades = useScopedUnits(companyId).data ?? [];
  const centros = useScopedCostCenters(companyId).data ?? [];
  const projects = useProjects(companyId).data ?? [];

  const [files, setFiles] = useState<StagedFile[]>([]);
  // Boletos read by the AI: already uploaded to storage, registered as attachments by key on save (no re-upload).
  const [boletoBlobs, setBoletoBlobs] = useState<BoletoBlobRef[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const { control, handleSubmit, formState, setValue, getValues, reset } = useForm<RequisitionValues>({
    resolver: resolver(requisitionSchema),
    mode: 'onChange',
    defaultValues: {
      unitId: initial?.unitId ?? '',
      costCenterId: initial?.costCenterId ?? '',
      vendorId: initial?.vendorId ?? '',
      type: initial?.type ?? 'Purchase',
      description: initial?.description ?? '',
      observation: initial?.observation ?? '',
      estimateValue: initial ? String(initial.estimateValue) : '',
      estimateType: initial?.estimateType ?? 'Full',
      startCompetency: initial?.startCompetency ?? currentCompetency(),
      endCompetency: initial?.endCompetency ?? '',
      // Sem data padrão: o solicitante informa o 1º vencimento (obrigatório).
      firstDueDate: initial?.firstDueDate ?? '',
      paymentFrequency: initial?.paymentFrequency ?? '',
      paymentInstallmentsCount: initial?.paymentInstallmentsCount ? String(initial.paymentInstallmentsCount) : '',
      projectId: initial?.projectId ?? null,
    },
  });

  const submitAfterSave = useRef(false);
  const toast = useToast();
  const save = useSaveRequisition();
  const submit = useSubmitRequisition();
  const resubmit = useResubmitRequisition();
  const upload = useUploadRequisitionAttachment();
  const registerAttachment = useRegisterRequisitionAttachment();

  // A unidade deve pertencer ao centro de custo selecionado (AEL-830): filtra opções e limpa a unidade ao trocar o CC.
  const watchedCostCenterId = useWatch({ control, name: 'costCenterId' });
  const selectedCentro = centros.find((c) => c.id === watchedCostCenterId);
  const unitOptions = selectedCentro ? unidades.filter((u) => selectedCentro.unitIds.includes(u.id)) : unidades;
  // The selected unit also scopes the budget aside (AEL-891) — not just the cost center.
  const watchedUnitId = useWatch({ control, name: 'unitId' });
  // Espelho da regra acima: escolhendo a unidade primeiro, só as naturezas registradas nela (unitIds) ficam disponíveis.
  const costCenterOptions = watchedUnitId ? centros.filter((c) => c.unitIds.includes(watchedUnitId)) : centros;

  // Projeto só aparece no tipo "Projeto" (AEL-792). Lista ativos + o vinculado atual (mesmo inativo, ao editar).
  const watchedType = useWatch({ control, name: 'type' });
  const projectOptions = projects.filter((p) => p.status === 'Active' || p.id === initial?.projectId);

  // Conta Fixa (energia/água/etc.): o valor estimado é POR MÊS e o plano é sempre Mensal, sem nº de parcelas
  // (janela aberta), com os meses seguintes lançados como faturas após a aprovação (AEL-877); e o valor varia
  // mês a mês → não trava por orçamento (AEL-881). Espelha a coerção do legado.
  const contaFixa = watchedType === 'FixedExpense';

  // Competência inicial observada (usada no plano mensal e na simulação).
  const startCompetency = useWatch({ control, name: 'startCompetency' });

  // Nº de parcelas só aparece no Mensal (valor que se repete por competência). No Integral o valor é
  // pago inteiro na competência inicial — sem parcelas e sem janela. No Mensal, a competência final é
  // CALCULADA a partir da inicial + nº de parcelas (start + n-1); em branco = recorrência mensal aberta.
  const estimateType = useWatch({ control, name: 'estimateType' });
  const mensal = estimateType === 'Monthly';
  const installmentsRaw = useWatch({ control, name: 'paymentInstallmentsCount' });
  const installmentsCount = Number(installmentsRaw) || 0;
  useEffect(() => {
    if (!mensal) {
      if (getValues('paymentInstallmentsCount')) setValue('paymentInstallmentsCount', '', { shouldValidate: true });
      if (getValues('endCompetency')) setValue('endCompetency', '', { shouldValidate: true });
      return;
    }
    const end = installmentsCount >= 1 ? shiftCompetency(startCompetency, installmentsCount - 1) : '';
    if (getValues('endCompetency') !== end) setValue('endCompetency', end, { shouldValidate: true });
  }, [mensal, installmentsCount, startCompetency, setValue, getValues]);

  // No Mensal, "Valor estimado" é o TOTAL e é DIVIDIDO pelas N parcelas → valor por mês (espelha o
  // Payable: total ÷ parcelas). No Integral (ou Mensal sem parcelas = recorrência aberta) o valor é usado
  // como está. O impacto por competência (BudgetHint + aside) e o valor enviado usam o valor por mês.
  const rawValue = parseMoneyInput(useWatch({ control, name: 'estimateValue' }) ?? '') ?? 0;
  const perMonth = mensal && installmentsCount >= 1 ? rawValue / installmentsCount : rawValue;
  const windowText =
    installmentsCount >= 1
      ? `${fmtCompetency(startCompetency)}–${fmtCompetency(shiftCompetency(startCompetency, installmentsCount - 1))}`
      : '';

  // Front-load da parte financeira: checkbox opcional que revela o editor de parcelas na própria
  // criação/edição de rascunho. Se preenchida, a triagem aprova direto para o Aprovador (pula o Complemento).
  // Ao editar um rascunho que já tem plano, começa aberto e semeado a partir dele.
  const firstDueDate = useWatch({ control, name: 'firstDueDate' });
  // Vendor's registered PIX key for the per-payment override warning (AEL-938); empty until a vendor is picked.
  const watchedVendorId = useWatch({ control, name: 'vendorId' });
  const vendorPixKey = useVendor(watchedVendorId).data?.bankInfo?.pixKey ?? '';
  const [financialOpen, setFinancialOpen] = useState(() => (initial?.installments?.length ?? 0) > 0);
  const [rows, setRows] = useState<Row[]>(() => installmentsToRows(initial?.installments ?? []));
  const [issueDate, setIssueDate] = useState(initial?.issueDate ?? '');
  const [fcShowErrors, setFcShowErrors] = useState(false);

  // "Enviar para triagem" na edição só libera quando não há nada pendente: o formState.isDirty cobre os campos
  // da requisição; este snapshot cobre o plano financeiro e os anexos (useState). savedStateKey é a baseline do
  // último salvamento — atualizada ao salvar para reabilitar o envio.
  const localStateKey = useMemo(
    () => serializeLocalState(financialOpen, issueDate, rows, boletoBlobs, files),
    [financialOpen, issueDate, rows, boletoBlobs, files],
  );
  const savedStateKey = useRef(localStateKey);
  const localDirty = localStateKey !== savedStateKey.current;

  const programmed = useMemo(() => rows.reduce((s, r) => s + (parseMoneyInput(r.amount) ?? 0), 0), [rows]);

  // Com o front-load ligado, a parte financeira do subform é a FONTE DA VERDADE e os campos de pagamento da
  // requisição viram derivados (e ficam disabled na UI): valor estimado = Σ parcelas; modo = Mensal quando há mais
  // de uma parcela, À vista (Integral) quando há uma; nº de parcelas = qtd de parcelas. Conta Fixa é exceção —
  // é sempre Mensal sem nº de parcelas (regra do domínio), então só o valor é sincronizado.
  const rowCount = rows.length;
  // 1º vencimento derivado = a parcela mais antiga (datas 'YYYY-MM-DD' comparam lexicograficamente).
  const earliestDue = useMemo(
    () => rows.reduce((min, r) => (r.dueDate && (!min || r.dueDate < min) ? r.dueDate : min), ''),
    [rows],
  );
  useEffect(() => {
    if (!financialOpen) return;
    const nextValue = programmed > 0 ? moneyInput(programmed) : '';
    if (getValues('estimateValue') !== nextValue) setValue('estimateValue', nextValue, { shouldValidate: true });
    // 1º vencimento segue a parcela mais antiga do plano (campo fica disabled).
    if (earliestDue && getValues('firstDueDate') !== earliestDue)
      setValue('firstDueDate', earliestDue, { shouldValidate: true });
    if (contaFixa) return;
    const nextType = rowCount > 1 ? 'Monthly' : 'Full';
    if (getValues('estimateType') !== nextType) setValue('estimateType', nextType, { shouldValidate: true });
    const nextCount = rowCount > 1 ? String(rowCount) : '';
    if (getValues('paymentInstallmentsCount') !== nextCount)
      setValue('paymentInstallmentsCount', nextCount, { shouldValidate: true });
  }, [financialOpen, programmed, rowCount, earliestDue, contaFixa, setValue, getValues]);

  const seedRowsFromForm = (): Row[] => {
    const total = parseMoneyInput(getValues('estimateValue') ?? '') ?? 0;
    const count = contaFixa ? 1 : installmentsCount >= 1 ? installmentsCount : 1;
    const firstDue = getValues('firstDueDate');
    const interval = intervalMonthsFor(getValues('paymentFrequency') || null);
    if (!firstDue)
      return [{ dueDate: '', amount: total ? moneyInput(total) : '', paymentMethod: 'Pix', boletoDigitableLine: '' }];
    return previewInstallments(total, count, firstDue, interval).map((p) => ({
      dueDate: p.dueDate,
      amount: moneyInput(p.amount),
      paymentMethod: 'Pix',
      boletoDigitableLine: '',
    }));
  };

  const toggleFinancial = (open: boolean) => {
    setFinancialOpen(open);
    if (!open) {
      setRows([]);
      setIssueDate('');
      setBoletoBlobs([]); // drop boletos read for the (now discarded) installments so they aren't attached on save
      setFcShowErrors(false);
      return;
    }
    if (rows.length === 0) setRows(seedRowsFromForm());
  };

  const addFile = (f: File | null) => {
    if (!f) return;
    setErr(null);
    if (f.size > MAX_BYTES) {
      setErr(t('requisitions.attachments.tooLarge'));
      return;
    }
    // XML is always an NF-e/NFS-e — pre-select it; the requester classifies the rest (required).
    const kind: RequisitionAttachmentKind | '' = f.name.toLowerCase().endsWith('.xml') ? 'FiscalDocument' : '';
    setFiles((fs) => [...fs, { file: f, kind }]);
  };

  const onValid = async (v: RequisitionValues) => {
    // Front-load has its own validation (issue + installments + boleto). A draft may hold an incomplete boleto
    // line (the read boleto still attaches); only issue/installments block it. Submit enforces the full plan.
    if (financialOpen) {
      const fv = validateInstallments(rows, issueDate);
      const ok = submitAfterSave.current ? fv.valid : fv.issueValid && fv.rowsValid;
      if (!ok) {
        setFcShowErrors(true);
        return;
      }
    }
    // Mandatory classification: every staged file needs a kind before save (the downstream NF gate
    // depends on it). AI-read boletos already come in as Boleto, so they don't block here.
    if (files.some((f) => !f.kind)) {
      setErr(t('requisitions.attachments.kindRequired'));
      return;
    }
    setBusy(true);
    setErr(null);
    try {
      const saved = await save.mutateAsync({
        id: initial?.id,
        unitId: v.unitId,
        costCenterId: v.costCenterId,
        vendorId: v.vendorId,
        type: v.type,
        description: v.description.trim(),
        observation: v.observation?.trim() || null,
        // EstimateValue é o TOTAL da requisição (o domínio divide por competência/parcela com resto-na-última,
        // preservando o total ao centavo). Com o front-load ligado, o total é a soma das parcelas informadas
        // (o domínio sobrescreve com esse valor); senão, envia o valor digitado tal como está.
        estimateValue: financialOpen ? programmed : (parseMoneyInput(v.estimateValue) ?? 0),
        estimateType: v.estimateType,
        startCompetency: v.startCompetency,
        endCompetency: v.endCompetency || null,
        firstDueDate: v.firstDueDate,
        paymentFrequency: v.paymentFrequency || null,
        paymentInstallmentsCount: v.paymentInstallmentsCount ? Number(v.paymentInstallmentsCount) : null,
        projectId: v.projectId || null,
        // Envia o plano quando o checkbox está ligado; senão null (no update, limpa um plano anterior).
        issueDate: financialOpen ? issueDate : null,
        installments: financialOpen ? buildInstallmentsPayload(rows) : null,
      });
      for (const { file, kind } of files) await upload.mutateAsync({ id: saved.id, file, kind: kind || undefined });
      // Attach each read boleto by key — reuses the blob already uploaded for the parse (no second upload).
      // Registered as Boleto (the reader already knows what they are). Best-effort: the requisition is already
      // saved, so a failed boleto attach must not abort the submit below (which would strand this draft and
      // duplicate on retry). The blob is recoverable via re-read if needed.
      for (const b of boletoBlobs) {
        try {
          await registerAttachment.mutateAsync({ id: saved.id, ...b, kind: 'BankSlip' });
        } catch {
          /* non-fatal: keep going so save + submit complete */
        }
      }
      if (submitAfterSave.current) {
        // Envio: a transição correta depende do estado atual — Complement reenvia (Resubmit); Draft/criação
        // enviam (Submit). FinancialComplement não chega aqui (o botão é escondido; usa o form dedicado).
        if (initial?.status === 'Complement') await resubmit.mutateAsync({ id: saved.id });
        else await submit.mutateAsync({ id: saved.id });
        nav(`/app/requisitions/${saved.id}`, { state: location.state });
        return;
      }
      // Salvar na edição: permanece no formulário e zera as baselines (isDirty do RHF + snapshot local) para
      // reabilitar "Enviar para triagem"; anexos/boletos já foram persistidos, então limpa o staging para não
      // reenviar no envio (e a baseline reflete esse estado: sem arquivos pendentes).
      if (editing) {
        reset(getValues());
        setFiles([]);
        setBoletoBlobs([]);
        savedStateKey.current = serializeLocalState(financialOpen, issueDate, rows, [], []);
        setBusy(false);
        toast.success(t('requisitions.form.draftSaved'));
        return;
      }
      nav(`/app/requisitions/${saved.id}`, { state: location.state });
    } catch (e) {
      setErr(getErrorMessage(e));
      setBusy(false);
    }
  };

  const costCenterName = selectedCentro?.name ?? '';

  return (
    <AsideLayout
      aside={
        <RequesterAside
          empresaId={companyId}
          variant='form'
          selectedCostCenterId={watchedCostCenterId}
          selectedUnitId={watchedUnitId}
          requestValue={perMonth}
        />
      }
    >
      <PageHeader title={t(editing ? 'requisitions.form.editTitle' : 'requisitions.form.newTitle')}>
        <Button
          icon={<ChevronLeft />}
          onClick={() =>
            nav(editing ? `/app/requisitions/${initial.id}` : '/app/requisitions', { state: location.state })
          }
        >
          {t('requisitions.back')}
        </Button>
      </PageHeader>

      <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
        <div className='form'>
          <Field label={t('requisitions.fields.type')} required>
            <Controller
              control={control}
              name='type'
              render={({ field }) => (
                <Segmented
                  value={field.value}
                  onChange={(v) => {
                    field.onChange(v);
                    // Sair do tipo Projeto limpa o vínculo; entrar exige escolher um projeto (validado no schema).
                    if (v !== 'Project') setValue('projectId', null, { shouldValidate: true });
                    // Conta Fixa força Mensal sem parcelas (valor POR MÊS, janela aberta) — coerção do legado.
                    if (v === 'FixedExpense') {
                      setValue('estimateType', 'Monthly', { shouldValidate: true });
                      setValue('paymentInstallmentsCount', '', { shouldValidate: true });
                    }
                  }}
                  options={REQUISITION_TYPES.map((ty) => ({ value: ty, label: t(`requisitions.type.${ty}`) }))}
                />
              )}
            />
          </Field>

          {watchedType === 'Project' && (
            <Field
              label={t('requisitions.fields.project')}
              help={projectOptions.length === 0 ? t('requisitions.fields.noActiveProjects') : undefined}
              required
            >
              <Controller
                control={control}
                name='projectId'
                render={({ field }) => (
                  <Select
                    value={field.value ?? ''}
                    onChange={(e) => field.onChange(e.target.value || null)}
                    disabled={projectOptions.length === 0}
                  >
                    <option value=''>{t('common.select')}</option>
                    {projectOptions.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.projectId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.projectId?.message)}</div>
              )}
            </Field>
          )}

          <Field label={t('requisitions.fields.vendor')} required>
            <Controller
              control={control}
              name='vendorId'
              render={({ field }) => (
                <VendorPicker
                  companyId={companyId}
                  value={field.value || null}
                  onChange={(id) => {
                    field.onChange(id ?? '');
                    // A linha digitável do boleto pertence ao fornecedor — troca de fornecedor a invalida.
                    if (financialOpen) setRows((rs) => rs.map((r) => ({ ...r, boletoDigitableLine: '' })));
                  }}
                  invalid={!!fieldError(formState.errors.vendorId?.message)}
                />
              )}
            />
            {fieldError(formState.errors.vendorId?.message) && (
              <div className='field-err'>{fieldError(formState.errors.vendorId?.message)}</div>
            )}
          </Field>

          <div className='two'>
            <Field label={t('requisitions.fields.unit')} required>
              <Controller
                control={control}
                name='unitId'
                render={({ field }) => (
                  <Select
                    {...field}
                    disabled={editing}
                    onChange={(e) => {
                      field.onChange(e);
                      const cc = centros.find((c) => c.id === getValues('costCenterId'));
                      if (cc && e.target.value && !cc.unitIds.includes(e.target.value)) setValue('costCenterId', '');
                    }}
                  >
                    <option value=''>{t('common.select')}</option>
                    {unitOptions.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.acronym} — {u.name}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.unitId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.unitId?.message)}</div>
              )}
            </Field>

            <Field label={t('requisitions.fields.costCenter')} required>
              <Controller
                control={control}
                name='costCenterId'
                render={({ field }) => (
                  <Select
                    {...field}
                    disabled={editing}
                    onChange={(e) => {
                      field.onChange(e);
                      const cc = centros.find((c) => c.id === e.target.value);
                      const u = getValues('unitId');
                      if (u && cc && !cc.unitIds.includes(u)) setValue('unitId', '');
                    }}
                  >
                    <option value=''>{t('common.select')}</option>
                    {costCenterOptions.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} · {c.name}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.costCenterId?.message) && (
                <div className='field-err'>{fieldError(formState.errors.costCenterId?.message)}</div>
              )}
            </Field>
          </div>

          <Field label={t('requisitions.fields.startCompetency')} required>
            <Controller
              control={control}
              name='startCompetency'
              render={({ field }) => <Input {...field} type='month' />}
            />
            {fieldError(formState.errors.startCompetency?.message) && (
              <div className='field-err'>{fieldError(formState.errors.startCompetency?.message)}</div>
            )}
          </Field>

          <Field label={t('requisitions.fields.estimateValue')} required>
            <div style={{ display: 'flex', gap: 10 }}>
              {/* Com o front-load ligado o valor é derivado (= Σ parcelas) e fica somente-leitura. */}
              {financialOpen ? (
                <Input
                  value={brlCents(programmed)}
                  readOnly
                  aria-label={t('requisitions.fields.estimateValue')}
                  style={{ flex: 1 }}
                />
              ) : (
                <Controller
                  control={control}
                  name='estimateValue'
                  render={({ field }) => (
                    <CurrencyInput
                      value={field.value}
                      onValueChange={field.onChange}
                      onBlur={field.onBlur}
                      name={field.name}
                      style={{ flex: 1 }}
                    />
                  )}
                />
              )}
              {/* Conta Fixa é sempre Mensal (coerção acima) — o toggle some para não sugerir escolha. Com o
                  front-load ligado o modo é derivado das parcelas (Mensal quando 2+, À vista quando 1) e o toggle
                  fica desabilitado. */}
              {!contaFixa && (
                <Controller
                  control={control}
                  name='estimateType'
                  render={({ field }) => (
                    <Segmented
                      variant='toggle'
                      value={field.value}
                      onChange={field.onChange}
                      disabled={financialOpen}
                      options={ESTIMATE_TYPES.map((et) => ({
                        value: et,
                        label: t(`requisitions.estimateTypeShort.${et}`),
                      }))}
                    />
                  )}
                />
              )}
            </div>
            {financialOpen && <div className='budgethint'>{t('requisitions.form.financialDerivedHint')}</div>}
            {contaFixa && <div className='budgethint'>{t('requisitions.form.fixedExpenseHint')}</div>}
            {!financialOpen && fieldError(formState.errors.estimateValue?.message) && (
              <div className='field-err'>{fieldError(formState.errors.estimateValue?.message)}</div>
            )}
          </Field>

          <BudgetHint control={control} costCenterName={costCenterName} value={perMonth} />

          <Field
            label={t('requisitions.fields.firstDueDate')}
            help={t('requisitions.fields.firstDueDateHelp')}
            required
          >
            {/* Com o front-load ligado, o 1º vencimento é derivado da parcela mais antiga e fica disabled. */}
            <Controller
              control={control}
              name='firstDueDate'
              render={({ field }) => <Input {...field} type='date' disabled={financialOpen} />}
            />
            {fieldError(formState.errors.firstDueDate?.message) && (
              <div className='field-err'>{fieldError(formState.errors.firstDueDate?.message)}</div>
            )}
          </Field>

          {mensal && !contaFixa && (
            <Field label={t('requisitions.fields.installments')} help={t('requisitions.form.installmentsHint')}>
              {/* Com o front-load ligado o nº de parcelas é derivado do subform (= qtd de parcelas) e fica disabled. */}
              <Controller
                control={control}
                name='paymentInstallmentsCount'
                render={({ field }) => (
                  <Input {...field} inputMode='numeric' placeholder='—' disabled={financialOpen} />
                )}
              />
              {fieldError(formState.errors.paymentInstallmentsCount?.message) && (
                <div className='field-err'>{fieldError(formState.errors.paymentInstallmentsCount?.message)}</div>
              )}
              {/* Simulação por-mês só quando NÃO front-loaded — com o subform aberto as parcelas já aparecem lá. */}
              {!financialOpen &&
                rawValue > 0 &&
                (installmentsCount >= 1 ? (
                  <div className='budgethint'>
                    {t('requisitions.form.simMonthly', {
                      total: brl(rawValue),
                      n: installmentsCount,
                      perMonth: brl(perMonth),
                      window: windowText,
                    })}
                  </div>
                ) : (
                  <div className='budgethint'>{t('requisitions.form.simOpen', { perMonth: brl(rawValue) })}</div>
                ))}
            </Field>
          )}

          {/* Front-load da parte financeira: checkbox opcional + editor de parcelas compartilhado. */}
          <div style={{ margin: '4px 0' }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 600, cursor: 'pointer' }}>
              <input type='checkbox' checked={financialOpen} onChange={(e) => toggleFinancial(e.target.checked)} />
              {t('requisitions.form.financialToggle')}
            </label>
            <div className='budgethint' style={{ marginTop: 4 }}>
              {t('requisitions.form.financialToggleHint')}
            </div>
          </div>
          {financialOpen && (
            <div className='fc-body'>
              <InstallmentsSubform
                companyId={companyId}
                vendorPixKey={vendorPixKey}
                // The boleto is already in storage (uploaded for the parse); register it by key on save — no re-upload.
                onBoletoBlob={(b) =>
                  setBoletoBlobs((prev) => (prev.some((x) => x.key === b.key) ? prev : [...prev, b]))
                }
                rows={rows}
                onRowsChange={setRows}
                issueDate={issueDate}
                onIssueDateChange={setIssueDate}
                recurring={contaFixa}
                firstDueDate={firstDueDate}
                fallbackTotal={parseMoneyInput(getValues('estimateValue') ?? '') ?? 0}
                showErrors={fcShowErrors}
              />
              {fcShowErrors && !validateInstallments(rows, issueDate).valid && (
                <div className='field-err' style={{ marginTop: 8 }}>
                  {t('requisitions.financialComplement.rowsInvalid')}
                </div>
              )}
            </div>
          )}

          <Field label={t('requisitions.fields.description')} required>
            <Controller
              control={control}
              name='description'
              render={({ field }) => (
                <textarea
                  {...field}
                  className='input'
                  rows={3}
                  style={{ resize: 'vertical' }}
                  placeholder={t('requisitions.fields.descriptionPlaceholder')}
                />
              )}
            />
            {fieldError(formState.errors.description?.message) && (
              <div className='field-err'>{fieldError(formState.errors.description?.message)}</div>
            )}
          </Field>

          <Field label={t('requisitions.attachments.title')}>
            <FilePicker large accept={ACCEPT} placeholder={t('requisitions.form.attachDropzone')} onSelect={addFile} />
            {(files.length > 0 || boletoBlobs.length > 0) && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 6 }}>
                {/* Boletos read via "Ler PDF do boleto" — already uploaded, registered by key on save. Shown here
                    so the read PDF is visibly an attachment even while the linha digitável still needs fixing. */}
                {boletoBlobs.map((b) => (
                  <div key={b.key} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                    <Paperclip size={14} />
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {b.fileName}
                    </span>
                    <span className='badge-status badge-remessa' style={{ fontSize: 10 }}>
                      {t('requisitions.attachments.fromBoleto')}
                    </span>
                    <button
                      type='button'
                      className='icon-btn'
                      aria-label={t('requisitions.attachments.remove')}
                      title={t('requisitions.attachments.remove')}
                      onClick={() => setBoletoBlobs((bs) => bs.filter((x) => x.key !== b.key))}
                    >
                      <X size={14} />
                    </button>
                  </div>
                ))}
                {files.map((f, i) => (
                  <div
                    key={`${f.file.name}-${i}`}
                    style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}
                  >
                    <Paperclip size={14} />
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {f.file.name}
                    </span>
                    {/* Per-file kind (mandatory): only FiscalDocument satisfies the NF gate in Tax/Accounting. */}
                    <Select
                      value={f.kind}
                      aria-label={t('requisitions.attachments.kindLabel')}
                      style={{ width: 150 }}
                      onChange={(e) =>
                        setFiles((fs) =>
                          fs.map((x, j) => (j === i ? { ...x, kind: e.target.value as RequisitionAttachmentKind } : x)),
                        )
                      }
                    >
                      <option value=''>{t('requisitions.attachments.kindPlaceholder')}</option>
                      {ATTACHMENT_KINDS.map((k) => (
                        <option key={k} value={k}>
                          {t(`requisitions.attachments.kinds.${k}`)}
                        </option>
                      ))}
                    </Select>
                    <button
                      type='button'
                      className='icon-btn'
                      aria-label={t('requisitions.attachments.remove')}
                      title={t('requisitions.attachments.remove')}
                      onClick={() => setFiles((fs) => fs.filter((_, j) => j !== i))}
                    >
                      <X size={14} />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </Field>
        </div>

        <div className='form-legend'>{t('common.requiredLegend')}</div>

        {err && <div className='field-err'>{err}</div>}

        <div className='formfoot'>
          <Button type='button' onClick={() => nav('/app/requisitions')}>
            {t('common.cancel')}
          </Button>
          <Button type='submit' disabled={busy} onClick={() => (submitAfterSave.current = false)}>
            {busy ? t('common.saving') : t('requisitions.form.saveDraft')}
          </Button>
          {/* FinancialComplement envia pelo form dedicado (FinancialComplementForm) nos detalhes, não aqui. */}
          {!(editing && initial?.status === 'FinancialComplement') && (
            <Button
              type='submit'
              variant='brand'
              icon={<SendHorizonal />}
              // Na edição, força salvar antes de enviar: desabilita enquanto houver alterações não salvas — nos
              // campos da requisição (isDirty) ou no plano financeiro/anexos (localDirty).
              disabled={busy || (editing && (formState.isDirty || localDirty))}
              onClick={() => (submitAfterSave.current = true)}
            >
              {t('requisitions.form.sendToTriage')}
            </Button>
          )}
        </div>
        {/* Feedback claro ao clicar com campos obrigatórios em branco (abaixo dos botões). */}
        {formState.submitCount > 0 && Object.keys(formState.errors).length > 0 && (
          <div className='field-err' style={{ marginTop: 8 }}>
            {t('common.requiredMissing')}
          </div>
        )}
      </form>
    </AsideLayout>
  );
}

/** Single-select control: 'seg' (pills, for Tipo) or 'toggle' (connected pair, for Mensal/Integral).
 * `disabled` deixa o controle somente-leitura (usado quando o modo é derivado do front-load). */
function Segmented<T extends string>({
  value,
  onChange,
  options,
  variant = 'seg',
  disabled = false,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: string }[];
  variant?: 'seg' | 'toggle';
  disabled?: boolean;
}) {
  const wrap = variant === 'toggle' ? 'toggle' : 'seg';
  const btn = variant === 'toggle' ? 'toggle-btn' : 'segbtn';
  return (
    <div className={wrap} style={disabled ? { opacity: 0.6 } : undefined}>
      {options.map((o) => (
        <button
          key={o.value}
          type='button'
          className={cx(btn, value === o.value && 'on')}
          aria-pressed={value === o.value}
          disabled={disabled}
          style={disabled ? { cursor: 'not-allowed' } : undefined}
          onClick={() => onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

/** Inline "impacto no orçamento" hint (like the artifact's budgethint): compares the per-competency value
 * (already divided by installments for Monthly) against the selected dimension's available ceiling. */
function BudgetHint({
  control,
  costCenterName,
  value,
}: {
  control: Control<RequisitionValues>;
  costCenterName: string;
  value: number;
}) {
  const { t } = useTranslation();
  const unitId = useWatch({ control, name: 'unitId' });
  const costCenterId = useWatch({ control, name: 'costCenterId' });
  const competency = useWatch({ control, name: 'startCompetency' });

  const ready = !!unitId && !!costCenterId && !!competency && value > 0;
  const q = useDimensionBudgetControl({ costCenterId, unitId, competencyFrom: competency }, { enabled: ready });

  if (!ready) return <div className='budgethint'>{t('requisitions.form.budgetHintEmpty')}</div>;
  if (q.isLoading || !q.data) return <LoadingState />;

  const month = q.data.months.find((m) => m.competency === competency) ?? q.data.months[0];
  if (!month?.budgeted) return <div className='budgethint'>{t('requisitions.form.budgetHintNoBudget')}</div>;

  const available = month.available;
  const over = value > available;
  const name = costCenterName || t('requisitions.fields.costCenter');
  return (
    <div className={cx('budgethint', over && 'bad')}>
      {over
        ? t('requisitions.form.budgetHintOver', { name, over: brl(value - available), available: brl(available) })
        : t('requisitions.form.budgetHintOk', { name, available: brl(available), left: brl(available - value) })}
    </div>
  );
}
