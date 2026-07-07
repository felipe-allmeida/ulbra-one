import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, ListTree, Pencil, Trash2 } from 'lucide-react';
import { type AccountKind, type ChartAccount, type ChartType } from '../api';
import { useChartAccounts, useCreateChartAccount, useDeleteChartAccount, useUpdateChartAccount } from '../queries';
import { getErrorMessage } from '../apiError';
import { Button, Input, Select, Combobox, Field, PageHeader, Panel, EmptyState, Drawer, ConfirmDialog } from '../ui';

type NatureValue = 'Debit' | 'Credit' | '';

/* ============ Create / edit form (Drawer body) ============ */
function ChartAccountForm({
  chartType,
  accounts,
  initial,
  onSaved,
  onCancel,
}: {
  chartType: ChartType;
  accounts: ChartAccount[];
  initial?: ChartAccount;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [code, setCode] = useState(initial?.code ?? '');
  const [name, setName] = useState(initial?.name ?? '');
  const [kind, setKind] = useState<AccountKind>(initial?.kind ?? 'Analytical');
  const [parentId, setParentId] = useState<string | null>(initial?.parentId ?? null);
  const [nature, setNature] = useState<NatureValue>(initial?.nature ?? '');

  const create = useCreateChartAccount({ onSuccess: () => onSaved() });
  const update = useUpdateChartAccount({ onSuccess: () => onSaved() });
  const busy = create.isPending || update.isPending;
  const err = create.error ? getErrorMessage(create.error) : update.error ? getErrorMessage(update.error) : null;

  // Parent options: same chartType Synthetic accounts only (exclude self).
  const parentItems = useMemo(
    () =>
      accounts
        .filter((a) => a.kind === 'Synthetic' && a.id !== initial?.id)
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((a) => ({ id: a.id, label: `${a.code} · ${a.name}` })),
    [accounts, initial?.id],
  );

  const submit = () => {
    const trimmedCode = code.trim();
    const trimmedName = name.trim();
    if (!trimmedCode || !trimmedName) return;
    const natureValue: 'Debit' | 'Credit' | null = nature === '' ? null : nature;
    if (initial) {
      update.mutate({
        id: initial.id,
        body: {
          code: trimmedCode,
          name: trimmedName,
          parentId,
          kind,
          nature: natureValue,
          isActive: initial.isActive,
        },
      });
    } else {
      create.mutate({
        chartType,
        code: trimmedCode,
        name: trimmedName,
        parentId,
        kind,
        nature: natureValue,
      });
    }
  };

  const isEditing = initial !== undefined;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
      noValidate
    >
      <div className='edit-grid'>
        <Field label={t('chartAccounts.fields.code')}>
          {isEditing ? (
            <Input value={code} disabled />
          ) : (
            <Input
              autoFocus
              value={code}
              placeholder={t('chartAccounts.placeholders.code')}
              onChange={(e) => setCode(e.target.value)}
            />
          )}
        </Field>
        <Field label={t('chartAccounts.fields.kind')}>
          {isEditing ? (
            <Input value={t(`chartAccounts.kinds.${kind}`)} disabled />
          ) : (
            <Select value={kind} onChange={(e) => setKind(e.target.value as AccountKind)}>
              <option value='Synthetic'>{t('chartAccounts.kinds.Synthetic')}</option>
              <option value='Analytical'>{t('chartAccounts.kinds.Analytical')}</option>
            </Select>
          )}
        </Field>
      </div>
      <Field label={t('chartAccounts.fields.name')}>
        <Input
          value={name}
          placeholder={t('chartAccounts.placeholders.name')}
          onChange={(e) => setName(e.target.value)}
        />
      </Field>
      <Field label={t('chartAccounts.fields.parent')}>
        <Combobox
          value={parentId}
          onChange={setParentId}
          items={parentItems}
          placeholder={t('chartAccounts.root')}
          emptyText={t('chartAccounts.noSynthetic')}
        />
      </Field>
      {chartType === 'Accounting' && (
        <Field label={t('chartAccounts.fields.nature')}>
          <Select value={nature} onChange={(e) => setNature(e.target.value as NatureValue)}>
            <option value=''>{t('chartAccounts.natures.none')}</option>
            <option value='Debit'>{t('chartAccounts.natures.Debit')}</option>
            <option value='Credit'>{t('chartAccounts.natures.Credit')}</option>
          </Select>
        </Field>
      )}
      {err && (
        <div className='create-err' style={{ marginTop: 10 }}>
          {err}
        </div>
      )}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button type='submit' variant='brand' disabled={!code.trim() || !name.trim() || busy}>
          {busy ? t('forms.actions.saving') : t('forms.actions.save')}
        </Button>
        <Button type='button' onClick={onCancel}>
          {t('forms.actions.cancel')}
        </Button>
      </div>
    </form>
  );
}

