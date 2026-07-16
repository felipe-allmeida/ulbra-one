import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, ListTree, Pencil, Sparkles } from 'lucide-react';
import { type AccountGroup, type LedgerAccount, type LedgerAccountKind, type NormalBalance } from '../api';
import { useCreateLedgerAccount, useLedgerAccounts, useSeedLedgerAccounts, useUpdateLedgerAccount } from '../queries';
import { getErrorMessage } from '../apiError';
import { useToast } from '../toast';
import { P, usePermissions } from '../permissions';
import { Button, Combobox, Drawer, EmptyState, Field, Input, Kpi, PageHeader, Panel, Select } from '../ui';

const GROUPS: AccountGroup[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'];

/** Natural normal balance for a group (Asset/Expense → Debit; others → Credit). */
function naturalBalance(group: AccountGroup): NormalBalance {
  return group === 'Asset' || group === 'Expense' ? 'Debit' : 'Credit';
}

/* ============ Create / edit form ============ */
function LedgerAccountForm({
  accounts,
  initial,
  onSaved,
  onCancel,
}: {
  accounts: LedgerAccount[];
  initial?: LedgerAccount;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [code, setCode] = useState(initial?.code ?? '');
  const [name, setName] = useState(initial?.name ?? '');
  const [group, setGroup] = useState<AccountGroup>(initial?.accountGroup ?? 'Asset');
  const [kind, setKind] = useState<LedgerAccountKind>(initial?.kind ?? 'Analytical');
  const [normalBalance, setNormalBalance] = useState<NormalBalance>(initial?.normalBalance ?? naturalBalance('Asset'));
  const [parentId, setParentId] = useState<string | null>(initial?.parentId ?? null);

  const create = useCreateLedgerAccount({ onSuccess: () => onSaved() });
  const update = useUpdateLedgerAccount({ onSuccess: () => onSaved() });
  const busy = create.isPending || update.isPending;
  const err = create.error ? getErrorMessage(create.error) : update.error ? getErrorMessage(update.error) : null;
  const isEditing = initial !== undefined;

  // Parent options: same-group synthetic accounts only (exclude self).
  const parentItems = useMemo(
    () =>
      accounts
        .filter((a) => a.kind === 'Synthetic' && a.accountGroup === group && a.id !== initial?.id)
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((a) => ({ id: a.id, label: `${a.code} · ${a.name}` })),
    [accounts, group, initial?.id],
  );

  const changeGroup = (g: AccountGroup) => {
    setGroup(g);
    if (!isEditing) setNormalBalance(naturalBalance(g));
    setParentId(null);
  };

  const submit = () => {
    const trimmedCode = code.trim();
    const trimmedName = name.trim();
    if (!trimmedCode || !trimmedName) return;
    if (isEditing) {
      update.mutate({
        id: initial.id,
        body: {
          name: trimmedName,
          accountGroup: group,
          kind,
          normalBalance,
          parentId,
          isActive: initial.isActive,
          // Preserve seed-managed fields (PUT is full-replace).
          reducedCode: initial.reducedCode,
          natureza: initial.natureza,
        },
      });
    } else {
      create.mutate({
        code: trimmedCode,
        name: trimmedName,
        accountGroup: group,
        kind,
        normalBalance,
        parentId,
      });
    }
  };

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
      noValidate
    >
      <div className='edit-grid'>
        <Field
          label={t('accounting.ledger.fields.code')}
          help={isEditing ? t('accounting.ledger.immutableAfterCreate') : undefined}
        >
          <Input
            autoFocus={!isEditing}
            value={code}
            disabled={isEditing}
            placeholder={t('accounting.ledger.placeholders.code')}
            onChange={(e) => setCode(e.target.value)}
          />
        </Field>
        <Field label={t('accounting.ledger.fields.kind')}>
          <Select value={kind} onChange={(e) => setKind(e.target.value as LedgerAccountKind)}>
            <option value='Synthetic'>{t('accounting.kinds.Synthetic')}</option>
            <option value='Analytical'>{t('accounting.kinds.Analytical')}</option>
          </Select>
        </Field>
      </div>
      <Field label={t('accounting.ledger.fields.name')}>
        <Input
          value={name}
          placeholder={t('accounting.ledger.placeholders.name')}
          onChange={(e) => setName(e.target.value)}
        />
      </Field>
      <div className='edit-grid'>
        <Field label={t('accounting.ledger.fields.group')}>
          <Select value={group} onChange={(e) => changeGroup(e.target.value as AccountGroup)}>
            {GROUPS.map((g) => (
              <option key={g} value={g}>
                {t(`accounting.groups.${g}`)}
              </option>
            ))}
          </Select>
        </Field>
        <Field label={t('accounting.ledger.fields.normalBalance')}>
          <Select value={normalBalance} onChange={(e) => setNormalBalance(e.target.value as NormalBalance)}>
            <option value='Debit'>{t('accounting.normalBalance.Debit')}</option>
            <option value='Credit'>{t('accounting.normalBalance.Credit')}</option>
          </Select>
        </Field>
      </div>
      <Field label={t('accounting.ledger.fields.parent')}>
        <Combobox
          value={parentId}
          onChange={setParentId}
          items={parentItems}
          placeholder={t('accounting.ledger.root')}
          emptyText={t('accounting.ledger.noSynthetic')}
        />
      </Field>
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

/* ============ Page ============ */
export function ContabilPlanoPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { can } = usePermissions();
  const canWrite = can(P.accountingWrite);
  const list = useLedgerAccounts({});
  const accounts = list.data ?? [];

  const seed = useSeedLedgerAccounts({
    onSuccess: () => toast.success(t('accounting.ledger.seedDone')),
    onError: (e) => toast.error(getErrorMessage(e)),
  });
  const update = useUpdateLedgerAccount();

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<LedgerAccount | null>(null);
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');

  // Sort by code, compute hierarchy depth via the parentId chain among loaded accounts.
  const rows = useMemo(() => {
    const byId = new Map(accounts.map((a) => [a.id, a]));
    const depthOf = (a: LedgerAccount) => {
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

  // Created / active / inactive counts (the list already includes inactive accounts).
  const counts = useMemo(
    () => ({
      total: accounts.length,
      active: accounts.filter((a) => a.isActive).length,
      inactive: accounts.filter((a) => !a.isActive).length,
    }),
    [accounts],
  );

  // The counts KPIs double as status filters: "all" shows everything, "active"/"inactive" narrow the list.
  const visibleRows = useMemo(
    () =>
      statusFilter === 'all'
        ? rows
        : rows.filter(({ account }) => account.isActive === (statusFilter === 'active')),
    [rows, statusFilter],
  );

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (a: LedgerAccount) => {
    setEditing(a);
    setFormOpen(true);
  };

  const toggleActive = (a: LedgerAccount) =>
    update.mutate({
      id: a.id,
      body: {
        name: a.name,
        accountGroup: a.accountGroup,
        kind: a.kind,
        normalBalance: a.normalBalance,
        parentId: a.parentId,
        isActive: !a.isActive,
        // Preserve seed-managed fields (PUT is full-replace).
        reducedCode: a.reducedCode,
        natureza: a.natureza,
      },
    });

  return (
    <>
      <PageHeader title={t('accounting.ledger.title')} sub={t('accounting.ledger.subtitle')}>
        {canWrite && accounts.length === 0 && !list.isLoading && (
          <Button icon={<Sparkles />} disabled={seed.isPending} onClick={() => seed.mutate()}>
            {seed.isPending ? t('accounting.ledger.seeding') : t('accounting.ledger.seed')}
          </Button>
        )}
        {canWrite && (
          <Button variant='brand' icon={<Plus />} onClick={openCreate}>
            {t('accounting.ledger.new')}
          </Button>
        )}
      </PageHeader>

      {accounts.length > 0 && (
        <div className='kpi-grid'>
          <Kpi
            label={t('accounting.ledger.counts.total')}
            value={String(counts.total)}
            onClick={() => setStatusFilter('all')}
            selected={statusFilter === 'all'}
          />
          <Kpi
            label={t('accounting.ledger.counts.active')}
            value={String(counts.active)}
            onClick={() => setStatusFilter('active')}
            selected={statusFilter === 'active'}
          />
          <Kpi
            label={t('accounting.ledger.counts.inactive')}
            value={String(counts.inactive)}
            onClick={() => setStatusFilter('inactive')}
            selected={statusFilter === 'inactive'}
          />
        </div>
      )}

      {accounts.length === 0 ? (
        <EmptyState
          icon={<ListTree />}
          title={t('accounting.ledger.empty.title')}
          sub={t('accounting.ledger.empty.sub')}
        />
      ) : (
        <Panel title={t('accounting.ledger.count', { count: visibleRows.length })}>
          <table>
            <tbody>
              {visibleRows.map(({ account, depth }) => (
                <tr key={account.id}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, paddingLeft: depth * 20 }}>
                      <span className='t-mono'>{account.code}</span>
                      <span>{account.name}</span>
                      <span className='badge-status badge-pendente'>
                        {t(`accounting.groups.${account.accountGroup}`)}
                      </span>
                      <span
                        className={`badge-status ${account.kind === 'Synthetic' ? 'badge-pendente' : 'badge-aprovado'}`}
                      >
                        {t(`accounting.kinds.${account.kind}`)}
                      </span>
                      {!account.isActive && (
                        <span className='badge-status badge-rejeitado'>{t('accounting.ledger.inactive')}</span>
                      )}
                    </div>
                  </td>
                  <td className='td-action'>
                    {canWrite && (
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <Button icon={<Pencil />} onClick={() => openEdit(account)}>
                          {t('accounting.ledger.edit')}
                        </Button>
                        <Button onClick={() => toggleActive(account)}>
                          {account.isActive ? t('accounting.ledger.deactivate') : t('accounting.ledger.activate')}
                        </Button>
                      </div>
                    )}
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
        title={editing ? t('accounting.ledger.editTitle') : t('accounting.ledger.newTitle')}
        onClose={() => setFormOpen(false)}
      >
        {formOpen && (
          <LedgerAccountForm
            accounts={accounts}
            initial={editing ?? undefined}
            onSaved={() => setFormOpen(false)}
            onCancel={() => setFormOpen(false)}
          />
        )}
      </Drawer>
    </>
  );
}
