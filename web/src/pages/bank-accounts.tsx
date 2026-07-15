import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Plus, Landmark, Pencil, Trash2 } from 'lucide-react';
import { type BankAccount, type BankAccountInput, type BankAccountUpdateInput, type PaymentMethod } from '../api';
import { useApp } from '../store';
import { useBankAccounts, useDeleteBankAccount, useLedgerAccounts, useSaveBankAccount } from '../queries';
import { getErrorMessage } from '../apiError';
import {
  Button,
  Input,
  Select,
  Field,
  PageHeader,
  Panel,
  EmptyState,
  Drawer,
  ConfirmDialog,
  Combobox,
  type ComboboxItem,
} from '../ui';
import { P, usePermissions } from '../permissions';
import { EmpresaPicker } from './shared';
import { resolver, fieldError, bankAccountSchema, PAYMENT_METHODS, type BankAccountValues } from './schemas';

/* ============ Create / edit form (Drawer body) ============ */
function BankAccountForm({
  companyId,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  initial?: BankAccount;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const { control, handleSubmit, watch, setValue, formState } = useForm<BankAccountValues>({
    resolver: resolver(bankAccountSchema),
    mode: 'onChange',
    defaultValues: {
      bankName: initial?.bankName ?? '',
      bankCode: initial?.bankCode ?? '',
      agency: initial?.agency ?? '',
      accountNumber: initial?.accountNumber ?? '',
      accountDigit: initial?.accountDigit ?? '',
      accountType: initial?.accountType ?? 'Checking',
      alias: initial?.alias ?? '',
      openingBalance: initial?.openingBalance != null ? String(initial.openingBalance) : '',
      isDefaultForPayments: initial?.isDefaultForPayments ?? false,
      defaultPaymentMethod: initial?.defaultPaymentMethod ?? null,
      cnabAgreementCode: initial?.cnabAgreementCode ?? '',
      ledgerAccountId: initial?.ledgerAccountId ?? null,
    },
  });
  const isDefault = watch('isDefaultForPayments');
  // Only analytical, active accounts can receive a posting; mirrors the payable classification picker.
  const ledgerAccounts = useLedgerAccounts({ kind: 'Analytical', activeOnly: true }).data ?? [];
  const ledgerItems: ComboboxItem[] = ledgerAccounts.map((a) => ({ id: a.id, label: a.name, sub: a.code }));
  const ledgerAccountId = watch('ledgerAccountId');
  // Required on create (UI gate); optional when editing an existing account.
  const ledgerMissingOnCreate = !initial && !ledgerAccountId;

  const save = useSaveBankAccount({ onSuccess: () => onSaved() });
  const erro = save.error ? getErrorMessage(save.error) : null;

  const onValid = (v: BankAccountValues) => {
    const openingBalance = v.openingBalance.trim() === '' ? null : Number(v.openingBalance);
    const defaultPaymentMethod: PaymentMethod | null = v.isDefaultForPayments
      ? (v.defaultPaymentMethod ?? 'Pix')
      : null;
    const cnabAgreementCode = v.cnabAgreementCode.trim() || null;
    if (initial) {
      const body: BankAccountUpdateInput = {
        bankCode: v.bankCode.trim(),
        bankName: v.bankName.trim(),
        agency: v.agency.trim(),
        accountNumber: v.accountNumber.trim(),
        accountDigit: v.accountDigit.trim(),
        accountType: v.accountType,
        alias: v.alias.trim(),
        openingBalance,
        active: initial.active,
        isDefaultForPayments: v.isDefaultForPayments,
        defaultPaymentMethod,
        cnabAgreementCode,
        ledgerAccountId: v.ledgerAccountId,
      };
      save.mutate({ id: initial.id, update: body });
    } else {
      const body: BankAccountInput = {
        companyId,
        bankCode: v.bankCode.trim(),
        bankName: v.bankName.trim(),
        agency: v.agency.trim(),
        accountNumber: v.accountNumber.trim(),
        accountDigit: v.accountDigit.trim(),
        accountType: v.accountType,
        alias: v.alias.trim(),
        openingBalance,
        isDefaultForPayments: v.isDefaultForPayments,
        defaultPaymentMethod,
        cnabAgreementCode,
        ledgerAccountId: v.ledgerAccountId,
      };
      save.mutate({ create: body });
    }
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <Field label={t('bankAccounts.fields.alias')}>
        <Controller
          control={control}
          name='alias'
          render={({ field }) => <Input autoFocus {...field} placeholder={t('bankAccounts.placeholders.alias')} />}
        />
        {fieldError(formState.errors.alias?.message) && (
          <div className='field-err'>{fieldError(formState.errors.alias?.message)}</div>
        )}
      </Field>
      <div className='edit-grid' style={{ marginTop: 14 }}>
        <Field label={t('bankAccounts.fields.bankName')}>
          <Controller
            control={control}
            name='bankName'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.bankName')} />}
          />
          {fieldError(formState.errors.bankName?.message) && (
            <div className='field-err'>{fieldError(formState.errors.bankName?.message)}</div>
          )}
        </Field>
        <Field label={t('bankAccounts.fields.bankCode')}>
          <Controller
            control={control}
            name='bankCode'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.bankCode')} />}
          />
          {fieldError(formState.errors.bankCode?.message) && (
            <div className='field-err'>{fieldError(formState.errors.bankCode?.message)}</div>
          )}
        </Field>
        <Field label={t('bankAccounts.fields.agency')}>
          <Controller
            control={control}
            name='agency'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.agency')} />}
          />
          {fieldError(formState.errors.agency?.message) && (
            <div className='field-err'>{fieldError(formState.errors.agency?.message)}</div>
          )}
        </Field>
        <Field label={t('bankAccounts.fields.accountType')}>
          <Controller
            control={control}
            name='accountType'
            render={({ field }) => (
              <Select {...field}>
                <option value='Checking'>{t('bankAccounts.types.Checking')}</option>
                <option value='Savings'>{t('bankAccounts.types.Savings')}</option>
              </Select>
            )}
          />
        </Field>
        <Field label={t('bankAccounts.fields.accountNumber')}>
          <Controller
            control={control}
            name='accountNumber'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.accountNumber')} />}
          />
          {fieldError(formState.errors.accountNumber?.message) && (
            <div className='field-err'>{fieldError(formState.errors.accountNumber?.message)}</div>
          )}
        </Field>
        <Field label={t('bankAccounts.fields.accountDigit')}>
          <Controller
            control={control}
            name='accountDigit'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.accountDigit')} />}
          />
        </Field>
        <Field label={t('bankAccounts.fields.openingBalance')}>
          <Controller
            control={control}
            name='openingBalance'
            render={({ field }) => (
              <Input {...field} inputMode='numeric' placeholder={t('bankAccounts.placeholders.openingBalance')} />
            )}
          />
          {fieldError(formState.errors.openingBalance?.message) && (
            <div className='field-err'>{fieldError(formState.errors.openingBalance?.message)}</div>
          )}
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('bankAccounts.fields.cnabAgreementCode')} help={t('bankAccounts.fields.cnabAgreementCodeHelp')}>
          <Controller
            control={control}
            name='cnabAgreementCode'
            render={({ field }) => <Input {...field} placeholder={t('bankAccounts.placeholders.cnabAgreementCode')} />}
          />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label={t('bankAccounts.fields.ledgerAccount')} help={t('bankAccounts.fields.ledgerAccountHelp')}>
          <Controller
            control={control}
            name='ledgerAccountId'
            render={({ field }) => <Combobox value={field.value} onChange={field.onChange} items={ledgerItems} />}
          />
          {ledgerMissingOnCreate && <div className='field-err'>{t('bankAccounts.fields.ledgerAccountRequired')}</div>}
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Controller
          control={control}
          name='isDefaultForPayments'
          render={({ field }) => (
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
              <input
                type='checkbox'
                checked={field.value}
                onChange={(e) => {
                  field.onChange(e.target.checked);
                  if (e.target.checked) {
                    // Pre-select Pix when toggled on so the form stays valid
                    setValue('defaultPaymentMethod', 'Pix', { shouldValidate: true });
                  } else {
                    setValue('defaultPaymentMethod', null, { shouldValidate: true });
                  }
                }}
              />
              {t('bankAccounts.fields.isDefaultForPayments')}
            </label>
          )}
        />
        {isDefault && (
          <div style={{ marginTop: 10 }}>
            <Field label={t('bankAccounts.fields.defaultPaymentMethod')}>
              <Controller
                control={control}
                name='defaultPaymentMethod'
                render={({ field }) => (
                  <Select {...field} value={field.value ?? ''} onChange={(e) => field.onChange(e.target.value || null)}>
                    {PAYMENT_METHODS.map((m) => (
                      <option key={m} value={m}>
                        {t(`payments.methods.${m}`)}
                      </option>
                    ))}
                  </Select>
                )}
              />
              {fieldError(formState.errors.defaultPaymentMethod?.message) && (
                <div className='field-err'>{fieldError(formState.errors.defaultPaymentMethod?.message)}</div>
              )}
            </Field>
          </div>
        )}
      </div>
      {erro && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {erro}
        </div>
      )}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button type='submit' variant='brand' disabled={!formState.isValid || save.isPending || ledgerMissingOnCreate}>
          {save.isPending ? t('forms.actions.saving') : t('forms.actions.save')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}

/* ============ List page ============ */
export function BankAccountsPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();
  const { can } = usePermissions();
  const list = useBankAccounts(empresaId, true);
  const contas = list.data ?? [];

  const remove = useDeleteBankAccount();
  const save = useSaveBankAccount();

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<BankAccount | null>(null);
  const [toDelete, setToDelete] = useState<BankAccount | null>(null);
  const [deleteErr, setDeleteErr] = useState<string | null>(null);

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (c: BankAccount) => {
    setEditing(c);
    setFormOpen(true);
  };

  const toggleActive = (c: BankAccount) =>
    save.mutate({
      id: c.id,
      update: {
        bankCode: c.bankCode,
        bankName: c.bankName,
        agency: c.agency,
        accountNumber: c.accountNumber,
        accountDigit: c.accountDigit,
        accountType: c.accountType,
        alias: c.alias,
        openingBalance: c.openingBalance,
        active: !c.active,
        isDefaultForPayments: c.isDefaultForPayments,
        defaultPaymentMethod: c.defaultPaymentMethod ?? null,
        // This handler rebuilds the whole payload; carry these through or they'd be cleared on toggle.
        cnabAgreementCode: c.cnabAgreementCode ?? null,
        ledgerAccountId: c.ledgerAccountId ?? null,
      },
    });

  const submitDelete = () => {
    if (!toDelete) return;
    setDeleteErr(null);
    remove.mutate(
      { id: toDelete.id },
      {
        onSuccess: () => setToDelete(null),
        onError: (e) => setDeleteErr(getErrorMessage(e)),
      },
    );
  };

  return (
    <>
      <PageHeader title={t('bankAccounts.title')} sub={t('bankAccounts.subtitle')}>
        <EmpresaPicker />
        {can(P.treasuryWrite) && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={openCreate}>
            {t('bankAccounts.new')}
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Landmark />}
          title={t('bankAccounts.empty.noCompanyTitle')}
          sub={t('bankAccounts.empty.noCompanySub')}
        />
      ) : contas.length === 0 ? (
        <EmptyState icon={<Landmark />} title={t('bankAccounts.empty.title')} sub={t('bankAccounts.empty.sub')} />
      ) : (
        <Panel title={t('bankAccounts.count', { count: contas.length })}>
          <table>
            <thead>
              <tr>
                <th>{t('bankAccounts.fields.alias')}</th>
                <th>{t('bankAccounts.fields.bankName')}</th>
                <th>{t('bankAccounts.fields.agency')}</th>
                <th>{t('bankAccounts.fields.accountNumber')}</th>
                <th>{t('bankAccounts.fields.accountType')}</th>
                <th>Status</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {contas.map((c) => (
                <tr key={c.id}>
                  <td>{c.alias}</td>
                  <td>
                    {c.bankCode ? `${c.bankCode} · ` : ''}
                    {c.bankName}
                  </td>
                  <td className='t-mono'>{c.agency}</td>
                  <td className='t-mono'>
                    {c.accountNumber}
                    {c.accountDigit ? `-${c.accountDigit}` : ''}
                  </td>
                  <td>{t(`bankAccounts.types.${c.accountType}`)}</td>
                  <td>
                    <span className={`badge-status ${c.active ? 'badge-aprovado' : 'badge-rejeitado'}`}>
                      {c.active ? t('bankAccounts.active') : t('bankAccounts.inactive')}
                    </span>
                  </td>
                  <td className='td-action'>
                    {can(P.treasuryWrite) && (
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button icon={<Pencil />} onClick={() => openEdit(c)}>
                          {t('bankAccounts.edit')}
                        </Button>
                        <Button onClick={() => toggleActive(c)}>
                          {c.active ? t('bankAccounts.deactivate') : t('bankAccounts.activate')}
                        </Button>
                        <Button
                          icon={<Trash2 />}
                          onClick={() => {
                            setDeleteErr(null);
                            setToDelete(c);
                          }}
                        >
                          {t('bankAccounts.delete')}
                        </Button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {save.error ? <p className='create-err'>{getErrorMessage(save.error)}</p> : null}
        </Panel>
      )}

      <Drawer
        open={formOpen}
        title={editing ? t('bankAccounts.editTitle') : t('bankAccounts.newTitle')}
        onClose={() => setFormOpen(false)}
      >
        {formOpen && (
          <BankAccountForm
            companyId={empresaId}
            initial={editing ?? undefined}
            onSaved={() => setFormOpen(false)}
            onCancel={() => setFormOpen(false)}
          />
        )}
      </Drawer>

      {toDelete && (
        <ConfirmDialog
          title={t('bankAccounts.deleteTitle')}
          danger
          busy={remove.isPending}
          confirmLabel={t('bankAccounts.delete')}
          message={
            <div>
              <div>{t('bankAccounts.deleteMessage', { name: toDelete.alias })}</div>
              {deleteErr && (
                <div className='create-err' style={{ marginTop: 8 }}>
                  {deleteErr}
                </div>
              )}
            </div>
          }
          onConfirm={submitDelete}
          onCancel={() => setToDelete(null)}
        />
      )}
    </>
  );
}