/* ============ List page ============ */
export function PlanoDeContasPage() {
  const { t } = useTranslation();
  // Single chart type since the Financial axis was removed (AEL-678); this page manages the accounting chart.
  const chartType: ChartType = 'Accounting';
  const list = useChartAccounts({ chartType });
  const accounts = list.data ?? [];

  const update = useUpdateChartAccount();
  const remove = useDeleteChartAccount();

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ChartAccount | null>(null);
  const [toDelete, setToDelete] = useState<ChartAccount | null>(null);
  const [deleteErr, setDeleteErr] = useState<string | null>(null);

  // Sort by code, compute depth by walking parentId chain among loaded accounts.
  const rows = useMemo(() => {
    const byId = new Map(accounts.map((a) => [a.id, a]));
    const depthOf = (a: ChartAccount) => {
      let depth = 0;
      let current = a.parentId ? byId.get(a.parentId) : undefined;
      const guard = new Set<string>([a.id]);
      while (current && !guard.has(current.id)) {
        depth += 1;
        guard.add(current.id);
        current = current.parentId ? byId.get(current.parentId) : undefined;
      }
      return depth;
    };
    return [...accounts].sort((a, b) => a.code.localeCompare(b.code)).map((a) => ({ account: a, depth: depthOf(a) }));
  }, [accounts]);

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (a: ChartAccount) => {
    setEditing(a);
    setFormOpen(true);
  };

  const toggleActive = (a: ChartAccount) =>
    update.mutate({
      id: a.id,
      body: {
        code: a.code,
        name: a.name,
        parentId: a.parentId,
        kind: a.kind,
        nature: a.nature,
        isActive: !a.isActive,
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
      <PageHeader title={t('chartAccounts.title')} sub={t('chartAccounts.subtitle')}>
        <Button variant='brand' icon={<Plus />} onClick={openCreate}>
          {t('chartAccounts.new')}
        </Button>
      </PageHeader>

      {accounts.length === 0 ? (
        <EmptyState icon={<ListTree />} title={t('chartAccounts.empty.title')} sub={t('chartAccounts.empty.sub')} />
      ) : (
        <Panel title={t('chartAccounts.count', { count: accounts.length })}>
          <table>
            <tbody>
              {rows.map(({ account, depth }) => (
                <tr key={account.id}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, paddingLeft: depth * 20 }}>
                      <span className='t-mono'>{account.code}</span>
                      <span>{account.name}</span>
                      <span
                        className={`badge-status ${account.kind === 'Synthetic' ? 'badge-pendente' : 'badge-aprovado'}`}
                      >
                        {t(`chartAccounts.kinds.${account.kind}`)}
                      </span>
                      {!account.isActive && (
                        <span className='badge-status badge-rejeitado'>{t('chartAccounts.inactive')}</span>
                      )}
                    </div>
                  </td>
                  <td className='td-action'>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      <Button icon={<Pencil />} onClick={() => openEdit(account)}>
                        {t('chartAccounts.edit')}
                      </Button>
                      <Button onClick={() => toggleActive(account)}>
                        {account.isActive ? t('chartAccounts.deactivate') : t('chartAccounts.activate')}
                      </Button>
                      <Button
                        icon={<Trash2 />}
                        onClick={() => {
                          setDeleteErr(null);
                          setToDelete(account);
                        }}
                      >
                        {t('chartAccounts.delete')}
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {update.error ? <p className='create-err'>{getErrorMessage(update.error)}</p> : null}
        </Panel>
      )}

      <Drawer
        open={formOpen}
        title={editing ? t('chartAccounts.editTitle') : t('chartAccounts.newTitle')}
        onClose={() => setFormOpen(false)}
      >
        {formOpen && (
          <ChartAccountForm
            chartType={chartType}
            accounts={accounts}
            initial={editing ?? undefined}
            onSaved={() => setFormOpen(false)}
            onCancel={() => setFormOpen(false)}
          />
        )}
      </Drawer>

      {toDelete && (
        <ConfirmDialog
          title={t('chartAccounts.deleteTitle')}
          danger
          busy={remove.isPending}
          confirmLabel={t('chartAccounts.delete')}
          message={
            <div>
              <div>{t('chartAccounts.deleteMessage', { name: toDelete.name })}</div>
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
