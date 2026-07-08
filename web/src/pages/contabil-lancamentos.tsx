import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Controller, useFieldArray, useForm } from 'react-hook-form';
import { BookOpen, Plus, Trash2, Undo2, CheckCircle2, Pencil } from 'lucide-react';
import { type JournalEntry, type JournalStatus, type LedgerAccount } from '../api';
import {
  useCreateJournalEntry,
  useJournalEntries,
  useLedgerAccounts,
  usePostJournalEntry,
  useReverseJournalEntry,
  useUpdateJournalEntry,
} from '../queries';
import { useCostCenters } from '../queries';
import { useApp } from '../store';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { brlCents } from '../lib';
import { Button, Combobox, Drawer, EmptyState, Field, Input, PageHeader, Panel, Select, ToolbarField } from '../ui';
import { EmpresaPicker } from './shared';
import { fieldError, journalEntrySchema, resolver, type JournalEntryValues } from './schemas';
import { fmtDate } from '../datetime';

const STATUSES: JournalStatus[] = ['Draft', 'Posted', 'Reversed'];

function statusBadge(status: JournalStatus) {
  return status === 'Posted' ? 'badge-aprovado' : status === 'Reversed' ? 'badge-rejeitado' : 'badge-pendente';
}

/* ============ Draft create/edit form (multi-line debit/credit editor) ============ */
function JournalEntryForm({
  companyId,
  accounts,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  accounts: LedgerAccount[];
  initial?: JournalEntry;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const costCenters = useCostCenters(companyId);

  // Only active analytical accounts may receive entries.
  const accountItems = useMemo(
    () =>
      accounts
        .filter((a) => a.kind === 'Analytical' && a.isActive)
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((a) => ({ id: a.id, label: `${a.code} · ${a.name}` })),
    [accounts],
  );
  const ccItems = useMemo(
    () => (costCenters.data ?? []).map((c) => ({ id: c.id, label: `${c.code} — ${c.name}` })),
    [costCenters.data],
  );

  const create = useCreateJournalEntry({ onSuccess: () => onSaved() });
  const update = useUpdateJournalEntry({ onSuccess: () => onSaved() });
  const busy = create.isPending || update.isPending;
  const mutErr = create.error ? getErrorMessage(create.error) : update.error ? getErrorMessage(update.error) : null;

  const {
    register,
    control,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<JournalEntryValues>({
    resolver: resolver(journalEntrySchema),
    defaultValues: initial
      ? {
          competenceDate: initial.competenceDate,
          description: initial.description,
          sourceReference: initial.sourceReference ?? '',
          lines: initial.lines.map((l) => ({
            ledgerAccountId: l.ledgerAccountId,
            side: l.side,
            amount: String(l.amount),
            costCenterId: l.costCenterId ?? '',
            description: l.description ?? '',
          })),
        }
      : {
          competenceDate: new Date().toISOString().slice(0, 10),
          description: '',
          sourceReference: '',
          lines: [
            { ledgerAccountId: '', side: 'Debit', amount: '', costCenterId: '', description: '' },
            { ledgerAccountId: '', side: 'Credit', amount: '', costCenterId: '', description: '' },
          ],
        },
  });

  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });
  const lines = watch('lines');

  // NOTE: `watch('lines')` returns a reference that react-hook-form mutates in
  // place across renders, so memoizing on `[lines]` would never recompute (the
  // identity is stable while the contents change). The sum is trivial — compute
  // it on every render so the debit/credit totals and the balanced state stay
  // live as the user edits amounts.
  const totals = (() => {
    let debit = 0;
    let credit = 0;
    for (const l of lines ?? []) {
      const amount = Number(l.amount) || 0;
      if (l.side === 'Debit') debit += amount;
      else credit += amount;
    }
    return { debit, credit, diff: Math.round((debit - credit) * 100) / 100 };
  })();
  const balanced = totals.diff === 0 && totals.debit > 0;

  const onSubmit = (v: JournalEntryValues) => {
    const body = {
      competenceDate: v.competenceDate,
      description: v.description.trim(),
      sourceReference: v.sourceReference.trim() || null,
      lines: v.lines.map((l) => ({
        ledgerAccountId: l.ledgerAccountId,
        side: l.side,
        amount: Number(l.amount),
        costCenterId: l.costCenterId || null,
        description: l.description.trim() || null,
      })),
    };
    if (initial) update.mutate({ id: initial.id, body });
    else create.mutate({ companyId, ...body });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(onSubmit)(e)} noValidate>
      <div className='edit-grid'>
        <Field label={t('accounting.journal.fields.competenceDate')} help={fieldError(errors.competenceDate?.message)}>
          <Input type='date' {...register('competenceDate')} />
        </Field>
        <Field label={t('accounting.journal.fields.sourceReference')}>
          <Input {...register('sourceReference')} />
        </Field>
      </div>
      <Field label={t('accounting.journal.fields.description')} help={fieldError(errors.description?.message)}>
        <Input {...register('description')} />
      </Field>

      <table className='journal-lines'>
        <thead>
          <tr>
            <th>{t('accounting.journal.fields.account')}</th>
            <th>{t('accounting.journal.fields.side')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.journal.fields.amount')}</th>
            <th>{t('accounting.journal.fields.costCenter')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {fields.map((f, i) => (
            <tr key={f.id}>
              <td>
                <Controller
                  control={control}
                  name={`lines.${i}.ledgerAccountId`}
                  render={({ field }) => (
                    <Combobox
                      value={field.value || null}
                      onChange={(id) => field.onChange(id ?? '')}
                      items={accountItems}
                      invalid={!!errors.lines?.[i]?.ledgerAccountId}
                    />
                  )}
                />
              </td>
              <td>
                <Select {...register(`lines.${i}.side`)}>
                  <option value='Debit'>{t('accounting.sides.Debit')}</option>
                  <option value='Credit'>{t('accounting.sides.Credit')}</option>
                </Select>
              </td>
              <td>
                <Input
                  type='number'
                  step='0.01'
                  min='0'
                  style={{ textAlign: 'right' }}
                  {...register(`lines.${i}.amount`)}
                />
              </td>
              <td>
                <Controller
                  control={control}
                  name={`lines.${i}.costCenterId`}
                  render={({ field }) => (
                    <Combobox
                      value={field.value || null}
                      onChange={(id) => field.onChange(id ?? '')}
                      items={ccItems}
                      placeholder={t('accounting.journal.noCostCenter')}
                    />
                  )}
                />
              </td>
              <td>
                <Button
                  type='button'
                  icon={<Trash2 />}
                  disabled={fields.length <= 2}
                  onClick={() => remove(i)}
                  aria-label={t('accounting.journal.removeLine')}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <Button
        type='button'
        icon={<Plus />}
        onClick={() => append({ ledgerAccountId: '', side: 'Debit', amount: '', costCenterId: '', description: '' })}
      >
        {t('accounting.journal.addLine')}
      </Button>

      <div className='journal-totals'>
        <div>
          <span>{t('accounting.journal.totalDebit')}</span>
          <strong className='t-mono'>{brlCents(totals.debit)}</strong>
        </div>
        <div>
          <span>{t('accounting.journal.totalCredit')}</span>
          <strong className='t-mono'>{brlCents(totals.credit)}</strong>
        </div>
        <div>
          <span className={`badge-status ${balanced ? 'badge-aprovado' : 'badge-rejeitado'}`}>
            {balanced ? t('accounting.journal.balanced') : t('accounting.journal.unbalanced')}
          </span>
        </div>
      </div>

      {errors.lines?.message && <div className='create-err'>{fieldError(errors.lines.message)}</div>}
      {typeof errors.lines?.root?.message === 'string' && (
        <div className='create-err'>{fieldError(errors.lines.root.message)}</div>
      )}
      {mutErr && <div className='create-err'>{mutErr}</div>}

      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button type='submit' variant='brand' disabled={busy || !balanced}>
          {busy ? t('forms.actions.saving') : t('forms.actions.save')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}

/* ============ Posted/Reversed read-only detail ============ */
function JournalEntryDetail({ entry, accounts }: { entry: JournalEntry; accounts: LedgerAccount[] }) {
  const { t } = useTranslation();
  const nameOf = useMemo(() => {
    const map = new Map(accounts.map((a) => [a.id, `${a.code} · ${a.name}`]));
    return (id: string) => map.get(id) ?? id;
  }, [accounts]);

  return (
    <div>
      <p className='help'>{t('accounting.journal.readOnlyHint')}</p>
      {entry.reversalOfEntryId && (
        <p className='help'>{t('accounting.journal.reversalOf', { number: entry.number ?? '—' })}</p>
      )}
      <table>
        <thead>
          <tr>
            <th>{t('accounting.journal.fields.account')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.journal.columns.debit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.journal.columns.credit')}</th>
          </tr>
        </thead>
        <tbody>
          {entry.lines.map((l) => (
            <tr key={l.id}>
              <td>{nameOf(l.ledgerAccountId)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {l.side === 'Debit' ? brlCents(l.amount) : ''}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {l.side === 'Credit' ? brlCents(l.amount) : ''}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td style={{ fontWeight: 600 }}>{t('accounting.journal.totalDebit')}</td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(entry.totalDebit)}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(entry.totalCredit)}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

/* ============ Reverse dialog ============ */
function ReverseForm({ entry, onDone, onCancel }: { entry: JournalEntry; onDone: () => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const [reason, setReason] = useState('');
  const reverse = useReverseJournalEntry({
    onSuccess: () => {
      toast.success(t('accounting.journal.reversed'));
      onDone();
    },
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        if (reason.trim()) reverse.mutate({ id: entry.id, reason: reason.trim() });
      }}
    >
      <p className='help'>{t('accounting.journal.reverseMessage')}</p>
      <Field label={t('accounting.journal.reverseReason')}>
        <Input autoFocus value={reason} onChange={(e) => setReason(e.target.value)} />
      </Field>
      {reverse.error ? <div className='create-err'>{getErrorMessage(reverse.error)}</div> : null}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button type='submit' variant='brand' disabled={!reason.trim() || reverse.isPending}>
          {t('accounting.journal.reverse')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}

/* ============ Page ============ */
export function ContabilLancamentosPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { empresa, empresaId } = useApp();
  const [status, setStatus] = useState<JournalStatus | ''>('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const list = useJournalEntries(
    { companyId: empresaId || undefined, status: status || undefined, from: from || undefined, to: to || undefined },
    { enabled: !!empresaId },
  );
  const accountsQuery = useLedgerAccounts({});
  const accounts = accountsQuery.data ?? [];
  const entries = list.data ?? [];

  const post = usePostJournalEntry({
    onSuccess: () => toast.success(t('accounting.journal.posted')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<JournalEntry | null>(null);
  const [viewing, setViewing] = useState<JournalEntry | null>(null);
  const [reversing, setReversing] = useState<JournalEntry | null>(null);

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };

  return (
    <>
      <PageHeader title={t('accounting.journal.title')} sub={t('accounting.journal.subtitle')}>
        <EmpresaPicker />
        {empresa && (
          <Button variant='brand' icon={<Plus />} onClick={openCreate}>
            {t('accounting.journal.new')}
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<BookOpen />}
          title={t('accounting.journal.empty.noCompanyTitle')}
          sub={t('accounting.journal.empty.noCompanySub')}
        />
      ) : (
        <Panel
          title={t('accounting.journal.count', { count: entries.length })}
          action={
            <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end' }}>
              <ToolbarField label={t('accounting.journal.filters.status')}>
                <Select value={status} onChange={(e) => setStatus(e.target.value as JournalStatus | '')}>
                  <option value=''>{t('accounting.journal.filters.all')}</option>
                  {STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {t(`accounting.status.${s}`)}
                    </option>
                  ))}
                </Select>
              </ToolbarField>
              <ToolbarField label={t('accounting.journal.filters.from')}>
                <Input type='date' value={from} onChange={(e) => setFrom(e.target.value)} />
              </ToolbarField>
              <ToolbarField label={t('accounting.journal.filters.to')}>
                <Input type='date' value={to} onChange={(e) => setTo(e.target.value)} />
              </ToolbarField>
            </div>
          }
        >
          {list.isLoading ? (
            <div className='help'>{t('accounting.journal.loading')}</div>
          ) : entries.length === 0 ? (
            <div className='empty-state' style={{ padding: 24 }}>
              <div className='t'>{t('accounting.journal.empty.title')}</div>
              <div className='s'>{t('accounting.journal.empty.sub')}</div>
            </div>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>{t('accounting.journal.columns.number')}</th>
                  <th>{t('accounting.journal.columns.date')}</th>
                  <th>{t('accounting.journal.columns.description')}</th>
                  <th>{t('accounting.journal.columns.status')}</th>
                  <th style={{ textAlign: 'right' }}>{t('accounting.journal.columns.debit')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {entries.map((e) => (
                  <tr key={e.id}>
                    <td className='t-mono'>{e.number ?? '—'}</td>
                    <td className='t-mono'>{fmtDate(e.competenceDate)}</td>
                    <td>{e.description}</td>
                    <td>
                      <span className={`badge-status ${statusBadge(e.status)}`}>
                        {t(`accounting.status.${e.status}`)}
                      </span>
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brlCents(e.totalDebit)}
                    </td>
                    <td className='td-action'>
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        {e.status === 'Draft' ? (
                          <>
                            <Button
                              icon={<Pencil />}
                              onClick={() => {
                                setEditing(e);
                                setFormOpen(true);
                              }}
                            >
                              {t('accounting.journal.edit')}
                            </Button>
                            <Button
                              icon={<CheckCircle2 />}
                              disabled={post.isPending}
                              onClick={() => post.mutate({ id: e.id })}
                            >
                              {t('accounting.journal.post')}
                            </Button>
                          </>
                        ) : (
                          <>
                            <Button onClick={() => setViewing(e)}>{t('accounting.journal.view')}</Button>
                            {e.status === 'Posted' && !e.reversedByEntryId && (
                              <Button icon={<Undo2 />} onClick={() => setReversing(e)}>
                                {t('accounting.journal.reverse')}
                              </Button>
                            )}
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
      )}

      <Drawer
        open={formOpen}
        title={editing ? t('accounting.journal.editTitle') : t('accounting.journal.newTitle')}
        onClose={() => setFormOpen(false)}
        width={760}
      >
        {formOpen && (
          <JournalEntryForm
            companyId={empresaId}
            accounts={accounts}
            initial={editing ?? undefined}
            onSaved={() => setFormOpen(false)}
            onCancel={() => setFormOpen(false)}
          />
        )}
      </Drawer>

      <Drawer
        open={!!viewing}
        title={viewing ? `${t('accounting.journal.columns.number')} ${viewing.number ?? '—'}` : ''}
        sub={viewing?.description}
        onClose={() => setViewing(null)}
        width={640}
      >
        {viewing && <JournalEntryDetail entry={viewing} accounts={accounts} />}
      </Drawer>

      <Drawer open={!!reversing} title={t('accounting.journal.reverseTitle')} onClose={() => setReversing(null)}>
        {reversing && (
          <ReverseForm entry={reversing} onDone={() => setReversing(null)} onCancel={() => setReversing(null)} />
        )}
      </Drawer>
    </>
  );
}
